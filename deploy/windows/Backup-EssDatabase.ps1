<#
.SYNOPSIS
  Backs up the ESS portal: database dump, encrypted documents and data-protection keys.

.DESCRIPTION
  Writes <BackupRoot>\<yyyyMMdd-HHmm>\ with ess.dump (pg_dump custom format), files.zip and keys.zip, and removes
  backups older than -RetentionDays. The documents are already encrypted; they can only be read with the keys, so keep
  the keys backup as safe as the database backup. Copy the backup folder off the server (another site or cloud storage).
  With -Register, creates a daily scheduled task (SYSTEM) that runs this script with the same parameters.

.EXAMPLE
  .\Backup-EssDatabase.ps1 -Register -At 01:30 -PasswordFile C:\EssData\backup.pwd
#>
[CmdletBinding()]
param(
    [string] $Server = 'localhost',
    [int] $Port = 5432,
    [string] $DatabaseName = 'ess',
    [string] $User = 'ess_app',
    # File holding the database password protected with DPAPI for the account that runs the backup
    # (create it with: Read-Host -AsSecureString | ConvertFrom-SecureString | Set-Content <file>, as that account).
    [Parameter(Mandatory)] [string] $PasswordFile,
    [string] $DataRoot = 'C:\EssData',
    [string] $BackupRoot = (Join-Path $DataRoot 'backups'),
    [int] $RetentionDays = 30,
    [string] $PgDumpPath,
    [switch] $Register,
    [string] $At = '01:30'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Register) {
    $argsLine = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Server $Server -Port $Port -DatabaseName $DatabaseName -User $User " +
                "-PasswordFile `"$PasswordFile`" -DataRoot `"$DataRoot`" -BackupRoot `"$BackupRoot`" -RetentionDays $RetentionDays"
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argsLine
    $trigger = New-ScheduledTaskTrigger -Daily -At $At
    Register-ScheduledTask -TaskName 'ESS Portal backup' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
    Write-Host "Scheduled daily backup at $At. Create $PasswordFile as SYSTEM (for example with PsExec -s) so the task can read it." -ForegroundColor Green
    return
}

if (-not $PgDumpPath) {
    $PgDumpPath = Get-ChildItem "$env:ProgramFiles\PostgreSQL\*\bin\pg_dump.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $PgDumpPath) { throw 'pg_dump.exe was not found. Pass -PgDumpPath.' }
}
$target = Join-Path $BackupRoot (Get-Date -Format 'yyyyMMdd-HHmm')
New-Item -ItemType Directory -Path $target -Force | Out-Null

$secure = Get-Content $PasswordFile | ConvertTo-SecureString
$b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
try {
    & $PgDumpPath -h $Server -p $Port -U $User -d $DatabaseName -Fc -Z 6 -f (Join-Path $target 'ess.dump')
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit code $LASTEXITCODE)." }
} finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }

foreach ($part in 'files', 'keys') {
    $src = Join-Path $DataRoot $part
    if ((Test-Path $src) -and (Get-ChildItem $src -Recurse -File | Select-Object -First 1)) {
        Compress-Archive -Path (Join-Path $src '*') -DestinationPath (Join-Path $target "$part.zip") -CompressionLevel Optimal
    }
}
Get-ChildItem $BackupRoot -Directory | Where-Object { $_.Name -match '^\d{8}-\d{4}$' -and $_.CreationTime -lt (Get-Date).AddDays(-$RetentionDays) } |
    Remove-Item -Recurse -Force
Write-Host "Backup written to $target" -ForegroundColor Green
