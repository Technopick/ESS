# ESS Portal: system architecture

Status: updated after Phase 5 (HR portal, management dashboard, reports, notifications). Version 0.5, 27 November 2026.

## 1. Purpose and ground rules

The ESS portal is a modern web front end over Microsoft Dynamics NAV / Business Central (BC). It lets employees, managers, HR, finance and administrators carry out HR self-service without NAV client licences, while **NAV stays the system of record** for employee, leave-ledger, payroll and financial data.

Rules every component follows:

1. The portal never reads or writes NAV SQL tables. All access goes through published NAV web services (SOAP pages and codeunits, OData, BC APIs).
2. NAV business logic always runs. Writes go to codeunits or API pages that call NAV's own validation and posting routines.
3. A transaction is shown as "posted to NAV" only after NAV returns a document number.
4. Every ESS → NAV transaction is written to an outbox first, so nothing is lost when NAV is down, and every failure is recorded and can be retried.
5. Retries are duplicate-free: each transaction carries a stable idempotency key that NAV stores and checks.
6. Payroll figures are never calculated or edited by the portal.
7. The portal does not assume standard NAV has HR, leave or payroll features. Each capability is an adapter with an explicit endpoint mapping; missing capabilities are listed as NAV customisations.

## 2. Logical architecture

```
 Browser / PWA (React + TypeScript)
        │  HTTPS, JWT access token (15 min) + HttpOnly refresh cookie
        ▼
 ASP.NET Core Web API  ── SignalR hub (/hubs/notifications)
        │
        ├── ESS business services (leave, payroll, expenses, requests, workflow, documents, ...)
        │        │ depend only on ports: INavEmployeeService, INavLeaveService, INavPayrollService,
        │        │ INavExpenseService, INavOrganizationService, IIntegrationQueue
        │        ▼
        ├── NAV Integration Engine (Ess.Integration.Nav)
        │        Connection Manager → Authentication Manager → Transport (throttle, log, classify)
        │        → SOAP client / OData client / BC API client → NAV web services
        │        Outbox queue + dispatcher, retry engine, reconciliation, sync, monitoring
        │
        ├── PostgreSQL (portal data, caches of NAV reference data, outbox, logs, audit)
        └── Hangfire (scheduled sync, reconciliation sweeps, reminders, notifications)
                                   │
                                   ▼
                    Dynamics NAV / BC service tier → NAV database
```

## 3. Solution structure

| Project | Responsibility |
|---|---|
| `backend/src/Ess.Domain` | Entities and enums only. GUID keys, UTC audit stamps, soft delete. |
| `backend/src/Ess.Application` | Use-case abstractions, permission catalogue, NAV ports and canonical NAV records, leave day calculator. No infrastructure dependencies. |
| `backend/src/Ess.Infrastructure` | EF Core `EssDbContext`, configurations and migrations, audit stamping, secret encryption, audit writer, reference numbers, seeding. |
| `backend/src/Ess.Integration.Nav` | The NAV Integration Engine (section 4). |
| `backend/src/Ess.Api` | HTTP API, authentication, authorisation policies, SignalR, Hangfire host, health checks, Swagger. |
| `backend/tests/Ess.UnitTests` | Unit tests plus end-to-end engine tests against PostgreSQL and the NAV simulator. |
| `frontend` | React 18 + TypeScript + Vite, MUI + Tailwind utilities, React Query, React Hook Form + Zod, PWA. |

Modules are separate assemblies with one-way references (Api → Integration.Nav → Infrastructure → Application → Domain), so the integration engine or a business module can be upgraded without rebuilding the rest.

## 4. NAV Integration Engine

The twelve components required by the spec map to code as follows.

| # | Component | Implementation |
|---|---|---|
| 1 | NAV Connection Manager | `Configuration/NavConnectionManager` resolves company → NAV instance → endpoint mappings, decrypts credentials in memory, caches for 60 s. |
| 2 | NAV Authentication Manager | `Security/NavAuthenticationManager`: Windows (NTLM/Negotiate), NavUserPassword / web service key (Basic), OAuth 2.0 client credentials (Entra ID) with token cache and one automatic refresh on 401. |
| 3 | NAV API Client | `Transport/NavTransport`: the only place that sends HTTP to NAV. Per-instance concurrency throttle, timing, error classification, masked logging to `nav_sync_logs`. |
| 4 | SOAP Service Client | `Clients/NavSoapClient`: `urn:microsoft-dynamics-schemas/codeunit/<Service>` envelopes, SOAPAction header, `<Method>_Result/return_value` and out-parameter parsing, NAV fault handling. |
| 5 | OData Client | `Clients/NavODataClient`: OData v3 (NAV 2013) and v4, server-driven paging (`@odata.nextLink`, `odata.nextLink`, `__next`), BC API URL shape `api/<publisher>/<group>/<version>/companies(<id>)/<entity>`. |
| 6 | NAV Data Mapping Engine | `Mapping/NavFieldMapper`: default field maps per protocol, overridable per instance via `nav_endpoint_mappings.field_map_json`. Handles NAV blank dates (0001-01-01) and OData field-name rules. |
| 7 | Background Synchronisation Service | `Sync/NavSyncService`: NAV → ESS for departments, positions, locations, employees, leave types, payroll periods. Incremental by modification marker; nightly full sync deactivates records deleted in NAV. Every run is a row in `nav_sync_jobs`. |
| 8 | Integration Queue | `Queue/IntegrationQueue`: transactional outbox (`integration_queue`), committed in the same database transaction as the business change. Unique on (company, operation, idempotency key). |
| 9 | Retry and Error Handling Engine | `Queue/IntegrationQueueProcessor` + `Transport/NavErrorClassifier` + `Transport/RetrySchedule`. See section 6. |
| 10 | Integration Monitoring Dashboard | `Monitoring/IntegrationDashboardService` + `/api/integration/nav/*` + the React page `pages/admin/IntegrationDashboardPage`. |
| 11 | Transaction Reconciliation Engine | `Queue/ReconciliationService`: for outcomes that are unknown, asks NAV by idempotency key whether the document exists before anything is resent. |
| 12 | NAV Version Compatibility Adapter | `Configuration/NavVersionProfile`: per platform (NAV 2013 … BC online) the OData version, write support, BC API support, modification marker and allowed auth methods. |

Business adapters (`Adapters/NavEmployeeAdapter`, `NavLeaveAdapter`, `NavPayrollAdapter`, `NavExpenseAdapter`, `NavOrganizationAdapter`) implement the ports. Each one chooses SOAP, OData or BC API from the endpoint mapping, not from hard-coded version checks, so a customer-specific codeunit or API page can be plugged in by configuration.

A NAV simulator (`Simulator/NavSimulatorHandler`) answers OData and SOAP calls in-process for development and automated tests. It is refused in Production, labels every record "SIM-", and is not a substitute for testing against the customer's NAV sandbox.

## 5. Data ownership

| Data | Owner | Portal copy |
|---|---|---|
| Employee master, departments, positions, locations | NAV | Cached for display, routing and authorisation (`employees`, `departments`, ...). Refreshed by sync. |
| Extended profile (next of kin, dependants, qualifications, bank) | NAV | Not cached. Read on demand; bank account masked to last four digits. |
| Leave types | NAV | Cached; HR adds portal rules (notice days, attachment needed, workflow). |
| Leave balances | NAV | Short-lived cache only; always re-read before a submission is validated. |
| Leave applications | Portal until approved, then NAV | Portal holds the request and workflow; NAV document number stored on confirmation. |
| Payroll periods | NAV | Cached; "published to employees" flag is portal-owned. |
| Payslips | NAV | Not stored by default; fetched on demand for the signed-in employee only. Optional encrypted snapshot. |
| Expense claims | Portal until approved, then NAV | NAV creates the finance document; portal never posts to the G/L. |
| Users, roles, permissions, sessions, notifications, workflow config, approval history, documents metadata, audit | Portal | Portal is the owner. |

## 6. ESS → NAV transaction lifecycle

Example: an approved leave application.

1. The final approval and the outbox row (`Leave.Submit`, idempotency key `ESS-LEAVESUBMI-<id>-0`) are saved in one database transaction. Leave `nav_status` = `PendingIntegration`; the employee sees "Approved, sending to NAV".
2. The dispatcher claims due rows with `FOR UPDATE SKIP LOCKED` and a lease, so several API nodes can run it safely.
3. It calls the NAV codeunit. Outcomes:

| NAV outcome | Classification | Queue status | Leave `nav_status` |
|---|---|---|---|
| Returns a document number | Success | `Succeeded` | `Posted` (the only path to Posted) |
| SOAP fault / 400 with business message ("insufficient balance") | Validation | `Failed` (no automatic retry) | `Failed`, user and HR told why |
| Connection refused, 503, 429, NAV table lock | Transient | `Pending`, exponential backoff with jitter | `PendingIntegration` |
| 401/403, missing mapping | Authentication / Configuration | `Pending` at the maximum delay, alert admins | `PendingIntegration` |
| Timeout after the request was sent, 502/504 on a write, worker crash | Unknown | `RequiresReconciliation` | `RequiresReconciliation` |
| Attempts exhausted | | `DeadLetter` (kept, retryable by an admin) | `Failed` |

4. Reconciliation (every 5 minutes, and before any manual retry) calls `FindByExternalRef` in NAV. If NAV has the document, the portal adopts NAV's number; if NAV confirms it has none, the item is resent. The NAV codeunit also checks the key itself, so even a resend that races cannot create a duplicate.

Nothing in `integration_queue` is ever deleted. This flow is covered by automated tests (`NavEngineEndToEndTests`).

## 7. Approval workflow ownership

Each document type has one owner, recorded on `approval_workflows.owner`:

* **Portal-owned** (default when NAV has no workflow for the document): the portal runs the configured steps (line manager → department head → HR → finance, parallel steps, delegation, escalation, amount limits) and submits the approved document to NAV with `preApproved = true`.
* **NAV-owned** (when the customer already approves this document in NAV): the portal submits on creation with `preApproved = false`, does not run its own approvals, and mirrors NAV's approval status through the `*.Status` endpoints.

This avoids two competing approval processes. The owner per document type is decided during discovery (see the mapping document).

## 8. Security

* **Authentication**: user name or email + password (ASP.NET Core Identity PBKDF2 hasher), lockout after 5 failures for 15 minutes, 15-minute JWT access token kept in memory, 7-day rotating refresh token in an HttpOnly SameSite=Strict cookie stored hashed, with token-family revocation on reuse. TOTP MFA (with recovery codes and required by role) is done; Microsoft Entra ID sign-in is planned once the customer confirms the tenant.
* **Authorisation**: deny by default. Ten system roles mapped to ~40 granular permissions (`Ess.Application/Security/Permissions.cs`); policies `perm:<code>`. HR roles can be scoped to a company and department.
* **Employee data isolation**: "my" data is always resolved from the token's employee claim, never from request parameters. The payroll adapter additionally verifies that NAV returned the requested employee's rows only.
* **Self-approval**: workflow steps skip or reassign when the approver is the requester (`skip_if_approver_is_requester`).
* **Secrets**: NAV passwords and client secrets, MFA secrets and sensitive request payloads are encrypted with ASP.NET Core Data Protection. Keys must be persisted and protected in production (certificate on Linux, DPAPI on Windows).
* **Logs**: NAV request/response bodies are masked (passwords, tokens, ID/KRA PIN/NSSF/SHIF numbers, bank accounts, pay amounts, birth dates) before storage.
* **Transport**: HTTPS with HSTS, security headers, CORS allow-list, rate limiting (10/min on auth endpoints, 300/min per user overall).
* **Audit**: `audit_logs` records sign-ins, payslip downloads, approvals, integration retries and configuration changes with user, IP and correlation id.
* **Kenya Data Protection Act 2019**: data minimisation (payroll not stored by default), purpose-limited access by role, encryption at rest for sensitive fields, audit trails, retention settings per document category, and support for data-subject access requests through HR. A DPIA template and retention schedule are Phase 6 deliverables.

## 9. Performance and scale (100 to 10,000+ employees)

* Stateless API nodes behind a load balancer; background workers can run on separate nodes (`Hosting:RunBackgroundWorkers`).
* Indexed lookups for every hot path (employee by NAV key, approvals by assignee and status, queue by status and due time).
* Paging everywhere (max 200 rows per page), NAV OData paging followed server-side.
* NAV calls are throttled per instance (`max_concurrency`) so peak leave periods or payslip publication cannot overload the NAV service tier.
* Reference data is cached; employee portal pages do not block on NAV when NAV is slow, and submissions queue with a visible pending status.

## 10. Deployment topology

* **Windows Server**: IIS (ASP.NET Core Module) for the API and static frontend, or the API as a Windows Service (`UseWindowsService`). PostgreSQL on Windows or a separate server.
* **Linux / containers**: `docker-compose.yml` (PostgreSQL, API, nginx serving the SPA and proxying `/api` and `/hubs`).
* Environments: `appsettings.{Development,Testing,UAT,Production}.json`; secrets only from environment variables or a secret store.
* Full deployment scripts, backups and CI/CD are Phase 7.

## 10a. Reporting, dashboard and notifications (Phase 5)

* **Reports** are defined in one catalogue (`Ess.Api/Reporting/ReportCatalog.cs`). Each report names its permission (HR, finance, payroll or approvals monitoring) and filters. `ReportRunner` resolves the user's department scope and refuses a department filter outside it. Exports (Excel through ClosedXML, PDF through PDFsharp/MigraDoc, CSV with formula-injection protection) need `data.export`, and every view and export is written to the audit log. Results are capped at 20,000 rows and say so when cut.
* **Payroll reporting** reads period totals from NAV through `INavPayrollService.GetPeriodTotalsAsync`. It only runs for closed periods and only shows totals per department, so no one sees another employee's pay through a report.
* **Management dashboard** (`ManagementDashboardService`) is computed from the portal's synced copy of NAV data, filtered by period, department and location within the user's scope, and honours the privacy settings for birthdays and gender.
* **Power BI readiness**: a `reporting` schema of views without names, pay, bank, ID or contact details, and an `ess_reporting` NOLOGIN role with SELECT on it only (`deploy/sql/reporting-role.sql`).
* **Notifications**: each event has a built-in wording; HR can override it per channel with placeholders. SMS is opt-in per event (only events with an active SMS template send one), payslip SMS may not contain amounts, and messages that cannot be sent (no gateway, no valid Kenyan number) are recorded as suppressed with the reason. Failed deliveries can be retried from the delivery history.
* **Organisation data kept in the portal**: department heads and parent departments are not reliably available from NAV, so HR maintains them in the portal for "head of department" approval routing. Contract end and termination dates are separate NAV fields (`Contract_End_Date`, `Termination_Date`), each configurable in the field mapping.
* **Scheduled jobs**: announcements are published and notified every 10 minutes; contract expiry alerts run daily at 06:15 and go to the line manager and HR, not the employee.

## 11. Phase status

| Phase | Scope | Status |
|---|---|---|
| 1 | Architecture, NAV discovery design, mapping, solution scaffold | Done except discovery on the real NAV, which needs NAV access |
| 2 | Auth (MFA, Entra ID), roles admin, employee dashboard, profile sync | Done, except Entra ID sign-in (needs the tenant details) |
| 3 | Leave, payroll, expenses, requests, documents, attendance, training, workflow engine | Done against the NAV simulator; NAV adapters for overtime and HR requests wait for the customer's objects |
| 4 | Remaining NAV adapters against the real NAV, approval status sync | Engine done; adapters need the customer's objects |
| 5 | HR portal, management dashboard, reports, notifications | Done against the NAV simulator; SMS needs the customer's gateway account |
| 6 | Security, performance and UAT testing | Not started |
| 7 | Production deployment package | Docker files only |

## 12. Open questions for the customer

1. ~~NAV/BC version~~ NAV 2016, standard HR with extensions and custom tables where needed (answered 27 November 2026). Still needed: the custom table names for leave and payroll, and the build number.
2. Which document types already have approval workflows in NAV (decides workflow ownership).
3. How departments and positions are modelled (Global Dimension 1, HR add-on tables, or Employee fields).
4. Whether NAV can render the payslip report as PDF via a codeunit, or the portal should render it.
5. Authentication on the NAV service tier (Windows, NavUserPassword, or OAuth for BC online), and whether a dedicated service user with a restricted permission set can be created.
6. ~~Hosting target~~ Windows/IIS (answered). Still needed: whether the NAV server is reachable from the portal server, and whether the portal faces the internet.
