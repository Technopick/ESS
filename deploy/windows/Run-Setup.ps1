<#
.SYNOPSIS
  Driver used by EssPortalSetup-x64.exe. Not meant to be run by hand: use Install-EssPortal.ps1 directly instead.
.DESCRIPTION
  Reads its inputs from ESS_SETUP_* environment variables set by the setup program (removing them at once so secrets
  never reach a command line or file), installs the prerequisites, then runs Initialize-EssDatabase.ps1 and
  Install-EssPortal.ps1 from the same folder.
#>
$ErrorActionPreference = 'Stop'
$in = @{}
$code = 1
$transcript = $false
try {
    # Copy, then clear. [Environment] is used rather than Remove-Item because an empty variable can be listed by
    # Get-ChildItem env: yet be "not found" by Remove-Item.
    foreach ($v in @(Get-ChildItem env: | Where-Object Name -like 'ESS_SETUP_*')) {
        $in[$v.Name.Substring(10)] = $v.Value
        [Environment]::SetEnvironmentVariable($v.Name, $null, 'Process')
    }
    function Sec([string] $v) { if ($v) { ConvertTo-SecureString $v -AsPlainText -Force } else { $null } }
    function Step([string] $m) { Write-Host ''; Write-Host "==> $m" -ForegroundColor Cyan }
    if ($in.LOG) { Start-Transcript -Path $in.LOG -Append | Out-Null; $transcript = $true }
    Get-ChildItem $PSScriptRoot -Filter *.ps1 | Unblock-File

    Step 'Installing the IIS features'
    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        $features = 'Web-Server', 'Web-WebSockets', 'Web-AppInit', 'Web-Http-Redirect', 'Web-Filtering', 'Web-Mgmt-Console', 'Web-Scripting-Tools'
        $missing = @($features | Where-Object { -not (Get-WindowsFeature $_).Installed })
        if ($missing) { Install-WindowsFeature -Name $missing | Out-Null; Write-Host "Added: $($missing -join ', ')" } else { Write-Host 'Already installed.' }
    } elseif (Get-Command Enable-WindowsOptionalFeature -ErrorAction SilentlyContinue) {
        $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-CommonHttpFeatures', 'IIS-DefaultDocument', 'IIS-StaticContent', 'IIS-HttpErrors',
            'IIS-HttpRedirect', 'IIS-RequestFiltering', 'IIS-HttpLogging', 'IIS-WebSockets', 'IIS-ApplicationInit', 'IIS-ManagementConsole', 'IIS-ManagementScriptingTools'
        $missing = @($features | Where-Object { (Get-WindowsOptionalFeature -Online -FeatureName $_).State -ne 'Enabled' })
        if ($missing) { Enable-WindowsOptionalFeature -Online -FeatureName $missing -All -NoRestart | Out-Null; Write-Host "Enabled: $($missing -join ', ')" } else { Write-Host 'Already enabled.' }
    }

    Step 'Checking the ASP.NET Core 8 Hosting Bundle'
    $ancm = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    $hasRuntime = (Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.AspNetCore.App" -Directory -ErrorAction SilentlyContinue | Where-Object Name -like '8.*')
    # The module must also be registered in IIS: if IIS was enabled after the bundle was installed, it is not, and the
    # bundle has to be repaired.
    $appHost = Join-Path $env:windir 'System32\inetsrv\config\applicationHost.config'
    $registered = (Test-Path $appHost) -and (Select-String -Path $appHost -Pattern 'AspNetCoreModuleV2' -SimpleMatch -Quiet)
    if (-not (Test-Path $ancm) -or -not $hasRuntime -or -not $registered) {
        $file = Join-Path $env:TEMP 'dotnet-hosting-8-win.exe'
        Write-Host "Downloading from $($in.HOSTING_BUNDLE_URL)"
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest $in.HOSTING_BUNDLE_URL -OutFile $file -UseBasicParsing
        } catch {
            throw "Could not download the Hosting Bundle ($($_.Exception.Message)). Download 'ASP.NET Core 8.0 Hosting Bundle' from https://dotnet.microsoft.com/download/dotnet/8.0 on another machine, install it here, then run setup again."
        }
        $mode = if ((Test-Path $ancm) -and $hasRuntime) { '/repair' } else { '/install' }
        $p = Start-Process $file -ArgumentList $mode, '/quiet', '/norestart' -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010) { throw "The Hosting Bundle installer failed with code $($p.ExitCode)." }
        & iisreset /noforce | Out-Null
        Write-Host 'Installed.'
    } else { Write-Host 'Already installed.' }
    # Pick up PATH changes made by installers that ran during this setup.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')

    Step 'Checking PostgreSQL'
    $psql = Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $psql -and -not (Get-Command psql -ErrorAction SilentlyContinue)) {
        throw 'PostgreSQL was not found. Install PostgreSQL 16 (https://www.postgresql.org/download/windows/), or at least its command line tools when the database is on another server, then run setup again.'
    }
    Write-Host 'Found.'

    if ($in.SELF_SIGNED) {
        Step "Creating a self-signed certificate for $($in.HOST)"
        $cert = New-SelfSignedCertificate -DnsName $in.HOST -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(2) -FriendlyName "ESS portal (self-signed)"
        $in.CERT = $cert.Thumbprint
        Write-Warning 'Browsers will warn about this certificate. Replace it with the company certificate before staff use the portal.'
    }

    Step 'Creating or upgrading the database'
    & (Join-Path $PSScriptRoot 'Initialize-EssDatabase.ps1') -Server $in.DB_SERVER -Port ([int]$in.DB_PORT) -DatabaseName $in.DB_NAME -AppUser $in.DB_USER `
        -AdminPassword (Sec $in.DB_ADMIN_PASSWORD) -AppPassword (Sec $in.DB_PASSWORD)

    Step 'Installing the portal'
    # Quote the password so characters such as ; or = in it cannot break the connection string.
    $cs = "Host=$($in.DB_SERVER);Port=$($in.DB_PORT);Database=$($in.DB_NAME);Username=$($in.DB_USER);Password='$($in.DB_PASSWORD -replace "'", "''")'"
    $install = @{
        HostName = $in.HOST; CertificateThumbprint = $in.CERT; DatabaseConnectionString = (Sec $cs)
        Environment = $in.ENVIRONMENT; InstallRoot = $in.INSTALL_ROOT; DataRoot = $in.DATA_ROOT
        HttpsPort = $(if ($in.HTTPS_PORT) { [int]$in.HTTPS_PORT } else { 443 })
        SiteName = $in.SITE; AppPoolName = $in.POOL; InstallPrerequisites = $true
    }
    if ($in.KEY_CERT) { $install.KeyProtectionCertificateThumbprint = $in.KEY_CERT }
    if ($in.SERVICE_ACCOUNT) { $install.ServiceAccount = New-Object pscredential ($in.SERVICE_ACCOUNT, (Sec $in.SERVICE_PASSWORD)) }
    if ($in.SMTP_HOST) {
        $install.SmtpHost = $in.SMTP_HOST; $install.SmtpPort = [int]$in.SMTP_PORT; $install.SmtpFromAddress = $in.SMTP_FROM
        if ($in.SMTP_USER) { $install.SmtpUserName = $in.SMTP_USER; $install.SmtpPassword = (Sec $in.SMTP_PASSWORD) }
    }
    if ($in.SMS_PROVIDER -eq 'MobileSasa') { $install.SmsProvider = 'MobileSasa'; $install.SmsApiKey = (Sec $in.SMS_KEY); $install.SmsSenderId = $in.SMS_SENDER }
    if ($in.ADMIN_USER) { $install.BootstrapAdminUserName = $in.ADMIN_USER; $install.BootstrapAdminPassword = (Sec $in.ADMIN_PASSWORD) }
    & (Join-Path $PSScriptRoot 'Install-EssPortal.ps1') @install

    if ($in.REVERSE_PROXY) {
        Step "Publishing https://$($in.HOST) on port 443 through the IIS reverse proxy"
        & (Join-Path $PSScriptRoot 'Enable-EssReverseProxy.ps1') -HostName $in.HOST -CertificateThumbprint $in.CERT -PortalSiteName $in.SITE -PortalAppPoolName $in.POOL
    }

    # Keep the operations scripts and guide next to the installed site for backups and updates.
    Copy-Item (Join-Path $PSScriptRoot '*.ps1') $in.INSTALL_ROOT -Exclude 'Run-Setup.ps1' -Force
    Copy-Item (Join-Path $PSScriptRoot 'deployment-windows-iis.md') $in.INSTALL_ROOT -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $PSScriptRoot 'sql') $in.INSTALL_ROOT -Recurse -Force
    $code = 0
} catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    if ($transcript) { try { Stop-Transcript | Out-Null } catch { } }
}
exit $code
