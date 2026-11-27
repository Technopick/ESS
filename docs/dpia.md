# Data protection impact assessment (DPIA): Employee Self-Service portal

This is a pre-filled template for the client's data protection officer. The technical sections describe the portal as built; the sections marked **[Client]** must be completed and signed by the client before go-live. It follows the structure the Office of the Data Protection Commissioner (ODPC) expects under section 31 of the Kenya Data Protection Act 2019 and the Data Protection (General) Regulations 2021. A DPIA is required here because the portal processes employee data, including health information (medical documents, sick leave) and financial information (payslips, bank-related claims), at scale.

| | |
|---|---|
| Organisation (data controller) | **[Client]** |
| ODPC registration number | **[Client]** |
| Data protection officer | **[Client]** |
| System owner | **[Client]**, HR |
| Assessment version and date | 1.0, **[date]** |
| Next review | Within 12 months, or before any major change (new module, new processor, hosting move) |

## 1. Description of the processing

**Purpose.** Let employees view their own HR and payroll information and submit requests (leave, expense claims, profile changes, HR service requests, attendance corrections, training applications) online, and let managers, HR and finance approve them. Approved requests are posted into Microsoft Dynamics NAV 2016, which remains the system of record.

**Data subjects.** Current employees of the client's companies; former employees for the retention period; next of kin and dependants only if HR records them in NAV (the portal shows them read-only).

**Personal data processed.**

| Category | Examples | Special category (DPA s.2)? |
|---|---|---|
| Identity and contact | Name, employee number, work and personal e-mail, mobile number, photo | No |
| Employment | Department, position, manager, location, employment and contract dates, status | No |
| Leave and attendance | Leave applications, balances, attendance records, overtime | Sick leave can reveal health data |
| Payroll | Payslips (gross, deductions, net pay), payroll periods | No, but high risk if disclosed |
| Expenses | Claims, amounts, receipts | No |
| Documents | Contracts, ID and KRA PIN copies, certificates, medical documents | Medical documents are health data |
| HR requests | Service requests, some confidential (for example grievances) | Can be sensitive |
| Technical | User name, password hash, MFA secret, IP address, sign-in times, audit trail | No |

**Sources.** NAV (synchronised every 15 minutes, read-only for HR master data); the employee; their manager and HR.

**Recipients.** Within the client: the employee, their approvers, HR, finance and management by role. Processors: the SMS gateway (Mobile Sasa) for text alerts, the client's e-mail server for e-mail alerts, and **[Client: IT support provider, if any]**. No data leaves Kenya unless the client's e-mail or backup service is hosted abroad: **[Client to confirm]**.

**Hosting.** On the client's Windows servers under IIS, with PostgreSQL on the client's infrastructure. The portal connects to NAV over the internal network using NAV web services with a dedicated service account.

**Scale.** Designed and load-tested for 10,000 employees.

## 2. Necessity and proportionality

| Question | Answer |
|---|---|
| Lawful basis (DPA s.30) | Performance of the employment contract and compliance with legal obligations (Employment Act, tax law). Consent is not relied on for core HR processing. |
| Is each data item needed? | The portal holds only fields needed for display and routing. Payslips are **not stored**: they are read live from NAV, with an encrypted fallback copy cleared after 90 days. Bank details and salary rates are not synchronised; when an employee asks to change bank details, the request carries the new details to HR for approval into NAV. |
| Accuracy (s.25(d)) | NAV is the source of truth; the portal refreshes every 15 minutes and shows "last updated" times. Employees request corrections through profile change requests, which HR approves into NAV. |
| Retention (s.25(e), s.39) | See [retention-schedule.md](retention-schedule.md). A nightly job enforces the automatic limits. |
| Transparency (s.29) | **[Client]**: publish a portal privacy notice (link on the sign-in page) covering purpose, recipients, retention and rights. |
| Data subject rights (s.26) | Employees see their own records in the portal. HR handles access, correction, objection and deletion requests within the statutory timelines. |

## 3. Risk assessment

Likelihood and impact: Low, Medium, High. Residual risk is after the controls listed.

| # | Risk | Likelihood | Impact | Controls in the portal | Residual |
|---|---|---|---|---|---|
| R1 | An employee sees another employee's payslip or records | Medium | High | Every endpoint requires sign-in and a named permission; "own data" endpoints resolve the employee from the signed-in user, never from a URL parameter; automated tests check that one employee cannot read, change or approve another's leave, claims or approval history. Payroll views are audited. | Low |
| R2 | A manager or HR officer sees more than their role needs | Medium | High | Role-based permissions with department scope; payroll needs its own permission; confidential HR request types hide subjects in reports; HR views of payroll are audited. | Low |
| R3 | Account takeover (weak or reused password, phishing) | Medium | High | Password policy and breach-resistant hashing; lockout after failed attempts; rate-limited sign-in; MFA (TOTP) enforced by default for administrators, the HR manager and the finance manager, and available to everyone (**[Client]**: decide whether to extend it to HR officers and line managers in `Security:RequireMfaForRoles`); refresh tokens rotated and revocable; other sessions end when the password is changed or reset. | Low to Medium (phishing remains) |
| R4 | Self-approval or approval fraud | Low | High | The workflow engine refuses tasks where the approver is the requester, including through delegation; every decision is recorded with the actor and time. | Low |
| R5 | Data posted to NAV twice, or marked posted when it was not | Medium | Medium | Idempotency keys on every NAV call; a request is marked posted only after NAV confirms; a reconciliation job compares statuses every 5 minutes. | Low |
| R6 | Interception of data in transit | Low | High | HTTPS only with HSTS; NAV traffic on the internal network (**[Client]**: enable SSL on the NAV web services where available). | Low |
| R7 | Theft of the database or file store | Low | High | Sensitive fields (MFA secrets, NAV credentials, payslip snapshots) and all uploaded files are encrypted at rest; encryption keys are protected by a server certificate or Windows DPAPI and stored apart from the database; backups contain encrypted values only. | Low |
| R8 | Malicious file upload | Medium | Medium | File type decided from content, not name; 10 MB limit; malware scan hook (**[Client]**: connect the antivirus scanner); files served with no-sniff headers and downloaded rather than rendered when not PDF or image. | Low |
| R9 | Over-retention | Medium | Medium | Retention schedule and nightly job; audit trail of each run. | Low |
| R10 | Processor misuse (SMS gateway) | Low | Medium | SMS bodies contain no payroll amounts or health data, only a short alert and a link; only the phone number is shared. **[Client]**: data processing agreement with Mobile Sasa (DPA s.42). | Low |
| R11 | Logs leak personal data | Low | Medium | Logs mask secrets and NAV payloads; payroll amounts and passwords are never logged; log files kept 90 days. | Low |
| R12 | Unavailability (NAV or portal down) | Medium | Low | Read-only cached data still shows; requests queue and retry; nightly backups; health checks. | Low |
| R13 | Insider misuse by an administrator | Low | High | Admin actions audited; administrators cannot read encrypted secrets through the UI; **[Client]**: separate duties between portal administrators and payroll staff, and review the audit log monthly. | Medium |

## 4. Measures to reduce risk (summary)

Technical: authentication with MFA for privileged roles, role and department based access control, encryption in transit and at rest, audit logging, retention enforcement, dependency vulnerability checks in CI, automated security tests on every change (see [phase6-security-test-report.md](phase6-security-test-report.md)).

Organisational **[Client]**: privacy notice; staff briefing for HR and managers; processor agreements; access review each quarter; incident response plan with 72-hour notification to the ODPC (DPA s.43); named owner for the retention job and legal holds.

## 5. Consultation

| Who | When | Outcome |
|---|---|---|
| HR | **[Client]** | |
| Finance and payroll | **[Client]** | |
| IT and security | **[Client]** | |
| Staff representatives (if applicable) | **[Client]** | |

## 6. Sign-off

| Role | Name | Decision | Date |
|---|---|---|---|
| Data protection officer | | Approved / approved with conditions / rejected | |
| System owner (HR) | | | |
| IT | | | |

If any residual risk stays High, the controller must consult the ODPC before processing (DPA s.31(2)).

## 7. Change log

| Date | Change | By |
|---|---|---|
| | Version 1.0 prepared with the Phase 6 release | |
