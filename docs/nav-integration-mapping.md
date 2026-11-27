# NAV integration mapping

Status: Phase 1 design. To be confirmed against the customer's NAV during discovery. Version 0.1, 26 November 2026.

Every NAV capability the portal uses has a **logical endpoint name**. Each NAV instance has one row per logical name in `nav_endpoint_mappings` saying which protocol and NAV object serve it, and optionally a field map that renames fields. Business code never changes when a customer's objects differ; only these rows do.

Legend for "Standard NAV": **Yes** = exists in a standard NAV/BC database; **Partial** = exists but lacks what the portal needs; **No** = needs an add-on or the ESS customisation in `nav-customizations.md`.

## 1. Discovery checklist (run against the customer's NAV)

1. Record the version and build (Help → About, or `Get-NAVServerInstance`), the localisation, and installed add-ons (Object Designer filtered on ID ranges 50000–99999 and partner ranges).
2. List published web services (page 810 "Web Services" in NAV, "Web Services" page in BC): object type, ID, service name, published flag.
3. For HR: does the Employee table have department, position, manager fields? Is there a leave application table, a leave ledger, leave periods, entitlements? Is there an HR approval workflow?
4. For payroll: which add-on, which tables hold periods, payslip header and lines, and is there a payslip report that can be saved as PDF?
5. For expenses: is there a staff claim / imprest module, or are claims purchase invoices against employee vendors?
6. Service tier auth (`ClientServicesCredentialType`), SOAP/OData ports, SSL, and whether OData V4 is enabled.
7. Run the portal's **Test connection** after filling in the mappings: every unpublished or misspelt service shows up as an HTTP 404 on its row.

## 2. Read endpoints (NAV → ESS)

| Logical name | Purpose | Sync | Standard NAV | NAV 2013–2018 (OData) | BC (API) |
|---|---|---|---|---|---|
| `Employee` | Employee master | Incremental every 15 min + nightly full | Yes, table 5200 (no department/position fields) | Page 5201 "Employee List" or ESS page, published as `ESSEmployees` | Standard `v2.0/employees` (no manager/department) or custom `ess/hr/v1.0/employees` |
| `Employee.Profile` | Personal details, next of kin, dependants, qualifications, masked bank | On demand | Partial (Alternative Address 5201, Employee Qualification 5203, Employee Relative 5205); bank details No | Codeunit method returning JSON | Custom API page or codeunit |
| `Department` | Departments | Every sync | No dedicated table; usually Dimension Values of Global Dimension 1 | Dimension Values page filtered to the department dimension | Standard `v2.0/dimensions(<id>)/dimensionValues` or custom. Head and parent department are maintained in the portal (HR, Organisation) unless the add-on exposes them. |
| `Position` | Job titles / positions | Every sync | No (Employee "Job Title" text only) | HR add-on table or ESS page | Custom |
| `Location` | Work locations | Every sync | Partial (Location table is for inventory; usually Global Dimension 2) | Dimension Values | Custom |
| `BaseCalendar` | Non-working days / public holidays | Daily | Yes (Base Calendar Change 7601) | Page on table 7601 | Custom API page |
| `LeaveType` | Leave types | Every sync | Partial (Cause of Absence 5206) | Page 5210 "Causes of Absence" or add-on | Standard `causesOfAbsence` (v1.0 API page) or custom |
| `LeaveBalance` | Entitlement, taken, pending, balance per type and period | On demand, cached minutes | No | Add-on or ESS query page | Custom |
| `PayrollPeriod` | Payroll periods | Every sync | No | Payroll add-on | Custom |
| `Payslip.Header` | Gross, deductions, net, payment date | On demand | No | Payroll add-on | Custom. The payroll summary report also reads headers by period and lines of type Statutory, for closed periods only. |
| `Payslip.Lines` | Earnings, allowances, PAYE, NSSF, SHIF, Housing Levy, other deductions, YTD | On demand | No | Payroll add-on | Custom |

### Default field maps

These are built into the adapters and can be overridden per instance.

**Employee (OData, NAV table 5200)**

| ESS field | NAV OData field | Notes |
|---|---|---|
| No | `No` | Key. |
| FirstName / MiddleName / LastName | `First_Name` / `Middle_Name` / `Last_Name` | |
| JobTitle, PositionCode | `Job_Title` | Override PositionCode if the add-on has a position code. |
| DepartmentCode | `Global_Dimension_1_Code` | Override to the add-on department field if there is one. |
| LocationCode | `Global_Dimension_2_Code` | |
| ManagerNo | `Manager_No` | Drives line-manager approvals. |
| CompanyEmail | `Company_E_Mail` | Used for notifications and user linking. |
| MobilePhone | `Mobile_Phone_No` | SMS notifications. |
| Status | `Status` | Active / Inactive / Terminated. Unknown values map to Inactive (no access). |
| EmploymentDate / ContractEndDate / TerminationDate | `Employment_Date` / `Contract_End_Date` / `Termination_Date` (BC API: `employmentDate` / `contractEndDate` / `terminationDate`) | Standard NAV has no contract end field; point `ContractEndDate` at the HR add-on's field in the field mapping. Contract expiry alerts and the contract expiry report depend on it. |
| BirthDate | `Birth_Date` | Only kept if birthday display is enabled. |
| LastModified | `Last_Date_Modified` | Date only in NAV, so incremental sync re-reads the whole watermark day. BC uses `lastModifiedDateTime` / `SystemModifiedAt`. |

**Employee (BC API v2.0)**: `number`, `givenName`, `middleName`, `surname`, `jobTitle`, `email`, `mobilePhone`, `status`, `employmentDate`, `terminationDate`, `birthDate`, `lastModifiedDateTime`. Department and manager are not in the standard API; a custom API page is recommended.

**Leave balance (ESS page)**: `Employee_No`, `Leave_Type`, `Leave_Period`, `Entitlement`, `Brought_Forward`, `Days_Taken`, `Days_Pending`, `Balance`.

**Payslip lines (ESS page)**: `Employee_No`, `Payroll_Period`, `Code`, `Description`, `Type` (Earning, Allowance, Bonus, Overtime, Statutory, Deduction, Relief, Information), `Amount`, `YTD_Amount`, `Line_No`. Kenyan statutory lines are identified by code (PAYE, NSSF, SHIF, AHL/Housing Levy) and are displayed exactly as NAV computed them.

## 3. Write endpoints (ESS → NAV)

All writes go through the outbox (section 6 of the architecture document) and carry an idempotency key.

| Logical name | Purpose | Standard NAV | Recommended object | Method / route |
|---|---|---|---|---|
| `Leave.Submit` | Create the leave application (pre-approved or for NAV approval) | No | ESS codeunit | `SubmitLeaveApplication` |
| `Leave.Validate` | Dry-run validation, returns NAV day count | No | ESS codeunit (optional) | `ValidateLeaveApplication` |
| `Leave.Cancel` | Cancel or withdraw | No | ESS codeunit | `CancelLeaveApplication` |
| `Leave.Status` | Status of a NAV leave document (for NAV-owned workflows) | No | ESS codeunit | `GetLeaveStatus` |
| `Leave.FindByRef` | Reconciliation lookup by idempotency key | No | ESS codeunit | `FindLeaveByExternalRef` |
| `Expense.Submit` | Create the claim document for finance | No | ESS codeunit | `SubmitExpenseClaim` |
| `Expense.Status` | Claim status / payment | No | ESS codeunit | `GetClaimStatus` |
| `Expense.FindByRef` | Reconciliation lookup | No | ESS codeunit | `FindClaimByExternalRef` |
| `Employee.Change` | Apply an HR-approved profile change | No | ESS codeunit | `ApplyEmployeeChange` |
| `HrRequest.Submit` | Requests that NAV must act on (optional) | No | ESS codeunit | `SubmitHrRequest` |
| `Payslip.Pdf` | NAV-rendered payslip as base64 PDF (optional) | No | ESS codeunit calling the payroll report with `SaveAsPdf` | `GetPayslipPdf` |

On BC the same logical names can point at custom API pages (`Protocol = BcApi`, e.g. `ess/hr/v1.0/leaveApplications`); the adapters then POST JSON with `externalReference` and read `documentNumber` from the response.

## 4. Workflow ownership per document (to confirm)

| Document | Default owner | Change it to NAV when |
|---|---|---|
| Leave application | Portal | NAV already approves leave in the HR add-on and HR wants to keep approving there. |
| Expense claim | Portal up to finance, NAV for posting | Finance approves claims in NAV approval workflows. |
| Profile change | Portal (HR review), then `Employee.Change` | Never; NAV has no change-request concept. |
| HR requests (letters, ID cards) | Portal | Rarely; only if NAV generates the letter. |

## 5. Example mapping rows

For a NAV 2018 instance with the ESS customisation installed:

| logical_name | protocol | service_name | method | modified_field |
|---|---|---|---|---|
| Employee | OData | ESSEmployees | | Last_Date_Modified |
| Department | OData | ESSDepartments | | |
| LeaveType | OData | ESSLeaveTypes | | |
| LeaveBalance | OData | ESSLeaveBalances | | |
| PayrollPeriod | OData | ESSPayrollPeriods | | |
| Payslip.Header | OData | ESSPayslipHeaders | | |
| Payslip.Lines | OData | ESSPayslipLines | | |
| Leave.Submit | Soap | ESSPortalGateway | SubmitLeaveApplication | |
| Leave.FindByRef | Soap | ESSPortalGateway | FindLeaveByExternalRef | |
| Expense.Submit | Soap | ESSPortalGateway | SubmitExpenseClaim | |

The development simulator ships with exactly these rows (`Simulator/SimulatorSetup.cs`).
