<#
.SYNOPSIS
  Creates or upgrades the ESS portal database on PostgreSQL 16.

.DESCRIPTION
  First run: creates the application login (-AppUser) and the database it owns, then applies the schema.
  Later runs: apply only the schema changes (the script is idempotent). Also prepares the read-only
  "ess_reporting" role for Power BI. Passwords are passed to psql through standard input and the
  PGPASSWORD environment variable of the child process, never on the command line.

.EXAMPLE
  .\Initialize-EssDatabase.ps1 -AdminPassword (Read-Host 'postgres password' -AsSecureString) `
      -AppPassword (Read-Host 'ess_app password' -AsSecureString)
#>
[CmdletBinding()]
param(
    [string] $Server = 'localhost',
    [int] $Port = 5432,
    [string] $AdminUser = 'postgres',
    [Parameter(Mandatory)] [securestring] $AdminPassword,
    [string] $DatabaseName = 'ess',
    [string] $AppUser = 'ess_app',
    [Parameter(Mandatory)] [securestring] $AppPassword,
    [string] $PsqlPath,
    [string] $SqlFolder = (Join-Path $PSScriptRoot 'sql')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertFrom-Secure([securestring] $s) {
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}
if ($DatabaseName -notmatch '^[a-z_][a-z0-9_]*$' -or $AppUser -notmatch '^[a-z_][a-z0-9_]*$') { throw 'Use lower-case letters, digits and underscores for the database and user names.' }
if (-not $PsqlPath) {
    $PsqlPath = Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $PsqlPath) { $PsqlPath = (Get-Command psql -ErrorAction SilentlyContinue).Source }
    if (-not $PsqlPath) { throw 'psql.exe was not found. Install the PostgreSQL client tools or pass -PsqlPath.' }
}
foreach ($f in 'ess-schema.sql', 'reporting-role.sql') { if (-not (Test-Path (Join-Path $SqlFolder $f))) { throw "Missing $SqlFolder\$f." } }

function Invoke-Psql([string] $User, [securestring] $Password, [string] $Database, [string] $Sql, [string] $File) {
    $env:PGPASSWORD = ConvertFrom-Secure $Password
    try {
        $psqlArgs = @('-h', $Server, '-p', $Port, '-U', $User, '-d', $Database, '-v', 'ON_ERROR_STOP=1', '-q', '-X')
        if ($File) { & $PsqlPath @psqlArgs -f $File } else { $Sql | & $PsqlPath @psqlArgs }
        if ($LASTEXITCODE -ne 0) { throw "psql failed (exit code $LASTEXITCODE)." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}

$appPwd = (ConvertFrom-Secure $AppPassword).Replace("'", "''")
if ($appPwd.Length -lt 16) { throw 'Use an application password of at least 16 characters.' }

Write-Host '==> Creating the application login and database (if missing)' -ForegroundColor Cyan
Invoke-Psql $AdminUser $AdminPassword 'postgres' @"
DO `$`$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$AppUser') THEN
    CREATE ROLE $AppUser LOGIN PASSWORD '$appPwd';
  ELSE
    ALTER ROLE $AppUser WITH LOGIN PASSWORD '$appPwd';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ess_reporting') THEN
    CREATE ROLE ess_reporting NOLOGIN;
  END IF;
END `$`$;
SELECT 'CREATE DATABASE $DatabaseName OWNER $AppUser ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = '$DatabaseName')\gexec
REVOKE ALL ON DATABASE $DatabaseName FROM PUBLIC;
GRANT CONNECT ON DATABASE $DatabaseName TO $AppUser, ess_reporting;
"@

Write-Host '==> Applying the schema' -ForegroundColor Cyan
Invoke-Psql $AppUser $AppPassword $DatabaseName -File (Join-Path $SqlFolder 'ess-schema.sql')

Write-Host '==> Granting the reporting role read access to the reporting views' -ForegroundColor Cyan
Invoke-Psql $AppUser $AppPassword $DatabaseName -File (Join-Path $SqlFolder 'reporting-role.sql')

Write-Host "Database $DatabaseName is ready. Connection string for the portal:" -ForegroundColor Green
Write-Host "  Host=$Server;Port=$Port;Database=$DatabaseName;Username=$AppUser;Password=<the ess_app password>;SSL Mode=Prefer"
