<#
.SYNOPSIS
  Updates an installed ESS portal to the version in this package.

.DESCRIPTION
  1. takes a backup (database, documents, keys) with Backup-EssDatabase.ps1,
  2. shows a maintenance page (app_offline.htm), which also shuts the application down cleanly,
  3. applies the database changes (idempotent script) with Initialize-EssDatabase.ps1,
  4. replaces the application files (settings and secrets live on the application pool, so they are kept),
  5. removes the maintenance page and waits for the readiness check.
  To roll back: restore the previous package's app folder and, if the schema changed, the database backup.

.EXAMPLE
  .\Update-EssPortal.ps1 -HostName ess.example.co.ke -PasswordFile C:\EssData\backup.pwd `
      -AdminPassword (Read-Host -AsSecureString) -AppPassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $PasswordFile,
    [Parameter(Mandatory)] [securestring] $AdminPassword,
    [Parameter(Mandatory)] [securestring] $AppPassword,
    [string] $InstallRoot = 'C:\inetpub\ess-portal',
    [string] $DataRoot = 'C:\EssData',
    [string] $AppPoolName = 'EssPortal',
    [string] $DatabaseServer = 'localhost',
    [string] $DatabaseName = 'ess',
    [string] $AppUser = 'ess_app',
    [int] $HttpsPort = 443,
    [switch] $SkipBackup
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module IISAdministration
$package = $PSScriptRoot
$app = Join-Path $InstallRoot 'app'
if (-not (Test-Path (Join-Path $package 'app\Ess.Api.dll'))) { throw "No application found in $package\app." }
if (-not (Test-Path $app)) { throw "The portal is not installed in $InstallRoot. Use Install-EssPortal.ps1." }

if (-not $SkipBackup) {
    Write-Host '==> Backing up' -ForegroundColor Cyan
    & (Join-Path $package 'Backup-EssDatabase.ps1') -Server $DatabaseServer -DatabaseName $DatabaseName -User $AppUser -PasswordFile $PasswordFile -DataRoot $DataRoot
}

Write-Host '==> Stopping the portal' -ForegroundColor Cyan
Set-Content (Join-Path $app 'app_offline.htm') '<html><body>The portal is being updated. Please try again in a few minutes.</body></html>'
# The ASP.NET Core Module stops the application when app_offline.htm appears; give running jobs time to finish.
Start-Sleep -Seconds 15

try {
    Write-Host '==> Applying database changes' -ForegroundColor Cyan
    & (Join-Path $package 'Initialize-EssDatabase.ps1') -Server $DatabaseServer -DatabaseName $DatabaseName -AppUser $AppUser `
        -AdminPassword $AdminPassword -AppPassword $AppPassword -SqlFolder (Join-Path $package 'sql')

    Write-Host '==> Replacing the application files' -ForegroundColor Cyan
    $previous = Join-Path $InstallRoot ("app-previous")
    if (Test-Path $previous) { Remove-Item $previous -Recurse -Force }
    & robocopy $app $previous /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm | Out-Null
    & robocopy (Join-Path $package 'app') $app /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm /XD logs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the application failed (robocopy exit code $LASTEXITCODE). The previous version is in $previous." }
    foreach ($s in Get-ChildItem $package -Filter '*.ps1') { Copy-Item $s.FullName $InstallRoot -Force }
    Copy-Item (Join-Path $package 'sql') $InstallRoot -Recurse -Force
} finally {
    Write-Host '==> Starting the portal' -ForegroundColor Cyan
    $pool = (Get-IISServerManager).ApplicationPools[$AppPoolName]
    if ($pool.State -ne 'Started') { [void] $pool.Start() }
    Remove-Item (Join-Path $app 'app_offline.htm') -ErrorAction SilentlyContinue
}

$url = "https://${HostName}:$HttpsPort/health/ready"
for ($i = 0; $i -lt 30; $i++) {
    try { if ((Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200) { Write-Host "Updated to $(Get-Content (Join-Path $package 'version.txt'))." -ForegroundColor Green; return } }
    catch { Start-Sleep -Seconds 5 }
}
Write-Warning "The readiness check at $url did not pass. Check $DataRoot\logs. The previous version is in $InstallRoot\app-previous."
