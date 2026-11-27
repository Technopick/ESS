# Required NAV customisations

Status: updated for the customer's NAV 2016 (9.0). Version 0.2, 27 November 2026. Object IDs are placeholders in the 50100–50149 range; use the customer's licensed range.

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
    approvedBy: Text[80]; linesXml: BigText) : Code[20]
    // NAV 2013 to 2018 (C/AL) receive linesXml; BC (AL) receives linesJson with the same fields.
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

Structured parameters: C/AL (NAV 2013 to 2018) has no JSON type, so for these versions the portal sends expense lines as XML, which C/AL reads with an XMLport (from an InStream over the BigText) or with codeunit 6224 "XML DOM Management":

```xml
<Lines>
  <Line lineNo="10" expenseCode="TRAVEL" date="2026-11-03" amount="1234.5" currency="KES">Taxi to the airport</Line>
</Lines>
```

Amounts use a full stop as the decimal separator and dates are yyyy-MM-dd whatever the NAV server's regional settings, so evaluate them with `EVALUATE(Amount, Text, 9)` (XML format). `changesJson` and the profile returned by `GetEmployeeProfile` stay JSON text: the portal builds and reads them, and in C/AL they are only assembled as text (see section 4) or applied field by field.

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

## 6. NAV 2016 (customer) set-up

The customer runs NAV 2016 with standard HR plus extensions and custom tables where needed. For this version:

1. **Packaging.** Deliver the ESS objects as a .fob in the customer's licensed range, or as a NAV 2016 Extension (.navx) if the objects only add new tables, pages and codeunits. An extension keeps the base application untouched and is easy to remove. Either way the objects call the existing HR and payroll routines; where the customer has custom tables (for example leave applications or payroll lines), the ESS pages read those tables and the gateway calls their existing posting functions.
2. **Service tier (NAV Server Administration, instance DynamicsNAV90 by default).** Enable SOAP Services (port 7047) and OData Services (port 7048), and check that "Enable OData V4 Endpoint" is ticked. Set "Max Page Size" for OData (default 1000; the portal pages by itself, so any value works). Restart the instance after the change.
3. **Authentication.** NAV 2016 supports Windows and NavUserPassword (no OAuth). With the portal on Windows/IIS in the same domain, Windows authentication is simplest: run the IIS application pool as a domain service account, create that account as a NAV Windows user with the ESS-PORTAL permission set, and leave the user name empty on the portal's NAV connection screen. With NavUserPassword the instance needs an SSL certificate; the portal can then use the user's password or its web service access key.
4. **Publish** page 50100 to 50106 and codeunit 50100 in page 810 "Web Services" with the service names in the mapping document (ESSEmployees, ESSLeaveTypes, ESSLeaveBalances, ESSPayrollPeriods, ESSPayslipHeaders, ESSPayslipLines, ESSDepartments, ESSPositions, ESSLocations, ESSPortalGateway), tick Published, and copy the OData V4 and SOAP URLs shown there.
5. **In the portal:** System administration, NAV connection, Add a NAV connection, choose NAV 2016, enter the server name and use "Fill in addresses", choose the sign-in, save, then "Add the standard ESS mappings". Link the portal company to the NAV company name exactly as NAV shows it, and run Test connection on the NAV integration page.

What the portal already handles for NAV 2016:

* Reads use OData V4 (`/ODataV4/Company('<name>')/<service>`); writes use the SOAP gateway, so NAV business logic always runs.
* "Last Date Modified" on Employee is a date without time. The incremental sync re-reads the last day, and a full sync runs nightly to catch deletions.
* `$top` ends a NAV OData result without a next link, so the employee sync pages with `$skip` (and follows NAV's next link when there is one).
* Option values in OData V4 arrive encoded (`Sick_x0020_Leave`, `_blank_`); the portal turns them back into captions.
* The record version comes from `@odata.etag`.
* SOAP dates are sent as yyyy-MM-dd and decimals with a full stop.

## 7. Test checklist for the customisation

* Calling `SubmitLeaveApplication` twice with the same `externalRef` returns the same Document No. and creates one document.
* A request exceeding the balance raises an ERROR with a readable message.
* A pre-approved submission does not enter NAV's approval workflow; a non-pre-approved one does.
* `FindLeaveByExternalRef` returns '' for an unknown key.
* The service user cannot read tables outside the permission set (for example G/L Entry).
