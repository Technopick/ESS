using System.Globalization;
using System.Text.Json;
using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>
/// Leave adapter. Standard NAV only has "Cause of Absence" and "Employee Absence" (no applications, balances or approvals),
/// so leave applications and balances come from the customer's HR add-on or from the ESS NAV extension documented in
/// docs/nav-customizations.md. Endpoints:
///   LeaveType (read), LeaveBalance (read), Leave.Submit / Leave.Cancel / Leave.Validate / Leave.Status / Leave.FindByRef (codeunit or API page).
/// </summary>
public sealed class NavLeaveAdapter : NavAdapterBase, INavLeaveService
{
    public static readonly IReadOnlyDictionary<string, string> TypeOData = new Dictionary<string, string>
    { ["Code"] = "Code", ["Description"] = "Description", ["DefaultEntitlement"] = "Total_Absence_Base", ["Paid"] = "Paid", ["Gender"] = "Gender" };
    public static readonly IReadOnlyDictionary<string, string> TypeBc = new Dictionary<string, string>
    { ["Code"] = "code", ["Description"] = "description", ["DefaultEntitlement"] = "defaultEntitlement", ["Paid"] = "paid", ["Gender"] = "gender" };
    public static readonly IReadOnlyDictionary<string, string> BalOData = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "Employee_No", ["LeaveTypeCode"] = "Leave_Type", ["PeriodCode"] = "Leave_Period", ["Entitlement"] = "Entitlement",
        ["BroughtForward"] = "Brought_Forward", ["Taken"] = "Days_Taken", ["Pending"] = "Days_Pending", ["Available"] = "Balance"
    };
    public static readonly IReadOnlyDictionary<string, string> BalBc = new Dictionary<string, string>
    {
        ["EmployeeNo"] = "employeeNumber", ["LeaveTypeCode"] = "leaveTypeCode", ["PeriodCode"] = "leavePeriod", ["Entitlement"] = "entitlement",
        ["BroughtForward"] = "broughtForward", ["Taken"] = "daysTaken", ["Pending"] = "daysPending", ["Available"] = "balance"
    };

    public NavLeaveAdapter(NavConnectionManager c, NavODataClient o, NavSoapClient s, ILogger<NavLeaveAdapter> log) : base(c, o, s, log) { }

    public Task<NavResult<IReadOnlyList<NavLeaveTypeRecord>>> GetLeaveTypesAsync(Guid companyId, CancellationToken ct) =>
        RunAsync(companyId, "LeaveType.List", async ctx =>
        {
            var map = Mapper(ctx, "LeaveType", TypeOData, TypeBc);
            var rows = await OData.GetAllAsync(ctx, "LeaveType", new NavODataClient.Query(), ct);
            return rows.Map<IReadOnlyList<NavLeaveTypeRecord>>(list => list.Select(r => new NavLeaveTypeRecord(
                map.RequireString(r, "Code"), map.GetString(r, "Description") ?? map.RequireString(r, "Code"),
                map.GetDecimal(r, "DefaultEntitlement"), map.GetBool(r, "Paid"), map.GetString(r, "Gender"), Str(r, "@odata.etag"))).ToList());
        }, ct);

    public Task<NavResult<IReadOnlyList<NavLeaveBalanceRecord>>> GetBalancesAsync(Guid companyId, string employeeNo, CancellationToken ct) =>
        RunAsync(companyId, "LeaveBalance.Get", async ctx =>
        {
            var map = Mapper(ctx, "LeaveBalance", BalOData, BalBc);
            var rows = await OData.GetAllAsync(ctx, "LeaveBalance",
                new NavODataClient.Query(Filter: $"{map.NavField("EmployeeNo")} eq {NavODataClient.Literal(employeeNo)}"), ct);
            return rows.Map<IReadOnlyList<NavLeaveBalanceRecord>>(list => list.Select(r => new NavLeaveBalanceRecord(
                employeeNo, map.RequireString(r, "LeaveTypeCode"), map.GetString(r, "PeriodCode") ?? "",
                map.GetDecimal(r, "Entitlement"), map.GetDecimal(r, "BroughtForward"), map.GetDecimal(r, "Taken"),
                map.GetDecimal(r, "Pending"), map.GetDecimal(r, "Available"))).ToList());
        }, ct);

    public Task<NavResult<decimal>> ValidateAsync(Guid companyId, NavLeaveSubmission s, CancellationToken ct) =>
        RunAsync(companyId, "Leave.Validate", async ctx =>
        {
            if (ctx.FindEndpoint("Leave.Validate") is null)
                return NavResult<decimal>.Ok(s.Days); // NAV offers no dry-run: NAV will validate on submit.
            var r = await Soap.InvokeCodeunitAsync(ctx, "Leave.Validate", Params(s), false, ct);
            if (!r.Success) return NavResult<decimal>.Fail(r.Error!);
            return decimal.TryParse(r.Value!.ReturnValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var days)
                ? NavResult<decimal>.Ok(days)
                : NavResult<decimal>.Fail(NavErrorCategory.Configuration, "Leave.Validate did not return a day count.");
        }, ct);

    public Task<NavResult<NavSubmissionReceipt>> SubmitAsync(Guid companyId, NavLeaveSubmission s, CancellationToken ct) =>
        RunAsync(companyId, "Leave.Submit", async ctx =>
        {
            if (IsSoap(ctx, "Leave.Submit"))
            {
                var r = await Soap.InvokeCodeunitAsync(ctx, "Leave.Submit", Params(s), true, ct);
                return r.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(r.Value!.ReturnValue)) : NavResult<NavSubmissionReceipt>.Fail(r.Error!);
            }
            var p = await OData.PostAsync(ctx, "Leave.Submit", new
            {
                externalReference = s.IdempotencyKey, portalReference = s.PortalReference, employeeNumber = s.EmployeeNo,
                leaveTypeCode = s.LeaveTypeCode, startDate = s.StartDate.ToString("yyyy-MM-dd"), endDate = s.EndDate.ToString("yyyy-MM-dd"),
                days = s.Days, reason = s.Reason ?? "", relieverNumber = s.RelieverNo ?? "", preApproved = s.PreApproved, approvedBy = s.ApprovedBy ?? ""
            }, s.IdempotencyKey, ct);
            return p.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(Str(p.Value, "documentNumber"), Str(p.Value, "status"))) : NavResult<NavSubmissionReceipt>.Fail(p.Error!);
        }, ct);

    public Task<NavResult<NavSubmissionReceipt>> CancelAsync(Guid companyId, string navDocumentNo, string idempotencyKey, string reason, CancellationToken ct) =>
        RunAsync(companyId, "Leave.Cancel", async ctx =>
        {
            var r = await Soap.InvokeCodeunitAsync(ctx, "Leave.Cancel", new (string, object?)[]
                { ("externalRef", idempotencyKey), ("documentNo", navDocumentNo), ("reason", reason) }, true, ct);
            return r.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(r.Value!.ReturnValue ?? navDocumentNo, "Cancelled")) : NavResult<NavSubmissionReceipt>.Fail(r.Error!);
        }, ct);

    public Task<NavResult<NavDocumentStatus?>> GetStatusAsync(Guid companyId, string navDocumentNo, CancellationToken ct) =>
        RunAsync(companyId, "Leave.Status", async ctx =>
        {
            var r = await Soap.InvokeCodeunitAsync(ctx, "Leave.Status", new (string, object?)[] { ("documentNo", navDocumentNo) }, false, ct);
            if (!r.Success) return NavResult<NavDocumentStatus?>.Fail(r.Error!);
            return NavResult<NavDocumentStatus?>.Ok(string.IsNullOrEmpty(r.Value!.ReturnValue) ? null
                : new NavDocumentStatus(navDocumentNo, r.Value.ReturnValue!, r.Value.OutParameters.GetValueOrDefault("statusReason"), null));
        }, ct);

    public Task<NavResult<NavSubmissionReceipt?>> FindByExternalReferenceAsync(Guid companyId, string idempotencyKey, CancellationToken ct) =>
        RunAsync(companyId, "Leave.FindByRef", async ctx =>
        {
            var r = await Soap.InvokeCodeunitAsync(ctx, "Leave.FindByRef", new (string, object?)[] { ("externalRef", idempotencyKey) }, false, ct);
            if (!r.Success) return NavResult<NavSubmissionReceipt?>.Fail(r.Error!);
            return NavResult<NavSubmissionReceipt?>.Ok(string.IsNullOrEmpty(r.Value!.ReturnValue) ? null : ReceiptFrom(r.Value.ReturnValue));
        }, ct);

    private static (string, object?)[] Params(NavLeaveSubmission s) => new (string, object?)[]
    {
        ("externalRef", s.IdempotencyKey), ("portalRef", s.PortalReference), ("employeeNo", s.EmployeeNo), ("leaveCode", s.LeaveTypeCode),
        ("startDate", s.StartDate), ("endDate", s.EndDate), ("days", s.Days), ("reason", s.Reason ?? ""), ("relieverNo", s.RelieverNo ?? ""),
        ("preApproved", s.PreApproved), ("approvedBy", s.ApprovedBy ?? "")
    };
}
