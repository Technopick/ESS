# Employee Self-Service portal for Dynamics NAV / Business Central

Enterprise ESS portal that keeps Dynamics NAV/BC as the system of record. Built in phases; Phases 1, 2, 3 and 5 are done. Phase 4 (the remaining adapters) waits for access to the customer's NAV.

* Architecture: [docs/architecture.md](docs/architecture.md)
* NAV integration mapping and discovery checklist: [docs/nav-integration-mapping.md](docs/nav-integration-mapping.md)
* NAV customisations required: [docs/nav-customizations.md](docs/nav-customizations.md)

## What works today (Phases 1, 2, 3 and 5)

* ASP.NET Core 8 API with Serilog, API versioning, Swagger, ProblemDetails errors, health checks, rate limiting, security headers, SignalR hub, Hangfire.
* PostgreSQL schema for all spec §10 entities (EF Core migration `InitialSchema`), soft delete, UTC audit stamps, partial unique indexes.
* Sign-in with lockout, rotating refresh tokens (HttpOnly cookie), sign-out, `/api/auth/me`; ten system roles with a granular permission matrix.
* NAV Integration Engine: connection and auth managers (Windows, NavUserPassword/web service key, OAuth for BC online), SOAP and OData/BC API clients, configurable field mapping, version profiles, transactional outbox with retry, backoff, dead-letter and duplicate-free reconciliation, NAV → ESS sync (incremental and full), masked call logging, connectivity test, integration dashboard API.
* React/TypeScript/Vite frontend: sign-in, responsive layout, light/dark mode, PWA manifest and service worker (API responses never cached), NAV integration dashboard (status, connection test, manual sync, failed transactions with retry).
* Phase 2 accounts and security: TOTP MFA (QR enrolment, recovery codes, replay protection), MFA required by role, forgot and reset password, invitation links, change password, rate-limited auth endpoints.
* Phase 2 administration: users (create, invite, roles with department scope, link to a NAV employee, disable, unlock, reset MFA, bulk-create accounts from NAV employees) and roles (custom roles with a permission editor).
* Phase 2 employee pages: dashboard (leave balances, pending requests, celebrations with privacy settings), My profile from NAV with change requests (bank and ID details encrypted at rest), and a staff directory that shows limited fields unless the user may see all employees.
* Phase 3 workflow engine: configurable approval routes per request type (line manager, head of department, manager's manager, role, named person), sequential and parallel steps, amount thresholds, delegation, deadlines with reminders and escalation, append-only history, versioned routes, and a "NAV owns the approval" option. Nobody can approve their own request. Profile change requests now go through it.
* Phase 3 leave: balances from NAV, live validation (notice, overlap, half days, non-working days, balance), apply, correct after return, cancel, team calendar that hides leave types from peers, and posting to NAV only after approval with status mirrored back.
* Phase 3 payslips: published periods only, payslip view and print, NAV PDF when available, yearly statutory summary (PAYE, NSSF, SHIF, Housing Levy), encrypted fallback copy, audited HR access with a reason, and HR publishing of closed NAV periods.
* Phase 3 expenses and documents: multi-line claims with receipts, policy checks, approval by amount, posting to NAV as a claim for finance; encrypted document storage with content checks, optional ClamAV scan, versions, expiry reminders and an access log.
* Phase 3 HR requests, attendance and training: request types with their own form fields and SLA, HR queue; clock in and out with optional location, corrections and overtime through approval, team view; training catalogue, applications, participants and feedback.
* Phase 5 HR portal: employee list with filters (department, location, job title, status, contracts ending, no portal account), employee detail with the live NAV profile, balances and activity (every view audited), refresh from NAV, organisation pages with headcounts, department heads and parent departments kept in the portal for approval routing, leave administration (applications monitor, portal-side leave type rules including gender eligibility, public holidays), HR document library, and announcements with targeting, scheduling and notification.
* Phase 5 management dashboard: headcount, joiners, leavers and turnover, breakdowns by department, location, status and gender, people on leave, contracts ending, celebrations (respecting privacy settings), 12-month trends, training and request statistics, filtered by period, department and location, with Excel and PDF export.
* Phase 5 reports: 13 reports (directory, headcount, leave utilisation, leave liability, leave applications, contract expiry, HR requests, attendance, training, document expiry, expense claims, pending approvals, payroll summary) with filters and Excel, PDF and CSV export. Department scope is enforced on every report, every view and export is audited, and the payroll summary shows department totals for closed periods only. A read-only `reporting` schema of views and an `ess_reporting` role make the data ready for Power BI.
* Phase 5 notifications: templates per event and channel with placeholders and preview, optional SMS through Africa's Talking (opt-in per event, no pay figures allowed), delivery history with retry, contract expiry alerts at 60, 30 and 7 days, and NAV integration log export.
* Notifications: in-app (live through SignalR), email and SMS, with a bell in the header.
* Tests: 87 backend tests (unit, end-to-end against PostgreSQL and the NAV simulator, and API tests), 10 frontend tests.

Known gaps: overtime and HR requests are not yet sent to NAV, biometric clock import is not built, and the payslip PDF needs NAV's report (otherwise the browser's print is used). These depend on the customer's NAV objects.

## Run locally

Prerequisites: .NET 8 SDK, Node 22, PostgreSQL 16.

```bash
# API (Development uses the built-in NAV simulator and demo company "SIM")
cd backend/src/Ess.Api
Bootstrap__AdminUserName=admin Bootstrap__AdminPassword='choose-a-long-password' dotnet run --urls http://localhost:5080
# Swagger: http://localhost:5080/swagger

# Frontend
cd frontend && npm ci && npm run dev    # http://localhost:5173 (proxies /api to :5080)
```

The Development connection string expects `postgres/postgres` on localhost; change it in `appsettings.Development.json` or with `ConnectionStrings__Ess`.

## Tests

```bash
cd backend && ESS_TEST_DB="Host=localhost;Username=postgres;Password=postgres;Database=postgres" dotnet test
cd frontend && npm test
```

Without `ESS_TEST_DB` the end-to-end tests are skipped.

## Windows Server and IIS (customer target)

`deploy/windows` builds and installs the production package: one IIS site that serves both the API and the React app, an always-running application pool for background jobs, HTTPS, settings and secrets as application-pool environment variables, PostgreSQL set-up, nightly backups and updates with rollback. Follow [docs/deployment-windows-iis.md](docs/deployment-windows-iis.md). GitHub Actions builds the package on every change to `main`.

## Containers

```bash
cp .env.example .env   # fill in secrets
docker compose up --build
```

## Power BI and other BI tools

The `reporting` schema holds views over employees, departments, leave, expenses, HR requests, training, attendance and approvals. They leave out names, salaries, bank and ID details, contact details and document contents; rows carry the NAV employee number so a BI model can join to NAV if the customer decides names belong in it. Run `deploy/sql/reporting-role.sql` once, then create a login that is a member of `ess_reporting` and connect Power BI to PostgreSQL with it:

```sql
CREATE ROLE powerbi LOGIN PASSWORD '<from your secret store>' IN ROLE ess_reporting;
```

## SMS and PDF settings

* SMS: set `Sms__Provider` to `AfricasTalking` with `Sms__Username`, `Sms__ApiKey` and optionally `Sms__SenderId` (environment variables or a secret store, never in files). `Log` writes messages to the log for testing. No SMS is sent for an event until HR adds an SMS template for it.
* PDF export needs a TrueType font. The Docker image installs DejaVu; on Windows Arial is used. `Reporting__FontPath` and `Reporting__BoldFontPath` point at other fonts.

## Database migrations

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
cd backend
dotnet ef migrations script --idempotent -p src/Ess.Infrastructure -s src/Ess.Infrastructure -o ../deploy/sql/ess-schema.sql
```

Production applies the idempotent script (or sets `Database__MigrateOnStartup=true` for a controlled first start); tables are never created by hand.
