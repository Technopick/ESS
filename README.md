# Employee Self-Service portal for Dynamics NAV / Business Central

Enterprise ESS portal that keeps Dynamics NAV/BC as the system of record. Built in phases; Phases 1 and 2 are done.

* Architecture: [docs/architecture.md](docs/architecture.md)
* NAV integration mapping and discovery checklist: [docs/nav-integration-mapping.md](docs/nav-integration-mapping.md)
* NAV customisations required: [docs/nav-customizations.md](docs/nav-customizations.md)

## What works today (Phases 1 and 2)

* ASP.NET Core 8 API with Serilog, API versioning, Swagger, ProblemDetails errors, health checks, rate limiting, security headers, SignalR hub, Hangfire.
* PostgreSQL schema for all spec §10 entities (EF Core migration `InitialSchema`), soft delete, UTC audit stamps, partial unique indexes.
* Sign-in with lockout, rotating refresh tokens (HttpOnly cookie), sign-out, `/api/auth/me`; ten system roles with a granular permission matrix.
* NAV Integration Engine: connection and auth managers (Windows, NavUserPassword/web service key, OAuth for BC online), SOAP and OData/BC API clients, configurable field mapping, version profiles, transactional outbox with retry, backoff, dead-letter and duplicate-free reconciliation, NAV → ESS sync (incremental and full), masked call logging, connectivity test, integration dashboard API.
* React/TypeScript/Vite frontend: sign-in, responsive layout, light/dark mode, PWA manifest and service worker (API responses never cached), NAV integration dashboard (status, connection test, manual sync, failed transactions with retry).
* Phase 2 accounts and security: TOTP MFA (QR enrolment, recovery codes, replay protection), MFA required by role, forgot and reset password, invitation links, change password, rate-limited auth endpoints.
* Phase 2 administration: users (create, invite, roles with department scope, link to a NAV employee, disable, unlock, reset MFA, bulk-create accounts from NAV employees) and roles (custom roles with a permission editor).
* Phase 2 employee pages: dashboard (leave balances, pending requests, celebrations with privacy settings), My profile from NAV with change requests (bank and ID details encrypted at rest), and a staff directory that shows limited fields unless the user may see all employees.
* Tests: 53 backend tests (unit, end-to-end against PostgreSQL and the NAV simulator, and API tests), 4 frontend tests.

Business modules (leave, payroll, expenses, requests, documents, attendance, training) and the workflow engine are Phase 3. HR approval of profile change requests arrives with the workflow engine.

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

## Containers

```bash
cp .env.example .env   # fill in secrets
docker compose up --build
```

## Database migrations

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
cd backend
dotnet ef migrations script --idempotent -p src/Ess.Infrastructure -s src/Ess.Infrastructure -o ../deploy/sql/ess-schema.sql
```

Production applies the idempotent script (or sets `Database__MigrateOnStartup=true` for a controlled first start); tables are never created by hand.
