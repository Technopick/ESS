# Required NAV customisations

Status: Phase 1 specification. Version 0.1, 26 November 2026. Object IDs are placeholders in the 50100–50149 range; use the customer's licensed range.

Standard NAV/BC does not include leave applications, leave balances, payroll or staff claims. The portal therefore needs a small, self-contained NAV customisation (C/AL for NAV 2013–BC14, an AL extension for BC15+). It adds no business rules of its own: every method calls the customer's existing HR/payroll add-on routines where they exist, so NAV logic is never bypassed.

## 1. Objects

| Type | ID | Name | Purpose |
|---|---|---|---|
| Table | 50100 | ESS External Reference | Idempotency: maps the portal's key to the NAV document it created. |
| Codeunit | 50100 | ESS Portal Gateway | All write and lookup methods, published as SOAP service `ESSPortalGateway`. |
| Page | 50100 | ESS Employees | Read-only list for OData (`ESSEmployees`), exposes department/position/manager fields from wherever the customer keeps them. |
| Page | 50101 | ESS Leave Balances | Read-only (or Query 50101) computing entitlement, taken, pending, balance per employee, type and period from the add-on's tables. |
| Page | 50102 | ESS Leave Types | If the add-on's leave type table differs from Cause of Absence. |
| Page | 50103–50105 | ESS Payroll Periods / Payslip Headers / Payslip Lines | Read-only views over the payroll add-on. Only closed and approved periods are exposed. |
| Page | 50106 | ESS Departments | If departments are not plain dimension values. |
| Permission set | ESS-PORTAL | Service user permissions | Read on the exposed tables, insert/modify only where the gateway writes, execute on the codeunit. No direct G/L posting permission. |

For BC15+ the pages are API pages (`PageType = API`, `APIPublisher = 'ess'`, `APIGroup = 'hr'`, `APIVersion = 'v1.0'`) and the writes can be either the codeunit (SOAP or as an unbound action) or API pages with `OnInsertRecord` calling the same codeunit procedures.

## 2. Table 50100 "ESS External Reference"

| Field | Type | Notes |
|---|---|---|
| External Reference | Code[50] | Primary key. The portal's idempotency key, e.g. `ESS-LEAVESUBMI-<32 hex>-0`. |
| Operation | Code[30] | e.g. LEAVE.SUBMIT. |
| Document Type | Option/Enum | Leave, Claim, EmployeeChange, ... |
| Document No. | Code[20] | The NAV document created. |
| Created At | DateTime | |
| Created By | Code[50] | Service user. |
| Portal Reference | Code[30] | Human-readable reference shown to users (LV-2026-000123). |

## 3. Codeunit 50100 "ESS Portal Gateway" (SOAP service `ESSPortalGateway`)

Rules for every write method:

1. First look up `External Reference`. If found, **return the existing Document No. and do nothing else** (this is what makes retries safe).
2. Validate using the add-on's own routines (balance, overlapping dates, calendar, employee status). Raise `ERROR('<plain message>')` for business rejections; the portal shows the message to the user and does not retry.
3. Create the document through the add-on's normal functions (not by assigning fields and skipping triggers), insert the External Reference row, and `COMMIT` only once at the end, so a failure leaves nothing behind.
4. Return the Document No. as the return value.

Parameter names below are as NAV exposes them in SOAP (it removes spaces and lower-cases the first letter); keep them exactly or update the adapter parameter lists.

```
SubmitLeaveApplication(externalRef: Code[50]; portalRef: Code[30]; employeeNo: Code[20]; leaveCode: Code[20];
    startDate: Date; endDate: Date; days: Decimal; reason: Text[250]; relieverNo: Code[20];
    preApproved: Boolean; approvedBy: Text[80]) : Code[20]
    // preApproved = TRUE: portal workflow already approved it; create in approved state (or submit and auto-approve).
    // preApproved = FALSE: create and send for approval through NAV's own workflow.

ValidateLeaveApplication(same parameters) : Decimal          // optional; returns NAV's day count, ERROR on rejection
CancelLeaveApplication(externalRef: Code[50]; documentNo: Code[20]; reason: Text[250]) : Code[20]
GetLeaveStatus(documentNo: Code[20]; VAR statusReason: Text[250]) : Text[30]
FindLeaveByExternalRef(externalRef: Code[50]) : Code[20]     // '' when not found

SubmitExpenseClaim(externalRef: Code[50]; portalRef: Code[30]; employeeNo: Code[20]; purpose: Text[250];
    currencyCode: Code[10]; projectCode: Code[20]; costCentreCode: Code[20]; preApproved: Boolean;
    approvedBy: Text[80]; linesJson: BigText) : Code[20]
    // Creates the claim document (purchase invoice for the employee vendor, or the add-on's staff claim)
    // for finance to review and post. Never posts to the G/L.
GetClaimStatus(documentNo: Code[20]; VAR statusReason: Text[250]) : Text[30]
FindClaimByExternalRef(externalRef: Code[50]) : Code[20]

ApplyEmployeeChange(externalRef: Code[50]; portalRef: Code[30]; employeeNo: Code[20]; section: Text[30];
    changesJson: BigText; approvedBy: Text[80]) : Code[20]
    // Applies HR-approved changes with VALIDATE() so field triggers run; logs to Change Log.

GetEmployeeProfile(employeeNo: Code[20]) : BigText            // JSON, see section 4
GetPayslipPdf(employeeNo: Code[20]; periodCode: Code[20]) : BigText   // base64 PDF of the payroll add-on's payslip report
```

JSON parameters: NAV 2013–2016 have no native JSON type; use the DotNet `Newtonsoft.Json` interop that ships with the service tier, or pass lines as a fixed delimited text. NAV 2017+ and BC have `JsonObject` / `JsonArray`.

## 4. Profile JSON returned by `GetEmployeeProfile`

```json
{
  "personal": { "nationalId": "…", "kraPin": "…", "nssfNo": "…", "shifNo": "…", "maritalStatus": "…", "gender": "…" },
  "address": { "address": "…", "city": "…", "county": "…", "postCode": "…" },
  "emergencyContacts": [ { "name": "…", "relationship": "…", "phone": "…" } ],
  "nextOfKin": [ { "name": "…", "relationship": "…", "phone": "…", "percentage": "…" } ],
  "dependants": [ { "name": "…", "relationship": "…", "birthDate": "…" } ],
  "qualifications": [ { "code": "…", "description": "…", "institution": "…", "from": "…", "to": "…", "expires": "…" } ],
  "bank": { "bankName": "…", "branch": "…", "accountNo": "…" }
}
```

The portal masks `accountNo` to the last four digits before display, and masks ID/PIN/NSSF/SHIF numbers in all logs. Include only sections the customer wants employees to see.

## 5. Service tier and security set-up

1. Create a dedicated service user (e.g. `ESS-SVC`) with permission set ESS-PORTAL only. No SUPER.
2. NAV on-premises: use NavUserPassword over HTTPS, or Windows auth if the portal server is in the domain. Configure the SOAP and OData V4 ports with an SSL certificate.
3. BC online: register an Entra ID app, grant `API.ReadWrite.All` (application), and add it in BC's "Microsoft Entra Applications" page with the ESS-PORTAL permission set.
4. Publish the codeunit and pages in "Web Services" with the service names in the mapping document.
5. Enable change log on the Employee table for fields the portal can change.

## 6. Test checklist for the customisation

* Calling `SubmitLeaveApplication` twice with the same `externalRef` returns the same Document No. and creates one document.
* A request exceeding the balance raises an ERROR with a readable message.
* A pre-approved submission does not enter NAV's approval workflow; a non-pre-approved one does.
* `FindLeaveByExternalRef` returns '' for an unknown key.
* The service user cannot read tables outside the permission set (for example G/L Entry).
