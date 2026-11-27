# Phase 6 security test report

| | |
|---|---|
| System | ESS portal for Dynamics NAV 2016 (ASP.NET Core 8 API, React front end, PostgreSQL) |
| Build tested | Branch `phase6-testing` (includes the Mobile Sasa, NAV 2016 and IIS packaging changes) |
| Method | Automated tests against the real API on a throw-away PostgreSQL database with the NAV simulator; dependency vulnerability scans; code review of the findings |
| Re-run | `ESS_TEST_DB="Host=...;Username=...;Password=...;Database=postgres" dotnet test` in `backend/`. CI runs the same suite on every pull request. |

## Summary

The portal passed every security test after four fixes made during this phase. No finding remains open. One area is recommended for the client before go-live: an independent penetration test of the installed site, because these tests run against the application and do not cover the client's IIS, network and NAV server configuration.

| Severity | Found | Fixed | Open |
|---|---:|---:|---:|
| High | 0 | 0 | 0 |
| Medium | 2 | 2 | 0 |
| Low | 2 | 2 | 0 |

## What was tested

The tests are in `backend/tests/Ess.Api.Tests/SecurityTests.cs` and stay in the suite, so a regression fails CI.

| Area | Test | Result |
|---|---|---|
| Endpoint inventory | Every one of the API's endpoints (about 160) is discovered from the routing table. Each must either require a named permission, be on a short reviewed list of self-service routes that scope data to the caller, or be one of eight anonymous routes (sign-in, MFA, token refresh, sign-out, password reset, health). A new endpoint that fits none of these fails the build. | Pass |
| Authentication | Every non-anonymous endpoint returns 401 to an anonymous caller. | Pass |
| Authorisation | Signed in as an ordinary employee, every endpoint guarded by a permission the employee lacks (more than 50) is called and must refuse (403 or 404). | Pass |
| Data isolation | Employee A tries to read, edit, cancel, submit or withdraw employee B's leave and expense claims, read B's approval history, and act on approval tasks. All refused; B's records unchanged. | Pass |
| Self-approval | The workflow engine refuses an approval when the approver is the requester, including through a delegation (existing workflow tests). | Pass |
| Payroll privacy | Payslips resolve the employee from the signed-in user; HR access needs the payroll permission and is audited (existing payroll tests). Staff see an empty report catalogue and cannot run or export any report. | Pass |
| Tokens | Tokens with a changed payload, no signature, `alg: none`, a different signing key, or garbage are rejected. | Pass |
| Robustness | As administrator, every endpoint is called with random IDs and empty bodies; none may answer with a server error (500). | Pass |
| Injection | SQL-style and script-style input in search, text fields and IDs is stored and returned as plain data; queries are parameterised by EF Core; rich text (announcements) is sanitised. | Pass |
| Response headers | `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` on API responses; no `X-Powered-By` header. HSTS is on outside development. | Pass |
| Multi-company | Reference lists (companies, departments, locations) show only the caller's own company. | Pass after fix F1 |
| Concurrency | Many parallel requests from one user (dashboard, leave, balances) never produce a server error. | Pass after fix F2 |
| Retention | The new retention job deletes only what the schedule allows and keeps a tombstone and audit entry. | Pass |
| Dependencies | `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev`, both added to CI. | Pass after fix F3 |

Earlier phases' tests also cover: the same answer for unknown users and wrong passwords, lockout after 5 failed attempts, forgot-password not revealing whether an e-mail exists, MFA enrolment with single-use recovery codes, refresh token rotation with reuse revoking the whole token family, bank details encrypted at rest and masked when listed, NAV passwords never returned by the API, and masking of secrets, ID numbers and pay in NAV logs.

## Findings and fixes

| ID | Severity | Finding | Fix |
|---|---|---|---|
| F1 | Medium | `GET /api/lookups/*` returned every company's departments and locations to any signed-in user. In a multi-company set-up this disclosed other companies' structure. | Signed-in users now see only their own company; only user and integration administrators (who link accounts to companies) see all. `LookupsController`. |
| F2 | Medium | Two requests refreshing the same employee's leave balances at the same moment (for example the dashboard and the leave page) could collide on a unique index and return a server error. | The refresh now takes a short per-employee database lock and replaces the cached rows in one transaction. `LeaveBalanceService`. |
| F3 | Low | Seven third-party libraries had published vulnerabilities: HtmlSanitizer, Newtonsoft.Json, MailKit/MimeKit, System.Security.Cryptography.Xml (server), System.Net.Http and System.Text.RegularExpressions (test projects only) and react-router (front end). | Upgraded or pinned to patched versions. CI now fails on any moderate or worse vulnerability in production dependencies. Remaining npm findings are in development-only tools (test runner, bundler) that are not shipped. |
| F4 | Low | The architecture notes said deleted document files were purged by a retention job, but no such job existed, so files and old records were kept indefinitely (Data Protection Act s.39). | Added the nightly `data-retention` job and the [retention schedule](retention-schedule.md). |

## Residual risks and recommendations

1. **Independent penetration test** of the installed UAT site (IIS, TLS, network paths to NAV and PostgreSQL) before go-live.
2. **MFA coverage**: by default MFA is enforced for administrators, the HR manager and the finance manager. Consider adding HR officers and line managers (`Security:RequireMfaForRoles`).
3. **Malware scanning**: uploads are scanned with Microsoft Defender on Windows (or ClamAV if configured), and production refuses uploads when no scanner works. Keep Defender's signatures updated.
4. **Swagger** is enabled in the UAT configuration for testing and disabled in production. Keep the UAT site on the internal network only.
5. **NAV web services** should use SSL where the NAV 2016 service tier supports it, and the NAV service account should have only the permissions listed in [nav-customizations.md](nav-customizations.md).
6. **Phishing** remains the main account-takeover route; brief staff at launch.
