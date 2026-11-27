<#
.SYNOPSIS
  Builds the Windows/IIS deployment package for the ESS portal.

.DESCRIPTION
  Run on a build machine with the .NET 8 SDK and Node.js 22. Produces
  out\ess-portal-<version>-win-x64.zip (plus a .sha256 file) containing:
    app\        the API published for IIS, with the built frontend in app\wwwroot
    sql\        the idempotent database script and the read-only reporting role
    *.ps1       the install, update, database and backup scripts
  The package holds no secrets and no environment-specific settings.

.EXAMPLE
  .\deploy\windows\Build-EssPackage.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'out' }
if (-not $Version) {
    $Version = (& git -C $repo describe --tags --always 2>$null)
    if (-not $Version) { $Version = (Get-Date -Format 'yyyy.MM.dd.HHmm') }
}

# The assembly version must be numeric (2 to 4 parts). A tag such as v1.2.0 gives 1.2.0; a bare commit hash
# (git describe without tags) gives nothing usable, so fall back to 0.0.0 and keep the hash in InformationalVersion.
$assemblyVersion = ($Version -replace '^[vV]', '') -replace '[^0-9\.].*$', ''
if ($assemblyVersion -notmatch '^\d+\.\d+(\.\d+){0,2}$') { $assemblyVersion = '0.0.0' }

$staging = Join-Path $OutputDirectory "ess-portal-$Version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

Invoke-Step 'Publishing the API' {
    dotnet publish (Join-Path $repo 'backend\src\Ess.Api\Ess.Api.csproj') -c Release -r win-x64 --self-contained false `
        -p:Version=$assemblyVersion -p:InformationalVersion=$Version -o (Join-Path $staging 'app')
}

Invoke-Step 'Building the frontend' {
    Push-Location (Join-Path $repo 'frontend')
    try {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
        npm run build
    } finally { Pop-Location }
}

Write-Host '==> Assembling the package' -ForegroundColor Cyan
$wwwroot = Join-Path $staging 'app\wwwroot'
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Copy-Item (Join-Path $repo 'frontend\dist\*') $wwwroot -Recurse -Force
# Development and test settings stay out of production packages.
Get-ChildItem (Join-Path $staging 'app') -Filter 'appsettings.*.json' |
    Where-Object { $_.Name -notin @('appsettings.Production.json') } | Remove-Item -Force

New-Item -ItemType Directory -Path (Join-Path $staging 'sql') | Out-Null
Copy-Item (Join-Path $repo 'deploy\sql\*.sql') (Join-Path $staging 'sql')
Copy-Item (Join-Path $PSScriptRoot '*.ps1') $staging -Exclude 'Build-EssPackage.ps1'
Copy-Item (Join-Path $repo 'docs\deployment-windows-iis.md') $staging
Set-Content -Path (Join-Path $staging 'version.txt') -Value $Version -Encoding ascii

$zip = Join-Path $OutputDirectory "ess-portal-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
Write-Host "Package: $zip" -ForegroundColor Green
Write-Host "SHA-256: $hash"
