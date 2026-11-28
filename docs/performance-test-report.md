# Phase 6 performance test report

| | |
|---|---|
| Goal | Confirm the portal stays responsive for a 10,000-employee company and find slow queries before UAT |
| Test | `backend/tests/Ess.Api.Tests/PerformanceTests.cs`, which loads a full company into its own database and times 17 key screens through the real API |
| Machine | Cloud build container (4 vCPU), API and PostgreSQL 16 on the same host, default PostgreSQL settings. A dedicated Windows server with tuned PostgreSQL should be at least as fast. |
| Re-run | `ESS_PERF=1 ESS_PERF_REPORT=perf.md ESS_TEST_DB="Host=...;Username=...;Password=...;Database=postgres" dotnet test --filter PerformanceTests` in `backend/` |

## Result

Every screen answered well inside its budget with 10,000 employees, 60,000 leave applications, 27,000 expense claims, 120,000 payslip records and 200,000 audit entries. Everyday employee screens answer in under 40 ms at the 95th percentile; the heaviest screen, the management dashboard, in under 200 ms. 50 simultaneous dashboard loads completed in under 0.4 s with no errors.

The test found two slow spots, both fixed in this phase:

| Screen | Before (p95) | After (p95) | Cause and fix |
|---|---:|---:|---|
| Employee dashboard | 113 ms | 37 ms | The birthdays and work-anniversaries panel read all 10,000 employees on every dashboard load. It now reads only the two relevant months and is cached per company for 10 minutes. |
| 50 parallel dashboard loads | 690 ms | 372 ms | Same cause. |
| HR leave applications list | 130 ms | 48 ms | Sorting by submission date had no index. Added indexes on (company, submitted date) for leave applications and expense claims (migration `Phase6Performance`). |

## Measurements after the fixes

### Data volume

| Table | Rows |
|---|---:|
| employees | 10,006 |
| leave_applications | 60,000 |
| expense_claims | 26,664 |
| payslip_metadata | 120,000 |
| notifications | 800 |
| audit_logs | 200,014 |

Seeded in 10 s.

### Response times (30 sequential requests per endpoint after 3 warm-up requests)

| Endpoint | p50 ms | p95 ms | max ms | Budget p95 | Response size | Rows scanned per request | Result |
|---|---:|---:|---:|---:|---:|---|---|
| Employee dashboard | 23 | 37 | 38 | 500 | 2.4 KB | - | Pass |
| My notifications | 3 | 4 | 4 | 300 | 4.2 KB | - | Pass |
| Unread count (polled) | 2 | 4 | 6 | 150 | 0 KB | - | Pass |
| My leave applications | 5 | 6 | 6 | 300 | 0 KB | - | Pass |
| Manager approvals inbox | 2 | 3 | 3 | 500 | 0 KB | - | Pass |
| HR employee list, page 1 | 9 | 11 | 14 | 500 | 8.1 KB | employees 5,154 | Pass |
| HR employee search | 35 | 44 | 44 | 500 | 8.1 KB | employees 10,612 | Pass |
| HR employees, contracts ending | 10 | 12 | 13 | 500 | 8.3 KB | employees 11,825 | Pass |
| HR employee list, page 200 | 16 | 23 | 24 | 500 | 8.1 KB | employees 8,186 | Pass |
| HR leave applications | 46 | 48 | 49 | 500 | 10.5 KB | leave_applications 58,181, employees 9,702 | Pass |
| Management dashboard | 133 | 184 | 189 | 1500 | 163.8 KB | leave_applications 50,909, expense_claims 26,664, employees 18,495 | Pass |
| Report: headcount | 39 | 59 | 104 | 1500 | 0.9 KB | employees 9,399 | Pass |
| Report: leave applications (12 months) | 124 | 189 | 204 | 2500 | 3097.6 KB | leave_applications 53,238, employees 18,495 | Pass |
| Report: employee directory (10k rows) | 58 | 75 | 77 | 2500 | 1159.7 KB | employees 8,186, leave_applications 1,309 | Pass |
| Report: expense claims (12 months) | 84 | 109 | 170 | 2500 | 1997.8 KB | - | Pass |
| Report: pending approvals | 6 | 6 | 6 | 1500 | 0.6 KB | - | Pass |
| Admin user list | 4 | 5 | 6 | 500 | 4.4 KB | - | Pass |

### Parallel load

50 simultaneous dashboard and unread-count requests finished in 393 ms; p50 255 ms, p95 372 ms, errors 0.

### Table access during the timed run

Sequential scans on large tables point to a missing index. Small tables are scanned on purpose by PostgreSQL.

| Table | Sequential scans | Rows read by them | Index scans |
|---|---:|---:|---:|
| approval_tasks | 891 | 0 | 0 |
| audit_logs | 1 | 200,014 | 0 |
| employee_mappings | 231 | 957 | 330 |
| employees | 431 | 4,312,586 | 166,518 |
| expense_claims | 34 | 906,576 | 198 |
| leave_applications | 133 | 6,000,000 | 495 |
| notifications | 67 | 53,600 | 66 |
| payslip_metadata | 1 | 120,000 | 0 |
| workflow_instances | 0 | 0 | 0 |

## Notes for production

* **Large reports.** "Leave applications" for 12 months returns about 3 MB and "Employee directory" about 1.2 MB as on-screen tables. They are fast, but on slow branch connections users should prefer the Excel or CSV export.
* **Remaining full scans are expected.** The employee search matches any part of a name, number or e-mail, so PostgreSQL reads the employees table (about 10,000 rows, 40 ms). Paged lists also count all matching rows for the page total. Both are well within budget at this size; at 50,000+ employees a trigram index (`pg_trgm`) on names would keep search fast.
* **PostgreSQL settings** for the production server (16 GB RAM, database on SSD): `shared_buffers = 4GB`, `effective_cache_size = 12GB`, `work_mem = 32MB`, `random_page_cost = 1.1`, `max_connections = 200`. Run `ANALYZE` after the first full NAV sync.
* **NAV is the real limit.** These timings exclude NAV calls because screens read the portal's synchronised copy. Payslips and live balance refreshes call NAV 2016 directly; their speed depends on the NAV service tier. Measure a payslip open in UAT (script EMP-05) and raise the NAV service tier's `MaxConcurrentCalls` if payday traffic queues.
* **Payday peak.** Expect most staff to open a payslip within a few hours of publication. The portal serves a closed period from its encrypted 24-hour snapshot after the first read, which spreads the load on NAV.
