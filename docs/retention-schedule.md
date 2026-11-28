# Data retention schedule

This schedule says how long the ESS portal keeps each kind of personal data, why, and how the limit is enforced. It applies to the portal's own PostgreSQL database and file store. Dynamics NAV 2016 remains the system of record for HR and payroll; NAV's own retention is governed by the client's HR and finance policies and is out of scope here.

Legal basis used throughout:

* **Kenya Data Protection Act 2019, s.25(e) and s.39**: personal data is kept only as long as needed for the purpose it was collected for, unless a law requires longer.
* **Employment Act 2007, s.74**: the employer keeps employment records for five years after the employee leaves.
* **Tax Procedures Act 2015, s.23**: records supporting tax returns (including expense receipts) are kept for five years.

The data protection officer (DPO) and HR manager must confirm the periods marked *client to confirm* before go-live. All automatic periods are configuration values under `Retention` in `appsettings.json` (or environment variables such as `Retention__AuditLogYears`), so they can be changed without a code release.

## Schedule

| Data | Where it lives | Retention | Trigger | How it is enforced |
|---|---|---|---|---|
| Employee master record (name, contacts, department, position, manager, dates) | `employees`, synchronised from NAV | While the employee exists in NAV; a leaver stays in the portal as *Terminated* with no sign-in | NAV sync | Mirrors NAV. Accounts of terminated employees are deactivated by HR (Admin > Users). |
| Portal user account (user name, e-mail, password hash, MFA secret, recovery codes) | `users` | Until HR deactivates it; deactivated accounts are kept 5 years after exit for the audit trail, then deleted manually | Exit date | Manual: HR deactivates on exit. Annual review by the DPO. *Client to confirm.* |
| Leave applications and their approval history | `leave_applications`, `workflow_instances`, `approval_tasks`, `approval_history` | 5 years after the employee leaves (Employment Act s.74) | Exit date | Manual annual review; NAV holds the posted leave ledger. |
| Leave balances | `leave_balances_cache` | Replaced on every refresh; no history | Refresh | Automatic (overwritten). |
| Expense claims and lines | `expense_claims`, `expense_claim_lines` | 5 years after the claim's financial year (Tax Procedures Act s.23) | Claim date | Manual annual review; NAV holds the posted entries. |
| Expense receipts | Document store, category `RECEIPT` | 84 months (7 years) from upload | Upload date | **Automatic**: nightly retention job. |
| Medical documents | Document store, category `MEDICAL` | 84 months from upload. *Client to confirm*: some organisations keep these only for the duration of the related sick leave | Upload date | **Automatic**: nightly retention job. |
| Other uploads (category `OTHER`) | Document store | 84 months from upload | Upload date | **Automatic**: nightly retention job. |
| Contracts, IDs, certificates, policies, HR letters | Document store | Kept while employed plus 5 years; no automatic limit because the end date depends on the exit | Exit date | Manual: HR deletes after the period. A category's retention months can be set in the `document_categories` table to automate it (there is no screen for this yet). |
| Files of deleted documents | Encrypted file store | 30 days after deletion (grace period to undo mistakes) | Deletion | **Automatic**: nightly job deletes the file and version rows and keeps a tombstone row for the audit trail. |
| Payslips | **Not stored.** Read live from NAV each time | None in the portal | | By design. |
| Payslip snapshot (encrypted copy used only when NAV is unreachable) | `payslip_metadata.snapshot_encrypted` | 90 days after retrieval | Retrieval date | **Automatic**: nightly job clears the snapshot; the index row (period and employee) stays. |
| Cached payslip PDFs | Encrypted file store | 90 days after retrieval | Retrieval date | **Automatic**: nightly job. |
| Profile change requests | `profile_change_requests` | 5 years after the employee leaves | Exit date | Manual annual review. |
| HR service requests (including confidential types) | `hr_requests` | 5 years after closure. *Client to confirm* for grievance and disciplinary types | Closure | Manual annual review. |
| Attendance records and requests | `attendance_records`, `attendance_requests` | 5 years | Record date | Manual annual review. |
| Training applications and feedback | `employee_training` | While employed plus 5 years | Exit date | Manual annual review. |
| Notifications (in-app, e-mail and SMS log) | `notifications` | 365 days | Creation | **Automatic**: nightly job. |
| Sign-in refresh tokens and password-reset tokens | `refresh_tokens`, `password_reset_tokens` | 30 days after expiry or revocation | Expiry | **Automatic**: nightly job. |
| NAV integration call logs (masked request and response) | `nav_sync_logs` | 180 days | Call date | **Automatic**: nightly job. |
| Integration queue items | `integration_queue` | Kept with the request they belong to | | Follows the request. |
| Audit trail (sign-ins, views of payroll and documents, approvals, admin changes) | `audit_logs` | 7 years | Event date | **Automatic**: nightly job. |
| Document access log | `document_access_logs` | 7 years | Access date | **Automatic**: nightly job. |
| Application logs (Serilog files on the IIS server) | `logs\` folder on the server | 90 days in Production, 30 in UAT (daily files) | Log date | Automatic via Serilog `retainedFileCountLimit`. Logs never contain passwords, tokens or payroll amounts. |
| Database backups | Backup folder, plus the client's off-site copy | 30 days | Backup date | `Backup-EssDatabase.ps1 -RetentionDays 30`. Deleted records disappear from backups once the backup ages out. The off-site copy's retention is set by the client's backup policy. |

## The retention job

`RetentionService` runs nightly at 02:30 (`Retention:Cron`) as the Hangfire job `data-retention`, on the worker node only. Each run:

1. Soft-deletes documents whose category retention has passed since the latest version was uploaded.
2. After the grace period, deletes the stored files and version rows of deleted documents, and cached payslip PDFs.
3. Clears payslip snapshots older than the snapshot period.
4. Deletes old notifications, spent tokens, NAV call logs, document access logs and audit entries.
5. Writes a `retention.run` audit entry with the counts, and one `document.retention-expired` entry per retired document.

It never calls NAV and never touches NAV data. It works in batches (`Retention:BatchSize`) so a first run on a large backlog does not lock tables; the remainder is picked up the next night. Setting `Retention:Enabled` to `false` pauses it, for example during a legal hold.

## Legal hold

If records are needed for litigation, an investigation or a regulator, the DPO sets `Retention:Enabled=false` and records the hold (who, why, from when) in the DPIA's change log. Manual deletion reviews also stop until the hold is lifted.

## Data subject requests

Requests for access, correction or deletion (DPA s.26, s.40) go to HR. Employees can already see and download their own leave, claims, requests, documents and payslips in the portal; HR assembles anything else from HR > Employees > the employee's page and the reports. There is no one-click export yet. Deletion is limited where the Employment Act or tax law requires the record to be kept, and the employee is told which records are kept and why.
