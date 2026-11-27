# Deploying the ESS portal on Windows Server and IIS

Version 1.0, 27 November 2026. This guide covers the customer's target: NAV 2016 on-premises, with the portal on Windows Server and IIS.

## 1. Layout

```
Internet / LAN ──HTTPS──> IIS site "ESS Portal" (one site, one app pool "EssPortal", always running)
                             └── C:\inetpub\ess-portal\app        ASP.NET Core 8 API + built React app (wwwroot)
                                   ├── SOAP :7047 / OData V4 :7048 ──> NAV 2016 service tier (DynamicsNAV90)
                                   ├── PostgreSQL 16 :5432 (same server or a database server)
                                   └── SMTP, Mobile Sasa (HTTPS, outbound)
C:\EssData\files     encrypted documents           (pool identity: modify)
C:\EssData\keys      data-protection keys          (pool identity: modify; back up with the database)
C:\EssData\logs      JSON logs, 90 days            (pool identity: modify)
C:\EssData\backups   nightly backups               (Administrators and SYSTEM only)
```

The API serves the React app itself, so IIS needs a single site and no URL Rewrite rules. Background work (NAV sync, the integration queue, reminders, escalations, notifications) runs inside the same process, which is why the pool is set to *AlwaysRunning* with no idle timeout and the site has *preload* on.

## 2. Server requirements

| Item | Requirement |
|---|---|
| OS | Windows Server 2019 or 2022, joined to the domain if NAV uses Windows authentication |
| Size | 4 vCPU, 8 GB RAM, 100 GB disk for up to 2,000 employees; add 50 GB of document storage per 1,000 employees |
| IIS features | Web Server, WebSocket Protocol, Application Initialization, HTTP Redirection, Request Filtering (the install script can add them) |
| Runtime | ASP.NET Core 8.0 Hosting Bundle (run `iisreset` after installing) |
| Database | PostgreSQL 16 (Windows installer from postgresql.org) on the same server or a separate one; the client tools (`psql`, `pg_dump`) on the portal server |
| Certificate | A TLS certificate for the portal host name in LocalMachine\My (from the customer's CA or a public CA) |
| Network | Portal server → NAV server ports 7047 and 7048; → PostgreSQL 5432; → SMTP; → api.mobilesasa.com:443 |

## 3. First installation

### The quick way: EssPortalSetup-x64.exe

`EssPortalSetup-<version>-x64.exe` is the whole package in one 64-bit file (built by `Build-EssPackage.ps1` and by CI).

1. Install PostgreSQL 16 on the server (or its command line tools if the database is on another server), and import the HTTPS certificate into Local Computer > Personal.
2. Copy the setup file to the server, right-click it, **Run as administrator**. Windows SmartScreen may warn because the file is not code-signed: choose *More info > Run anyway*.
3. Answer the questions. Press Enter to accept a default. Passwords are typed hidden and never saved. Setup then adds the IIS features, downloads and installs the ASP.NET Core 8 Hosting Bundle if missing, creates the database, and installs the site.
4. Continue at step 4 below (sign in, NAV connection, backups).

Running the same or a newer setup file again **updates** the portal: the database script is idempotent, data is kept, and the previous answers (not the passwords) are offered as defaults. Setup logs to `C:\ProgramData\EssPortalSetup\setup-*.log`. If the database password was generated, setup shows it once at the end: store it, because backups and updates need it.

### Step by step with the scripts

1. **Build the package** on a build machine (or download it from the GitHub Actions run of the release): `.\deploy\windows\Build-EssPackage.ps1 -Version 1.0.0`. Copy `ess-portal-1.0.0-win-x64.zip` to the server, check the SHA-256 against the `.sha256` file, and extract it, for example to `C:\Install\ess-portal-1.0.0`.
2. **Create the database** (as Administrator, from the extracted folder):
   ```powershell
   .\Initialize-EssDatabase.ps1 -AdminPassword (Read-Host 'postgres password' -AsSecureString) `
       -AppPassword (Read-Host 'new ess_app password (16+ characters)' -AsSecureString)
   ```
3. **Install the portal**:
   ```powershell
   $cs = Read-Host 'Host=localhost;Database=ess;Username=ess_app;Password=...' -AsSecureString
   .\Install-EssPortal.ps1 -HostName ess.company.co.ke -CertificateThumbprint '<https cert thumbprint>' `
       -DatabaseConnectionString $cs -ServiceAccount (Get-Credential 'DOMAIN\svc-ess') `
       -KeyProtectionCertificateThumbprint '<key protection cert thumbprint>' `
       -SmtpHost smtp.company.co.ke -SmtpUserName ess@company.co.ke -SmtpPassword (Read-Host -AsSecureString) -SmtpFromAddress ess@company.co.ke `
       -SmsProvider MobileSasa -SmsApiKey (Read-Host 'Mobile Sasa token' -AsSecureString) -SmsSenderId COMPANY `
       -BootstrapAdminUserName admin -BootstrapAdminPassword (Read-Host -AsSecureString) -InstallPrerequisites
   ```
   * `-ServiceAccount` is needed when NAV uses Windows authentication: the portal then signs in to NAV as that account (leave the user name empty on the NAV connection screen). Otherwise omit it and the pool runs as `IIS AppPool\EssPortal`.
   * `-KeyProtectionCertificateThumbprint` is strongly recommended: the data-protection keys encrypt every document, bank detail and the stored NAV password, and a certificate lets them be restored on a replacement server. Export that certificate with its private key and keep it with the backups, in a safe place. Without it the keys are protected with machine DPAPI and only work on this server.
   * The JWT signing key is generated on first install and kept on later runs.
4. **Sign in** as the bootstrap administrator at `https://ess.company.co.ke`, enrol MFA, create the real administrators, then re-run the install script **without** the two `-Bootstrap…` parameters so they are removed.
5. **Connect NAV**: System administration, NAV connection (see `docs/nav-customizations.md` section 6), then Test connection and a full sync on the NAV integration page.
6. **Schedule backups**:
   ```powershell
   # as SYSTEM (for example: PsExec -s powershell), so the scheduled task can read it
   Read-Host 'ess_app password' -AsSecureString | ConvertFrom-SecureString | Set-Content C:\EssData\backup.pwd
   C:\inetpub\ess-portal\Backup-EssDatabase.ps1 -Register -At 01:30 -PasswordFile C:\EssData\backup.pwd
   ```
   Copy `C:\EssData\backups` off the server every day (another site or cloud storage). Test a restore at least every quarter.

### Virus scanning of uploads

Uploaded documents and receipts are scanned before they are stored. On Windows the portal uses **Microsoft Defender** (built into Windows Server 2016 and later) automatically, so no extra software is needed. To use a ClamAV server instead, set `Storage__ClamAv__Host` on the application pool. If neither is available, production refuses uploads rather than storing unscanned files.

## 4. Where settings live

Everything environment-specific is an environment variable on the application pool, set by the install script (IIS Manager, Application Pools, EssPortal, Advanced Settings, Environment Variables). `applicationHost.config` is readable by Administrators and SYSTEM only. Nothing secret is in the site folder, `web.config` or the package.

| Variable | Meaning |
|---|---|
| `ConnectionStrings__Ess` | PostgreSQL connection |
| `Jwt__SigningKey` | Token signing key (generated) |
| `App__PublicUrl` | Public address, used in email links |
| `Storage__RootPath`, `DataProtection__KeysPath`, `DataProtection__CertificateThumbprint` | Document and key storage |
| `Serilog__WriteTo__1__Args__path` | Log file location |
| `Smtp__*` | Email server |
| `Sms__Provider`, `Sms__ApiKey`, `Sms__SenderId` | Mobile Sasa |

To change one setting, re-run `Install-EssPortal.ps1` with the same parameters, or edit the variable in IIS Manager and recycle the pool.

## 5. Updating

```powershell
.\Update-EssPortal.ps1 -HostName ess.company.co.ke -PasswordFile C:\EssData\backup.pwd `
    -AdminPassword (Read-Host 'postgres password' -AsSecureString) -AppPassword (Read-Host 'ess_app password' -AsSecureString)
```

The script backs up, shows a maintenance page, applies the idempotent database script, swaps the files (keeping the previous version in `app-previous`) and checks `/health/ready`. To roll back, copy `app-previous` back to `app` and, if the database changed, restore the backup taken at the start.

## 6. Restore

1. Install the same portal version on the replacement server (steps 1 to 3 of section 3) with the **same** key protection certificate imported into LocalMachine\My.
2. Stop the site, then restore the database: `pg_restore -h localhost -U postgres -d ess --clean --if-exists ess.dump`.
3. Extract `files.zip` into `C:\EssData\files` and `keys.zip` into `C:\EssData\keys`, start the site, and open a document to confirm the keys work.

## 7. Operations checklist

* `/health/live` (process up) and `/health/ready` (database reachable) for monitoring; both are anonymous and reveal nothing.
* Logs: `C:\EssData\logs\ess-YYYYMMDD.json` (one JSON object per line; secrets and pay figures are masked). Startup failures also go to the Windows Application event log.
* Background jobs: `https://<host>/admin/jobs` (system administrators only).
* NAV health: the NAV integration page, with failed transactions, retry and log export.
* Certificates: renew the HTTPS certificate before it expires and re-run the install script with the new thumbprint.
* Patching: Windows updates and the ASP.NET Core 8 Hosting Bundle (monthly security releases) in a maintenance window; the pool recycles daily at 03:00.
