# User acceptance testing (UAT) plan

UAT is where the client's own staff confirm that the portal does their job correctly against **their** NAV 2016 data before it goes live. The step-by-step scripts are in [uat-test-scripts.md](uat-test-scripts.md).

## 1. Scope

In scope: every module in the portal as delivered: sign-in and account security, dashboard, profile and change requests, leave, payslips, expense claims, documents, HR service requests, attendance, training, announcements, approvals and delegation, notifications (in-app, e-mail and SMS through Mobile Sasa), HR administration, reports and exports, the management dashboard, user and role administration, workflows, NAV connection settings and the integration monitor.

Also in scope: the round trip to NAV for every request that posts (leave, expense claims, profile changes, HR requests with a NAV action), checked **in NAV** by the client's NAV team, not only in the portal.

Out of scope: NAV's own processing after posting (payroll runs, payments), load testing (done in Phase 6, see [performance-test-report.md](performance-test-report.md)) and penetration testing by a third party (recommended before go-live, see the security report).

## 2. Environment

| Item | UAT setting |
|---|---|
| Portal | Separate UAT site on the client's IIS server, installed with `Install-EssPortal.ps1 -Environment UAT -SiteName 'ESS Portal UAT' -AppPoolName EssPortalUat` and its own host name, install folder and data folder |
| Database | Separate PostgreSQL database `ess_uat`; never the production database |
| NAV | A NAV 2016 **test company or test server** holding a recent copy of production. Never the live company. |
| E-mail | Client SMTP relay; testers' real mailboxes |
| SMS | Mobile Sasa with the client's sender ID; testers' phones only |
| Data | Real employee master data copied from NAV. Payslips for testers only, or a payroll period HR has cleared for testing. |
| Access | Testers sign in with their own accounts. HR assigns roles per the tester list below. |

Because UAT uses a copy of real personal data, the DPIA controls apply: access only for named testers, no exports leave the client's network, and the UAT database is wiped (or the UAT NAV copy refreshed) after sign-off.

## 3. Roles and testers

| Role in the portal | Who tests it | Scripts |
|---|---|---|
| Employee | 3 to 5 staff from different departments, at least one who uses a phone only | EMP |
| Line manager (Supervisor, Department manager) | 2 managers with direct reports among the employee testers | MGR |
| HR officer and HR manager | 2 from HR | HR |
| Finance officer and finance manager | 1 to 2 from finance | FIN |
| Management viewer | 1 senior manager | EXEC |
| System administrator | Client IT | ADM |
| Integration administrator | Client NAV team | INT |

Testers need: a work e-mail, a mobile number in NAV, the authenticator app (for roles that require MFA), and 30 to 90 minutes per script set.

## 4. Schedule

| Step | Duration | Exit when |
|---|---|---|
| Set-up: install UAT, connect NAV test company, run full sync, create tester accounts | 1 to 2 days | Sync shows all employees; testers can sign in |
| Briefing session with testers (walk-through, how to log defects) | 1 hour | Testers have scripts and the defect log |
| Cycle 1: all scripts | 5 working days | All scripts run once |
| Fix window | 3 to 5 days | Critical and high defects fixed and deployed to UAT |
| Cycle 2: failed scripts plus a regression pass of the core flows (EMP-03, MGR-02, FIN-02, INT-03) | 3 days | Exit criteria met |
| Sign-off | 1 day | Signatures in section 8 |

## 5. Entry criteria

* Phases 1 to 6 merged and the UAT build installed from the CI package.
* NAV customisations (web services, codeunits, approval settings) published in the NAV test company per [nav-customizations.md](nav-customizations.md).
* NAV connection test passes in Admin > NAV connection, and a full sync has completed without errors.
* SMTP and Mobile Sasa configured, with a test message received.
* Tester accounts provisioned and roles assigned.

## 6. Defects

Log each defect in the shared defect log (the spreadsheet or tracker the client prefers) with: script ID and step, what happened, what was expected, screenshot, tester, date. Never paste payslip figures or ID numbers into the log; describe them instead.

| Severity | Meaning | Example | Go-live rule |
|---|---|---|---|
| Critical | Wrong data, data shown to the wrong person, or a core flow blocked with no workaround | Employee sees another's payslip; approved leave never reaches NAV | Must be fixed |
| High | A core flow fails or gives a wrong result, but a workaround exists | Expense claim posts to NAV with the wrong cost centre | Must be fixed |
| Medium | A secondary function fails, or a confusing message | Report filter ignores location | Fix or agree a date |
| Low | Cosmetic or wording | Typo, misaligned column | Optional |

## 7. Exit criteria

* 100% of scripts executed; at least 95% passed.
* No open critical or high defects.
* Every NAV posting test confirmed in NAV by the NAV team (leave, expense, profile change, HR request).
* No access control failure in the data isolation checks (EMP-10, MGR-06, HR-08).
* Medium defects either fixed or accepted in writing with a target date.
* DPIA signed by the DPO, and the retention schedule periods marked "client to confirm" decided.

## 8. Sign-off

| Area | Name | Signature | Date |
|---|---|---|---|
| HR | | | |
| Finance | | | |
| IT | | | |
| NAV team | | | |
| Data protection officer | | | |
| Project sponsor | | | |
