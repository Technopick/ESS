# UAT test scripts

Run these in the UAT environment described in [uat-plan.md](uat-plan.md). For each step write **P** (pass) or **F** (fail, with a defect number) in the Result column. "Check in NAV" steps are done by the NAV team in the NAV 2016 test company.

Tester: ______________________ Role: ______________________ Date: ____________

---

## EMP: Employee

### EMP-01 First sign-in and account security

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Open the invitation e-mail and follow the link | The set-password page opens | |
| 2 | Set a weak password such as `password1` | It is refused with a clear reason | |
| 3 | Set a strong password and sign in | The dashboard opens with your name | |
| 4 | Account > Security: turn on two-step verification with the authenticator app; save the recovery codes | MFA is on; recovery codes shown once | |
| 5 | Sign out, sign in again | You are asked for the 6-digit code; a wrong code is refused | |
| 6 | Sign out. Use "Forgot password" | A reset e-mail arrives; the link works once only | |
| 7 | Enter a wrong password 5 times | The account locks for a period and says so | |

### EMP-02 Dashboard and profile

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Look at the dashboard | Leave balances, upcoming leave, open requests, latest payslip and announcements are yours and correct | |
| 2 | Compare the leave balance with NAV (ask HR) | Same figures; "last updated" time shown | |
| 3 | Open My profile | Details match NAV | |
| 4 | Request a change of mobile number | The request shows as pending; personal details do not change yet | |

### EMP-03 Apply for leave (core flow)

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Leave > Apply: annual leave, 3 working days next month | Days are counted excluding weekends and public holidays | |
| 2 | Try dates that overlap an existing application | Refused, with the clash named | |
| 3 | Try more days than your balance (if the leave type does not allow negative) | Refused, with the balance shown | |
| 4 | Submit a valid application | Status "In approval"; a reference such as LV-2026-000123 is shown | |
| 5 | Check your e-mail and phone | Submission confirmation received; no balances or personal details in the SMS | |
| 6 | After your manager approves (MGR-02) | Status "Approved", then "Posted to NAV" once NAV confirms | |
| 7 | **Check in NAV** | The leave entry exists for the right employee, type, dates and days, once only | |

### EMP-04 Correct returned leave and cancel

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | After your manager returns the application with a comment (MGR-03) | Status "Returned for correction"; the comment is visible | |
| 2 | Edit the dates and resubmit | Status "In approval" again | |
| 3 | Apply for another day and cancel it before approval | Status "Cancelled"; your manager's task disappears | |

### EMP-05 Payslips

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Payslips: open the latest published period | Figures match your NAV payslip | |
| 2 | Download the PDF | The PDF opens and matches | |
| 3 | Try a period HR has not published | Not listed, or "not available yet" | |

### EMP-06 Expense claim

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Expenses > New claim with two lines and a receipt photo for each | Total is calculated; receipts upload | |
| 2 | Try uploading a `.exe` renamed to `.pdf` | Refused | |
| 3 | Submit | Status "In approval" | |
| 4 | After approvals (MGR-04, FIN-02) | Status follows to "Posted to NAV", then "Paid" when finance pays in NAV | |

### EMP-07 Documents

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Documents: upload a certificate | Listed under your documents | |
| 2 | Open a company policy HR published | It opens; the view is recorded | |
| 3 | Delete your own upload | It disappears from your list | |

### EMP-08 HR service request

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Requests > New: employment confirmation letter | Request created with a due date | |
| 2 | Add a comment and an attachment | Visible on the request | |
| 3 | When HR completes it (HR-04) | Status "Completed"; the letter is attached | |

### EMP-09 Attendance, training, directory, notifications

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Attendance: request a correction for a missed clock-in | Request goes to your manager | |
| 2 | Training: apply for an open programme | Application pending | |
| 3 | Directory: search a colleague | Work contact details only; no personal phone, birth date or salary | |
| 4 | Notifications bell | Unread count matches; marking read works | |
| 5 | Use the portal on a phone | All screens above are usable | |

### EMP-10 Data isolation (do not skip)

| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Open one of your leave applications and copy the address from the browser | Address contains an ID | |
| 2 | Ask another tester for one of their leave or claim addresses and open it while signed in as you | "Not found"; nothing of theirs is shown | |
| 3 | Try the HR, Reports and Admin addresses (`/hr/employees`, `/reports`, `/admin/users`) | Not available to you | |

---

## MGR: Line manager

### MGR-01 Team view
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Sign in (MFA if your role requires it) | Dashboard shows "Approvals waiting" | |
| 2 | Leave calendar / team attendance | Only your team is shown | |

### MGR-02 Approve leave (core flow)
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Approvals: open the employee's leave from EMP-03 | Dates, days, balance and clashes with other team members are shown | |
| 2 | Approve with a comment | Task leaves your inbox; the employee is notified | |

### MGR-03 Return and reject
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Return an application without a comment | Refused: a comment is required | |
| 2 | Return with a comment | Employee sees "Returned for correction" | |
| 3 | Reject a different application with a reason | Status "Rejected"; nothing is sent to NAV | |

### MGR-04 Approve an expense claim
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Open the claim from EMP-06; view each receipt | Receipts open; amounts match | |
| 2 | Approve | Moves to the finance step | |

### MGR-05 Delegation
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Approvals > Delegate to another manager for next week | Delegation saved | |
| 2 | During the period, a new request from your team arrives | It goes to the delegate; you can see it was delegated | |

### MGR-06 No self-approval
| # | Step | Expected | Result |
|---|---|---|---|
| 1 | Apply for leave yourself | It goes to **your** manager, never to you | |
| 2 | If you are a delegate for your own manager, check your inbox | Your own request is never in your inbox | |

---

## HR: HR officer and HR manager

| ID | Step | Expected | Result |
|---|---|---|---|
| HR-01 | HR > Employees: search, filter by department, "contracts ending in 90 days" | Correct lists; counts match NAV | |
| HR-02 | Open an employee: balances, documents, recent activity, account status | All correct; viewing is audited | |
| HR-03 | Approve the mobile number change from EMP-02 | **Check in NAV**: the number changed in NAV; the portal shows it after sync | |
| HR-04 | Requests: pick up the letter request, attach the letter, complete it | Employee notified; SLA shown as met | |
| HR-05 | HR > Leave: filter by status and NAV status; open a failed NAV posting if any | Error is understandable; retry works after the cause is fixed | |
| HR-06 | HR > Documents: publish a policy to the whole company, set an expiry for a certificate | Employees see the policy; expiry reminder scheduled | |
| HR-07 | HR > Announcements: schedule one for tomorrow | Appears on dashboards at the time; notification sent | |
| HR-08 | Confidential request type (for example grievance) | Only HR with the right permission sees the subject; reports hide it | |
| HR-09 | Reports: headcount, leave utilisation, leave applications, contract expiry; export one to Excel | Figures reconcile with NAV; export downloads and is audited | |
| HR-10 | Admin > Payroll (HR manager): publish a payroll period | Employees can now see that payslip | |

---

## FIN: Finance

| ID | Step | Expected | Result |
|---|---|---|---|
| FIN-01 | Sign in; MFA required for finance manager | Code requested | |
| FIN-02 | Approve the claim from EMP-06 at the finance step | Status "Approved", then "Posted to NAV" after NAV confirms | |
| FIN-03 | **Check in NAV** | One expense document with the right lines, amounts, project and cost centre; no duplicate even if the queue retried | |
| FIN-04 | Pay the claim in NAV | Within 30 minutes the portal shows "Paid" with the payment reference | |
| FIN-05 | Reports: expense claims for last month; payroll summary for a period | Totals match NAV | |

---

## EXEC: Management viewer

| ID | Step | Expected | Result |
|---|---|---|---|
| EXEC-01 | Management dashboard | Headcount, leave, claims and approvals figures load within a few seconds | |
| EXEC-02 | Filter by department and period; export | Figures change accordingly; export works | |
| EXEC-03 | Try to open an individual payslip | Not available | |

---

## ADM: System administrator

| ID | Step | Expected | Result |
|---|---|---|---|
| ADM-01 | Admin > Users: provision accounts for a department; resend an invite | Invites arrive | |
| ADM-02 | Assign and remove a role; deactivate a user | The user's access changes at next sign-in; a deactivated user cannot sign in | |
| ADM-03 | Admin > Roles: view permissions of each role | Matches the client's access matrix | |
| ADM-04 | Admin > Workflows: change the leave workflow to add an HR step for more than 10 days | Next long application follows the new route | |
| ADM-05 | Admin > Notifications: edit a template; send a test e-mail and SMS | Received; SMS from the client's sender ID | |
| ADM-06 | Background jobs page (`/admin/jobs`) | Only reachable by administrators; recurring jobs listed, including `data-retention` | |
| ADM-07 | Restore drill: run `Backup-EssDatabase.ps1`, restore the backup to a scratch database | Restore succeeds; documents still open with the keys | |

---

## INT: Integration administrator (NAV team)

| ID | Step | Expected | Result |
|---|---|---|---|
| INT-01 | Admin > NAV connection: test the connection | Success; the password is never shown back | |
| INT-02 | Review the endpoint mappings against the published NAV web services | All required services found | |
| INT-03 | Run a full sync; change an employee's department in NAV; wait for the incremental sync | Portal shows the new department within 15 minutes | |
| INT-04 | Stop the NAV service tier briefly, submit a leave application, restart NAV | The request waits in the queue, then posts once; no duplicate | |
| INT-05 | Admin > Integration: look at a failed item and the masked log | No passwords or full payloads in the log | |
| INT-06 | Approve a leave application in NAV directly, for a type where NAV owns the approval | Portal mirrors NAV's status within 10 minutes | |
