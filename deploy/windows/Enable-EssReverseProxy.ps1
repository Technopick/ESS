<#
.SYNOPSIS
  Publishes the ESS portal at https://<host> on port 443 through IIS when another IIS site already owns port 443.
.DESCRIPTION
  Use this when the portal site runs on a custom port (for example 4048) because port 443 is taken. It:
    1. installs IIS URL Rewrite and Application Request Routing (ARR) if missing, and turns on ARR's proxy;
    2. gives the portal site a private plain-HTTP binding on 127.0.0.1 (default port 4049) for the proxy to call;
    3. on the site that owns port 443 (or Default Web Site), adds an HTTPS binding for the host name with the
       certificate, and a rewrite rule that forwards only that host name to the portal, with X-Forwarded-Proto and
       X-Forwarded-For set and the Host header preserved (WebSockets for live notifications pass through ARR);
    4. sets the portal's public address (used in e-mail links) to https://<host>.
  The portal trusts X-Forwarded-* only from the loopback address, so only this proxy can set them.
  Safe to run again. Run as Administrator.
.EXAMPLE
  .\Enable-EssReverseProxy.ps1 -HostName ess.technopickltd.com -CertificateThumbprint 'A1B2...'
.EXAMPLE
  .\Enable-EssReverseProxy.ps1 -HostName ess.technopickltd.com -CertificateThumbprint 'A1B2...' -FrontSiteName 'Default Web Site' -UrlRewriteMsi C:\Install\rewrite_amd64_en-US.msi -ArrMsi C:\Install\requestRouter_amd64.msi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $CertificateThumbprint,
    [string] $PortalSiteName = 'ESS Portal',
    [string] $PortalAppPoolName = 'EssPortal',
    # Private port on 127.0.0.1 that the proxy uses to reach the portal. Not reachable from the network.
    [int] $LoopbackPort = 4049,
    # Site that receives https://<host> on 443. Default: the site that already has an HTTPS binding on 443, else Default Web Site.
    [string] $FrontSiteName,
    # Local copies of the installers, for servers without internet access.
    [string] $UrlRewriteMsi,
    [string] $ArrMsi
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Write-Step([string] $m) { Write-Host "==> $m" -ForegroundColor Cyan }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator.' }
$CertificateThumbprint = $CertificateThumbprint -replace '\s', ''
$cert = Get-Item "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $cert) { throw "Certificate $CertificateThumbprint is not in LocalMachine\My." }

# ---------------------------------------------------------------- URL Rewrite and ARR
$inetsrv = Join-Path $env:windir 'System32\inetsrv'
function Install-Msi([string] $Name, [string] $Dll, [string] $LocalMsi, [string] $Url, [string] $Page) {
    if (Test-Path (Join-Path $inetsrv $Dll)) { Write-Host "$Name is installed."; return }
    $msi = $LocalMsi
    if (-not $msi) {
        $msi = Join-Path $env:TEMP ([IO.Path]::GetFileName($Url))
        Write-Host "Downloading $Name..."
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest $Url -OutFile $msi -UseBasicParsing
        } catch { throw "Could not download $Name ($($_.Exception.Message)). Download it from $Page, then run this script again with -$(if ($Dll -eq 'rewrite.dll') { 'UrlRewriteMsi' } else { 'ArrMsi' }) <path to the .msi>." }
    }
    $p = Start-Process msiexec.exe -ArgumentList '/i', "`"$msi`"", '/qn', '/norestart' -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { throw "$Name installation failed (msiexec exit code $($p.ExitCode))." }
    Write-Host "$Name installed."
}
Write-Step 'Checking IIS URL Rewrite and Application Request Routing'
Install-Msi 'IIS URL Rewrite 2.1' 'rewrite.dll' $UrlRewriteMsi `
    'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi' 'https://www.iis.net/downloads/microsoft/url-rewrite'
Install-Msi 'IIS Application Request Routing 3.0' 'requestRouter.dll' $ArrMsi `
    'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi' 'https://www.iis.net/downloads/microsoft/application-request-routing'

Import-Module WebAdministration
Import-Module IISAdministration
$apphost = 'MACHINE/WEBROOT/APPHOST'

Write-Step 'Turning on the ARR proxy'
Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name 'enabled' -Value $true
Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name 'preserveHostHeader' -Value $true
Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name 'reverseRewriteHostInResponseHeaders' -Value $false
Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name 'timeout' -Value '00:02:00'
foreach ($v in 'HTTP_X_FORWARDED_PROTO', 'HTTP_X_FORWARDED_FOR') {
    if (-not (Get-WebConfiguration -PSPath $apphost -Filter "system.webServer/rewrite/allowedServerVariables/add[@name='$v']")) {
        Add-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = $v }
    }
}

# ---------------------------------------------------------------- sites
$sm = Get-IISServerManager
$portal = $sm.Sites[$PortalSiteName]
if (-not $portal) { throw "IIS site '$PortalSiteName' was not found. Install the portal first (setup or Install-EssPortal.ps1)." }
if (-not $FrontSiteName) {
    $owner = $sm.Sites | Where-Object { $_.Name -ne $PortalSiteName -and ($_.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation.Split(':')[1] -eq '443' }) } | Select-Object -First 1
    $FrontSiteName = if ($owner) { $owner.Name } else { 'Default Web Site' }
}
$front = $sm.Sites[$FrontSiteName]
if (-not $front) { throw "IIS site '$FrontSiteName' was not found. Pass -FrontSiteName with the site that owns port 443." }
if ($front.Name -eq $portal.Name) { throw 'The front site must be a different site from the portal.' }
Write-Host "Port 443 site: '$FrontSiteName'. Portal site: '$PortalSiteName'."

Write-Step "Adding the private binding http://127.0.0.1:$LoopbackPort to '$PortalSiteName'"
$loopback = "127.0.0.1:${LoopbackPort}:"
if (-not ($portal.Bindings | Where-Object { $_.Protocol -eq 'http' -and $_.BindingInformation -eq $loopback })) { [void] $portal.Bindings.Add([string]$loopback, 'http') }

if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
    $other = @(Get-NetTCPConnection -State Listen -LocalPort 443 -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -ne 4 } |
        ForEach-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } | Select-Object -Unique)
    if ($other) { throw "Port 443 is held by $($other -join ', '), a program outside IIS. Stop it (or move it to another port) first; a reverse proxy in IIS cannot share 443 with it." }
}

Write-Step "Adding https://${HostName} (port 443) to '$FrontSiteName'"
$frontBinding = "*:443:$HostName"
$existing = $front.Bindings | Where-Object { $_.Protocol -eq 'https' -and $_.BindingInformation -eq $frontBinding }
if ($existing) { $existing.CertificateHash = $cert.GetCertHash(); $existing.CertificateStoreName = 'My' }
else { [void] $front.Bindings.Add([string]$frontBinding, $cert.GetCertHash(), 'My', 'Sni') }
$sm.CommitChanges()
$sm = Get-IISServerManager
foreach ($name in $FrontSiteName, $PortalSiteName) {
    if ($sm.Sites[$name].State -ne 'Started') {
        try { [void] $sm.Sites[$name].Start() } catch { throw "IIS could not start site '$name': $($_.Exception.Message)" }
    }
}

Write-Step "Forwarding $HostName to the portal"
$ruleName = "ESS portal ($HostName)"
$ruleFilter = "system.webServer/rewrite/rules/rule[@name='$ruleName']"
if (Get-WebConfiguration -PSPath $apphost -Location $FrontSiteName -Filter $ruleFilter) {
    Clear-WebConfiguration -PSPath $apphost -Location $FrontSiteName -Filter $ruleFilter
}
Add-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter 'system.webServer/rewrite/rules' -Name '.' `
    -AtIndex 0 -Value @{ name = $ruleName; stopProcessing = 'True' }
Set-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/match" -Name 'url' -Value '(.*)'
Add-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/conditions" -Name '.' `
    -Value @{ input = '{HTTP_HOST}'; pattern = '^' + [regex]::Escape($HostName) + '(:443)?$' }
Add-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/conditions" -Name '.' `
    -Value @{ input = '{HTTPS}'; pattern = '^on$' }
Add-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/serverVariables" -Name '.' `
    -Value @{ name = 'HTTP_X_FORWARDED_PROTO'; value = 'https' }
Add-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/serverVariables" -Name '.' `
    -Value @{ name = 'HTTP_X_FORWARDED_FOR'; value = '{REMOTE_ADDR}' }
Set-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/action" -Name 'type' -Value 'Rewrite'
Set-WebConfigurationProperty -PSPath $apphost -Location $FrontSiteName -Filter "$ruleFilter/action" -Name 'url' -Value "http://127.0.0.1:$LoopbackPort/{R:1}"

# ---------------------------------------------------------------- portal public address
Write-Step "Setting the portal's public address to https://$HostName"
$sm = Get-IISServerManager
$pool = $sm.ApplicationPools[$PortalAppPoolName]
if ($pool) {
    $coll = $pool.GetChildElement('environmentVariables').GetCollection()
    foreach ($old in @($coll | Where-Object { $_['name'] -eq 'App__PublicUrl' })) { $coll.Remove($old) }
    $el = $coll.CreateElement('add'); $el['name'] = 'App__PublicUrl'; $el['value'] = [string]"https://$HostName"; [void] $coll.Add($el)
    $sm.CommitChanges()
    [void] $sm.ApplicationPools[$PortalAppPoolName].Recycle()
} else { Write-Warning "Application pool '$PortalAppPoolName' not found; set App__PublicUrl to https://$HostName yourself." }

Write-Step 'Checking'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$saved = [Net.ServicePointManager]::ServerCertificateValidationCallback
[Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
try {
    Start-Sleep -Seconds 3
    $r = Invoke-WebRequest "http://127.0.0.1:$LoopbackPort/health/live" -Headers @{ 'X-Forwarded-Proto' = 'https' } -UseBasicParsing -TimeoutSec 20
    Write-Host "Portal answers on the private binding ($($r.StatusCode))."
    try {
        $r = Invoke-WebRequest "https://$HostName/health/ready" -UseBasicParsing -TimeoutSec 20
        Write-Host "https://$HostName answers ($($r.StatusCode)). Done." -ForegroundColor Green
    } catch {
        Write-Warning "https://$HostName did not answer from this server ($($_.Exception.Message)). If $HostName does not resolve here yet, add it to DNS or to C:\Windows\System32\drivers\etc\hosts (127.0.0.1 $HostName) and try in a browser."
    }
} catch {
    Write-Warning "The portal did not answer on http://127.0.0.1:$LoopbackPort ($($_.Exception.Message)). Check that the '$PortalSiteName' site is started."
} finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $saved }
