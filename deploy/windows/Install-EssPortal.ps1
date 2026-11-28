<#
.SYNOPSIS
  Installs or reconfigures the ESS portal on Windows Server with IIS.

.DESCRIPTION
  Run as Administrator from the extracted package folder. The script:
    1. checks (and with -InstallPrerequisites installs) the IIS features the portal needs,
    2. checks the ASP.NET Core 8 Hosting Bundle,
    3. copies the application to -InstallRoot and creates the data folders under -DataRoot,
    4. creates an application pool that is always running (background jobs, NAV sync, reminders),
    5. creates the IIS site with HTTPS on the certificate you name,
    6. grants the pool identity the least file access it needs,
    7. stores settings and secrets as environment variables on the application pool
       (applicationHost.config is readable by Administrators and SYSTEM only), never in files in the site,
    8. starts the site and waits for the readiness check.
  Run Initialize-EssDatabase.ps1 first. Re-running the script is safe; it updates the configuration in place.

.EXAMPLE
  $db  = Read-Host 'Database connection string' -AsSecureString
  .\Install-EssPortal.ps1 -HostName ess.example.co.ke -CertificateThumbprint 'A1B2...' -DatabaseConnectionString $db `
      -ServiceAccount (Get-Credential 'CONTOSO\svc-ess') -InstallPrerequisites
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $CertificateThumbprint,
    [Parameter(Mandatory)] [securestring] $DatabaseConnectionString,
    [securestring] $JwtSigningKey,
    [string] $InstallRoot = 'C:\inetpub\ess-portal',
    [string] $DataRoot = 'C:\EssData',
    [string] $SiteName = 'ESS Portal',
    [string] $AppPoolName = 'EssPortal',
    [ValidateSet('Production', 'UAT')] [string] $Environment = 'Production',
    [int] $HttpsPort = 443,
    # Domain account for the pool. Needed when NAV uses Windows authentication; otherwise ApplicationPoolIdentity is used.
    [pscredential] $ServiceAccount,
    # Thumbprint of a LocalMachine\My certificate that protects the data-protection keys. Recommended: it lets the keys
    # (and so the encrypted documents and stored NAV credentials) be restored on another server. Without it, keys use machine DPAPI.
    [string] $KeyProtectionCertificateThumbprint,
    [string] $SmtpHost, [int] $SmtpPort = 587, [string] $SmtpUserName, [securestring] $SmtpPassword, [string] $SmtpFromAddress,
    [ValidateSet('None', 'MobileSasa', 'AfricasTalking', 'Log')] [string] $SmsProvider = 'None',
    [securestring] $SmsApiKey, [string] $SmsSenderId,
    # First installation only: creates the initial administrator when the database has no users.
    [string] $BootstrapAdminUserName, [securestring] $BootstrapAdminPassword,
    [switch] $InstallPrerequisites,
    [switch] $SkipStart
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }
function ConvertFrom-Secure([securestring] $s) {
    if (-not $s) { return $null }
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

# ---------------------------------------------------------------- checks
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator.' }
$package = $PSScriptRoot
if (-not (Test-Path (Join-Path $package 'app\Ess.Api.dll'))) { throw "No application found in $package\app. Run the script from the extracted package." }

Write-Step 'Checking IIS features'
if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
    # Windows Server
    $features = 'Web-Server', 'Web-WebSockets', 'Web-AppInit', 'Web-Http-Redirect', 'Web-Filtering', 'Web-Mgmt-Console', 'Web-Scripting-Tools'
    $missing = @($features | Where-Object { -not (Get-WindowsFeature $_).Installed })
    if ($missing) {
        if (-not $InstallPrerequisites) { throw "Missing Windows features: $($missing -join ', '). Re-run with -InstallPrerequisites or install them." }
        Install-WindowsFeature -Name $missing | Out-Null
    }
} elseif (Get-Command Get-WindowsOptionalFeature -ErrorAction SilentlyContinue) {
    # Windows 10/11 (for UAT or a small site): the same features under their client names.
    $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-CommonHttpFeatures', 'IIS-DefaultDocument', 'IIS-StaticContent', 'IIS-HttpErrors',
        'IIS-HttpRedirect', 'IIS-RequestFiltering', 'IIS-HttpLogging', 'IIS-WebSockets', 'IIS-ApplicationInit', 'IIS-ManagementConsole', 'IIS-ManagementScriptingTools'
    $missing = @($features | Where-Object { (Get-WindowsOptionalFeature -Online -FeatureName $_).State -ne 'Enabled' })
    if ($missing) {
        if (-not $InstallPrerequisites) { throw "Missing Windows features: $($missing -join ', '). Re-run with -InstallPrerequisites or enable them in 'Turn Windows features on or off'." }
        Enable-WindowsOptionalFeature -Online -FeatureName $missing -All -NoRestart | Out-Null
    }
} else {
    Write-Warning 'Could not check Windows features: make sure IIS, WebSocket Protocol and Application Initialization are enabled.'
}

Write-Step 'Checking the ASP.NET Core 8 Hosting Bundle'
$ancm = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
$runtimes = (& dotnet --list-runtimes 2>$null) -join "`n"
if (-not (Test-Path $ancm) -or $runtimes -notmatch 'Microsoft\.AspNetCore\.App 8\.') {
    throw 'Install the ASP.NET Core 8.0 Hosting Bundle (https://dotnet.microsoft.com/download/dotnet/8.0), then run iisreset and this script again.'
}
Import-Module IISAdministration

$cert = Get-Item "Cert:\LocalMachine\My\$($CertificateThumbprint -replace '\s','')" -ErrorAction SilentlyContinue
if (-not $cert) { throw "Certificate $CertificateThumbprint is not in LocalMachine\My." }
if ($cert.NotAfter -lt (Get-Date).AddDays(30)) { Write-Warning "The HTTPS certificate expires on $($cert.NotAfter.ToString('yyyy-MM-dd'))." }
if ($KeyProtectionCertificateThumbprint -and -not (Test-Path "Cert:\LocalMachine\My\$($KeyProtectionCertificateThumbprint -replace '\s','')")) {
    throw "Key protection certificate $KeyProtectionCertificateThumbprint is not in LocalMachine\My."
}

# ---------------------------------------------------------------- files
Write-Step "Copying the application to $InstallRoot"
$app = Join-Path $InstallRoot 'app'
$folders = @{ files = Join-Path $DataRoot 'files'; keys = Join-Path $DataRoot 'keys'; logs = Join-Path $DataRoot 'logs'; backups = Join-Path $DataRoot 'backups' }
foreach ($f in @($app) + $folders.Values) { New-Item -ItemType Directory -Path $f -Force | Out-Null }
$pool = Get-IISAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
if ($pool -and $pool.State -eq 'Started') { Set-Content (Join-Path $app 'app_offline.htm') '<html><body>The portal is being updated. Please try again in a few minutes.</body></html>'; Start-Sleep 3 }
& robocopy (Join-Path $package 'app') $app /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm /XD logs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the application failed (robocopy exit code $LASTEXITCODE)." }
foreach ($s in Get-ChildItem $package -Filter '*.ps1') { Copy-Item $s.FullName $InstallRoot -Force }
if (Test-Path (Join-Path $package 'sql')) { Copy-Item (Join-Path $package 'sql') $InstallRoot -Recurse -Force }

# ---------------------------------------------------------------- application pool
Write-Step "Configuring application pool $AppPoolName"
$sm = Get-IISServerManager
$p = $sm.ApplicationPools[$AppPoolName]
if (-not $p) { $p = $sm.ApplicationPools.Add($AppPoolName) }
$p.ManagedRuntimeVersion = ''                       # No managed code: ASP.NET Core runs in-process
$p.StartMode = 'AlwaysRunning'                      # background jobs must run without a first request
$p.ProcessModel.IdleTimeout = [TimeSpan]::Zero
$p.ProcessModel.LoadUserProfile = $true
$p.Recycling.PeriodicRestart.Time = [TimeSpan]::Zero
$p.Recycling.PeriodicRestart.Schedule.Clear()
[void] $p.Recycling.PeriodicRestart.Schedule.Add([TimeSpan]::FromHours(3))   # daily recycle at 03:00, outside working hours
if ($ServiceAccount) {
    $p.ProcessModel.IdentityType = 'SpecificUser'
    $p.ProcessModel.UserName = [string]$ServiceAccount.UserName
    $p.ProcessModel.Password = $ServiceAccount.GetNetworkCredential().Password
    $identity = $ServiceAccount.UserName
} else {
    $p.ProcessModel.IdentityType = 'ApplicationPoolIdentity'
    $identity = "IIS AppPool\$AppPoolName"
}

$jwt = ConvertFrom-Secure $JwtSigningKey
$envVars = [ordered]@{}
$existing = @{}
foreach ($e in $p.GetChildElement('environmentVariables').GetCollection()) { $existing[$e['name']] = $e['value'] }
if (-not $jwt) {
    $jwt = $existing['Jwt__SigningKey']
    if (-not $jwt) { $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $jwt = [Convert]::ToBase64String($bytes) }
}
$envVars['ASPNETCORE_ENVIRONMENT'] = $Environment
$envVars['ConnectionStrings__Ess'] = ConvertFrom-Secure $DatabaseConnectionString
$envVars['Jwt__SigningKey'] = $jwt
$envVars['App__PublicUrl'] = "https://$HostName" + $(if ($HttpsPort -ne 443) { ":$HttpsPort" } else { '' })
$envVars['Storage__RootPath'] = $folders.files
$envVars['DataProtection__KeysPath'] = $folders.keys
if ($KeyProtectionCertificateThumbprint) { $envVars['DataProtection__CertificateThumbprint'] = $KeyProtectionCertificateThumbprint -replace '\s','' }
$envVars['Serilog__WriteTo__1__Args__path'] = Join-Path $folders.logs 'ess-.json'
$envVars['Database__MigrateOnStartup'] = 'false'
if ($SmtpHost) {
    $envVars['Smtp__Host'] = $SmtpHost; $envVars['Smtp__Port'] = "$SmtpPort"
    if ($SmtpUserName) { $envVars['Smtp__UserName'] = $SmtpUserName }
    if ($SmtpPassword) { $envVars['Smtp__Password'] = ConvertFrom-Secure $SmtpPassword }
    if ($SmtpFromAddress) { $envVars['Smtp__FromAddress'] = $SmtpFromAddress }
}
$envVars['Sms__Provider'] = $SmsProvider
if ($SmsApiKey) { $envVars['Sms__ApiKey'] = ConvertFrom-Secure $SmsApiKey }
if ($SmsSenderId) { $envVars['Sms__SenderId'] = $SmsSenderId }
if ($BootstrapAdminUserName -and $BootstrapAdminPassword) {
    $envVars['Bootstrap__AdminUserName'] = $BootstrapAdminUserName
    $envVars['Bootstrap__AdminPassword'] = ConvertFrom-Secure $BootstrapAdminPassword
}

$coll = $p.GetChildElement('environmentVariables').GetCollection()
foreach ($name in $envVars.Keys) {
    foreach ($old in @($coll | Where-Object { $_['name'] -eq $name })) { $coll.Remove($old) }
    # [string] matters: IIS configuration attributes reject PowerShell-wrapped values (such as Join-Path output) with
    # DISP_E_TYPEMISMATCH.
    $el = $coll.CreateElement('add'); $el['name'] = [string]$name; $el['value'] = [string]$envVars[$name]; [void] $coll.Add($el)
}

# ---------------------------------------------------------------- site
Write-Step "Configuring site $SiteName"

# Port checks before IIS is changed: another IIS site with the same port and host name, or a program outside IIS
# listening on the port, would stop the site from starting.
function Get-PortListeners([int] $Port) {
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { return @() }
    @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
        Where-Object { $_.OwningProcess -ne 4 } |   # PID 4 is http.sys, which IIS shares with others
        ForEach-Object { $proc = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue; "$(if ($proc) { $proc.ProcessName } else { 'PID ' + $_.OwningProcess }) (PID $($_.OwningProcess))" } |
        Select-Object -Unique)
}
function Get-SiteConflicts([int] $Port, [string] $HostHeader, [string] $Protocol) {
    # Same port and same host name, or HTTP and HTTPS on one port, cannot coexist. Another HTTPS site without a host
    # name can share the port with this SNI binding.
    @(foreach ($other in $sm.Sites) {
        if ($other.Name -eq $SiteName) { continue }
        foreach ($b in $other.Bindings) {
            if ($b.Protocol -notin 'http', 'https') { continue }
            $parts = $b.BindingInformation.Split(':')
            if ($parts.Count -ge 3 -and $parts[1] -eq "$Port" -and ($parts[2] -eq $HostHeader -or $b.Protocol -ne $Protocol)) {
                "'$($other.Name)' ($($b.Protocol) $($b.BindingInformation))"
            }
        }
    })
}
$siteConflicts = Get-SiteConflicts $HttpsPort $HostName 'https'
if ($siteConflicts) { throw "Port $HttpsPort is already used by IIS site $($siteConflicts -join ', '). Choose another HTTPS port, or stop or change that site." }
$listeners = Get-PortListeners $HttpsPort
if ($listeners) { throw "Port $HttpsPort is already used by $($listeners -join ', '). Choose another HTTPS port, or stop that program." }

$site = $sm.Sites[$SiteName]
$bindingInfo = "*:${HttpsPort}:$HostName"
if (-not $site) {
    $site = $sm.Sites.Add([string]$SiteName, 'https', [string]$bindingInfo, [string]$app)
    $site.Bindings[0].CertificateHash = $cert.GetCertHash()
    $site.Bindings[0].CertificateStoreName = 'My'
    $site.Bindings[0].SslFlags = 'Sni'
} else {
    $site.Applications['/'].VirtualDirectories['/'].PhysicalPath = [string]$app
    # Drop bindings from an earlier run on another port, so a changed port does not keep the old conflict.
    foreach ($old in @($site.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation -ne $bindingInfo })) { $site.Bindings.Remove($old) }
    $https = $site.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation -eq $bindingInfo }
    if (-not $https) { $https = $site.Bindings.Add([string]$bindingInfo, $cert.GetCertHash(), 'My', 'Sni') } else { $https.CertificateHash = $cert.GetCertHash() }
}
# Plain HTTP on port 80 only redirects to HTTPS, and only for the standard port 443. With a custom port, or when port 80
# is taken by a program outside IIS, there is no HTTP binding: users open https://host:port directly.
$httpBinding = "*:80:$HostName"
$wantHttp = $HttpsPort -eq 443
if ($wantHttp -and ((Get-PortListeners 80) -or (Get-SiteConflicts 80 $HostName 'http'))) {
    Write-Warning "Port 80 is in use, so there is no HTTP-to-HTTPS redirect. Users must type https://."
    $wantHttp = $false
}
# The private 127.0.0.1 binding used by Enable-EssReverseProxy.ps1 is kept.
foreach ($old in @($site.Bindings | Where-Object { $_.Protocol -eq 'http' -and -not $_.BindingInformation.StartsWith('127.0.0.1:') -and (-not $wantHttp -or $_.BindingInformation -ne $httpBinding) })) { $site.Bindings.Remove($old) }
if ($wantHttp -and -not ($site.Bindings | Where-Object { $_.Protocol -eq 'http' -and $_.BindingInformation -eq $httpBinding })) { [void] $site.Bindings.Add($httpBinding, 'http') }
$site.Applications['/'].ApplicationPoolName = [string]$AppPoolName
$site.ApplicationDefaults.ApplicationPoolName = [string]$AppPoolName
$site.Applications['/'].SetAttributeValue('preloadEnabled', $true)
$site.ServerAutoStart = $true
$sm.CommitChanges()

if ($HttpsPort -ne 443 -and (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
    $ruleName = "ESS Portal HTTPS $HttpsPort"
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Action Allow -Profile Domain, Private | Out-Null
        Write-Host "Opened TCP port $HttpsPort in Windows Firewall (domain and private networks)."
    }
}

# ---------------------------------------------------------------- permissions
Write-Step "Granting $identity access"
& icacls $app /grant "${identity}:(OI)(CI)RX" /T /Q | Out-Null
foreach ($f in $folders.files, $folders.keys, $folders.logs) {
    & icacls $f /inheritance:r /grant:r "${identity}:(OI)(CI)M" 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' /Q | Out-Null
}
& icacls $folders.backups /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' /Q | Out-Null

# ---------------------------------------------------------------- start
Remove-Item (Join-Path $app 'app_offline.htm') -ErrorAction SilentlyContinue
if ($SkipStart) { Write-Host 'Installed. Start the site when ready.' -ForegroundColor Green; return }
Write-Step 'Starting the portal'
$sm = Get-IISServerManager
if ($sm.ApplicationPools[$AppPoolName].State -ne 'Started') { [void] $sm.ApplicationPools[$AppPoolName].Start() } else { [void] $sm.ApplicationPools[$AppPoolName].Recycle() }
if ($sm.Sites[$SiteName].State -ne 'Started') {
    try { [void] $sm.Sites[$SiteName].Start() }
    catch {
        $who = @(Get-PortListeners $HttpsPort) + @(Get-SiteConflicts $HttpsPort $HostName 'https')
        throw "IIS could not start the site on port $HttpsPort$(if ($who) { ' (in use by ' + ($who -join ', ') + ')' }). Run setup again with another HTTPS port, for example 4048. Details: $($_.Exception.Message)"
    }
}

# Readiness check. Windows PowerShell 5.1 defaults to old TLS versions, and a self-signed certificate or a host name
# that DNS does not resolve yet on this server would make the check fail although the portal is fine, so: TLS 1.2,
# certificate errors ignored for this check only, and a fallback through the loopback address with SNI-free HTTP.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$savedCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$url = "https://${HostName}:$HttpsPort/health/ready"
$ready = $false
try {
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
            $ready = $r.StatusCode -eq 200
        } catch {
            if ($_.Exception.Message -match 'remote name could not be resolved') {
                # DNS does not know the host name on this server yet: ask IIS through the plain-HTTP binding instead.
                # HttpWebRequest is used because Invoke-WebRequest in Windows PowerShell 5.1 cannot set the Host header.
                $req = [Net.HttpWebRequest]::Create('http://127.0.0.1/health/ready')
                $req.Host = $HostName
                $req.AllowAutoRedirect = $false
                $req.Timeout = 10000
                $resp = $null
                try { $resp = $req.GetResponse() } catch [Net.WebException] { $resp = $_.Exception.Response } catch { }
                if ($resp) {
                    $status = [int]$resp.StatusCode
                    $resp.Close()
                    $ready = $status -in 200, 301, 302, 307, 308   # 3xx: the app answered with its redirect to HTTPS
                }
                if ($ready) { Write-Warning "$HostName does not resolve on this server yet. Add it to DNS (or the hosts file) so users can reach the portal." }
            }
        }
        if (-not $ready) { Start-Sleep -Seconds 5 }
    }
} finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $savedCallback }
if ($ready) { Write-Host "The portal is running: https://$HostName" -ForegroundColor Green }
else { Write-Warning "The readiness check at $url did not pass. Check $($folders.logs) and the Windows Application event log." }
if ($BootstrapAdminUserName) {
    Write-Warning 'Sign in as the bootstrap administrator, then re-run this script without -BootstrapAdminUserName/-BootstrapAdminPassword to remove them.'
} else {
    $sm = Get-IISServerManager
    $coll = $sm.ApplicationPools[$AppPoolName].GetChildElement('environmentVariables').GetCollection()
    foreach ($n in 'Bootstrap__AdminUserName', 'Bootstrap__AdminPassword') { $old = $coll | Where-Object { $_['name'] -eq $n }; if ($old) { $coll.Remove($old) } }
    $sm.CommitChanges()
}
