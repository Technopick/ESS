# Connecting the portal to NAV 2016: step by step

This guide connects an installed portal to the customer's NAV 2016 (9.0) server. Run the NAV steps on the NAV server as a NAV administrator, and the portal steps in the portal as the first administrator.

The portal never reads or writes NAV's SQL tables. It reads through published **pages** (OData V4) and writes through one published **codeunit** (SOAP), so NAV's own validation and posting logic always runs.

## What works with standard NAV, and what needs the ESS objects

| Portal feature | Reads from | Works with standard NAV? |
|---|---|---|
| Employee list, directory, org chart, user linking | Employee table 5200 | **Yes.** Publish page 5200 "Employee Card" (step 2). |
| Leave types | Cause of Absence 5206 | **Yes.** Publish page 5210 "Causes of Absence". |
| Leave balances | Your custom leave tables | No. Needs a list page over your table (ESS page 50101). |
| Payroll periods, payslips | Your custom payroll tables | No. Needs list pages over your tables (ESS pages 50103 to 50105). |
| Submitting leave, claims, profile changes | | No. Needs the ESS Portal Gateway codeunit 50100. |

The ESS objects are specified in `nav-customizations.md` but are **not built yet**, because they call your own leave and payroll tables and routines. Export those tables from Object Designer (select them, File, Export, .txt) and they can be written against your actual fields. Until then, start with employees (steps 1 to 7). Everything else plugs in the same way later.

## Step 1. Turn on web services on the NAV server

Open the **Dynamics NAV 2016 Administration Shell** as Administrator on the NAV server:

```powershell
Get-NAVServerInstance                          # note the instance name, normally DynamicsNAV90
$i = 'DynamicsNAV90'
Get-NAVServerConfiguration $i | Where-Object { $_.Key -match 'ClientServicesCredentialType|SOAPServices|ODataServices' }

Set-NAVServerConfiguration $i -KeyName SOAPServicesEnabled            -KeyValue true
Set-NAVServerConfiguration $i -KeyName ODataServicesEnabled           -KeyValue true
Set-NAVServerConfiguration $i -KeyName ODataServicesV4EndpointEnabled -KeyValue true
Set-NAVServerConfiguration $i -KeyName SOAPServicesPort  -KeyValue 7047   # defaults; change only if taken
Set-NAVServerConfiguration $i -KeyName ODataServicesPort -KeyValue 7048
Set-NAVServerInstance $i -Restart
```

If the portal runs on another machine, open the ports on the NAV server:

```powershell
New-NetFirewallRule -DisplayName 'NAV SOAP/OData for ESS portal' -Direction Inbound -Protocol TCP -LocalPort 7047,7048 -Action Allow
```

## Step 2. Publish the pages (and later the codeunit)

Either in NAV (**Departments, Administration, IT Administration, Services, Web Services**, page 810: New, Object Type, Object ID, Service Name, tick Published), or in the shell:

```powershell
$i = 'DynamicsNAV90'
# Standard objects, usable today
New-NAVWebService $i -ServiceName ESSEmployees  -ObjectType Page -ObjectId 5200 -Published $true   # Employee Card
New-NAVWebService $i -ServiceName ESSLeaveTypes -ObjectType Page -ObjectId 5210 -Published $true   # Causes of Absence

# ESS objects, once imported (numbers are placeholders in your licensed range)
# New-NAVWebService $i -ServiceName ESSLeaveBalances  -ObjectType Page     -ObjectId 50101 -Published $true
# New-NAVWebService $i -ServiceName ESSPayrollPeriods -ObjectType Page     -ObjectId 50103 -Published $true
# New-NAVWebService $i -ServiceName ESSPayslipHeaders -ObjectType Page     -ObjectId 50104 -Published $true
# New-NAVWebService $i -ServiceName ESSPayslipLines   -ObjectType Page     -ObjectId 50105 -Published $true
# New-NAVWebService $i -ServiceName ESSPortalGateway  -ObjectType Codeunit -ObjectId 50100 -Published $true

Get-NAVWebService $i | Where-Object ServiceName -like 'ESS*'
```

The service names must match the portal's mappings exactly. If you publish under other names, change the name on the mapping instead (step 5).

## Step 3. Give the portal a NAV user

The installed portal's application pool runs as the Windows account you gave setup ("NAV Windows account"). The simplest sign-in is to make that account a NAV user:

```powershell
$i = 'DynamicsNAV90'; $svc = 'TECHNOPICK\svc-ess'     # the account the ESS app pool runs as
New-NAVServerUser $i -WindowsAccount $svc
New-NAVServerUserPermissionSet $i -WindowsAccount $svc -PermissionSetId BASIC   # for the first test only
# Production: create permission set ESS-PORTAL (read on the published tables, execute on codeunit 50100), then
# New-NAVServerUserPermissionSet $i -WindowsAccount $svc -PermissionSetId ESS-PORTAL
# and remove BASIC. Never give the portal user SUPER.
```

Check from the **portal server**, signed in as that account (or `runas /user:TECHNOPICK\svc-ess powershell`). Use your NAV company name, with spaces as `%20`:

```powershell
$u = "http://NAVSERVER:7048/DynamicsNAV90/ODataV4/Company('Technopick%20Ltd')/ESSEmployees?`$top=1"
(Invoke-WebRequest $u -UseDefaultCredentials -UseBasicParsing).Content
```

You should see one employee as JSON. A 401 means the account is not a NAV user, and a 404 means the service or company name is wrong.

If the NAV server is not in the same domain, use **NAV user name and password** instead. In that case the instance needs `ClientServicesCredentialType = NavUserPassword` and an SSL certificate on the service ports. Create a NAV user `ESS-SVC` with a password, or with a web service access key on its user card.

## Step 4. Add the NAV connection in the portal

Go to **System administration, NAV connection, Add a NAV connection**:

| Field | Value |
|---|---|
| Name | For example *NAV 2016 Live* |
| Version | NAV 2016 |
| NAV server | The NAV server's host name, for example `NAVSERVER` |
| Server instance | `DynamicsNAV90` |
| HTTPS | Only if the service ports have a certificate |
| Fill in addresses | Click it. It fills the OData address `http://NAVSERVER:7048/DynamicsNAV90/ODataV4` and the SOAP address `http://NAVSERVER:7047/DynamicsNAV90/WS` |
| Sign-in | **Windows**. Leave the user name empty to use the application pool account. Or **NAV user name and password** / **Web service access key** |
| Timeout, parallel calls | Leave the defaults (60 seconds, 4) |

Save. The password or key is stored encrypted and never shown again.

## Step 5. Mappings: tell the portal which NAV object serves what

On the same page, under the connection, click **Add the standard ESS mappings**. This adds one row per feature (Employee, LeaveType, LeaveBalance, Payslip.Lines, Leave.Submit and so on) pointing at the ESS service names.

- **Rows you are not ready for:** click **Edit** and untick **Enabled** (for example payroll and the gateway rows until those objects exist). A disabled row turns that feature off and does not cause errors.
- **Different service name:** Edit, then change **Published service name**.
- **Your custom tables use other field names:** Edit, then fill **Field map (JSON)** with only the fields that differ. The portal field is on the left and the NAV OData field name is on the right. OData field names are the page's field captions with spaces and dots replaced by `_`. Open the OData URL from step 3 in a browser to see them. Examples:

```json
Employee:       {"DepartmentCode": "Department_Code", "ManagerNo": "Supervisor_No", "ContractEndDate": "Contract_Expiry_Date"}
LeaveBalance:   {"EmployeeNo": "Staff_No", "LeaveTypeCode": "Leave_Code", "PeriodCode": "Leave_Year", "Available": "Remaining_Days"}
Payslip.Lines:  {"PeriodCode": "Period", "Code": "ED_Code", "Group": "Category", "YTD": "Year_To_Date"}
```

These are the default NAV field names for each mapping. Map anything your tables call differently:

| Mapping | Fields the portal reads (portal = default NAV field) |
|---|---|
| Employee | No, FirstName = First_Name, LastName = Last_Name, JobTitle = Job_Title, DepartmentCode = Global_Dimension_1_Code, LocationCode = Global_Dimension_2_Code, ManagerNo = Manager_No, CompanyEmail = Company_E_Mail, MobilePhone = Mobile_Phone_No, Status, EmploymentDate = Employment_Date, TerminationDate = Termination_Date, ContractEndDate = Contract_End_Date, BirthDate = Birth_Date, Gender, LastModified = Last_Date_Modified |
| LeaveType | Code, Description, DefaultEntitlement = Total_Absence_Base, Paid, Gender |
| LeaveBalance | EmployeeNo = Employee_No, LeaveTypeCode = Leave_Type, PeriodCode = Leave_Period, Entitlement, BroughtForward = Brought_Forward, Taken = Days_Taken, Pending = Days_Pending, Available = Balance |
| PayrollPeriod | Code, Name, Start = Starting_Date, End = Ending_Date, PayDate = Pay_Date, Closed |
| Payslip.Header | EmployeeNo = Employee_No, PeriodCode = Payroll_Period, Currency = Currency_Code, Basic = Basic_Pay, Gross = Gross_Pay, Deductions = Total_Deductions, Net = Net_Pay, PaymentDate = Payment_Date |
| Payslip.Lines | EmployeeNo = Employee_No, PeriodCode = Payroll_Period, Code, Description, Group = Type, Amount, YTD = YTD_Amount, Sort = Line_No |

Standard NAV has no manager field on Employee. Line-manager approvals need `ManagerNo` mapped to wherever you keep the supervisor (a custom field on Employee, shown on the published page). Fields the page does not show come back empty; they cause no errors.

## Step 6. Link the portal company to the NAV company

Still on **NAV connection**, in **Companies**: choose the connection for your company and type the **NAV company name exactly as NAV shows it** (Companies page, or `Get-NAVCompany DynamicsNAV90`). Click save on the row.

## Step 7. Test and run the first sync

Go to **System administration, NAV integration**:

1. **Test connection.** Every enabled mapping is called once. A 404 on a row means that service is not published or is misspelt. A 401 means the sign-in failed (step 3).
2. **Full sync.** This loads all employees, departments, leave types and (when mapped) payroll periods. After that, changes sync every 15 minutes, and a full sync runs nightly to pick up deletions. **Sync changes now** forces an immediate incremental sync.
3. Failed calls and the queue of submissions to NAV are on the same page, with **Retry**.

Then check **HR administration, Employees**: the employees from NAV should be listed. Your admin account needs the HrOfficer or HrManager role to see that page (step 8).

## Step 8. Link portal users to NAV employees

- **Everyone at once:** **System administration, Users, Create accounts from NAV**. This creates an Employee account for each active NAV employee that has a Company E-Mail, links it to their employee number, and emails a set-password link. Employees without a Company E-Mail are skipped and counted. Click **Preview** first to see the counts, then **Create accounts**.
- **One person:** Users, the ⋮ menu on the user, **Link to NAV employee**, then enter the employee **No.**
- **Your own admin account:** link it the same way to your employee number if you also want to use leave and payslips yourself.
- **Roles:** on Users, the ⋮ menu, give HR staff **HrOfficer** or **HrManager**, finance **FinanceOfficer** or **FinanceManager**, and line managers **Supervisor** (or **DepartmentManager**). Who approves whose request comes from `ManagerNo` (step 5); the Supervisor role only lets that person act on approvals. An employee can never approve their own request.

## Troubleshooting

| Symptom | Fix |
|---|---|
| 401 on every row | The app pool account (or ESS-SVC) is not a NAV user, or the wrong sign-in type is set. Re-test with the `Invoke-WebRequest` from step 3, signed in as that account. |
| 404 on one row | The service is not published, or its name differs. Run `Get-NAVWebService DynamicsNAV90` and compare. |
| 404 on every row | The company name is wrong (step 6), or OData V4 is not enabled (step 1). |
| Connection refused or timeout | Wrong port or instance in the addresses, or the firewall on the NAV server (step 1). |
| Employees load, but there are no departments or managers | Map DepartmentCode and ManagerNo to your fields (step 5) and run Full sync again. |
| A field shows the wrong data | Check the field map. The NAV name is the OData name, so open the OData URL in a browser and copy the name from there. |
