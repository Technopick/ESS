using System.Text.Json;
using Ess.Application.Integration;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Adapters;

/// <summary>
/// Expense claims. The portal hands an approved claim to a NAV codeunit/API page that creates a document
/// (e.g. a purchase invoice for the employee vendor, or a staff-claim journal) for finance to post. It never posts to the G/L.
/// </summary>
public sealed class NavExpenseAdapter : NavAdapterBase, INavExpenseService
{
    public NavExpenseAdapter(NavConnectionManager c, NavODataClient o, NavSoapClient s, ILogger<NavExpenseAdapter> log) : base(c, o, s, log) { }

    public Task<NavResult<NavSubmissionReceipt>> SubmitClaimAsync(Guid companyId, NavExpenseClaimSubmission claim, CancellationToken ct) =>
        RunAsync(companyId, "Expense.Submit", async ctx =>
        {
            var linesJson = JsonSerializer.Serialize(claim.Lines.Select(l => new
            { lineNo = l.LineNo, expenseCode = l.ExpenseCode, date = l.Date.ToString("yyyy-MM-dd"), description = l.Description, amount = l.Amount, currency = l.Currency }));
            if (IsSoap(ctx, "Expense.Submit"))
            {
                var r = await Soap.InvokeCodeunitAsync(ctx, "Expense.Submit", new (string, object?)[]
                {
                    ("externalRef", claim.IdempotencyKey), ("portalRef", claim.PortalReference), ("employeeNo", claim.EmployeeNo),
                    ("purpose", claim.Purpose), ("currencyCode", claim.Currency), ("projectCode", claim.ProjectCode ?? ""),
                    ("costCentreCode", claim.CostCentreCode ?? ""), ("preApproved", claim.PreApproved), ("approvedBy", claim.ApprovedBy ?? ""),
                    ("linesJson", linesJson)
                }, true, ct);
                return r.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(r.Value!.ReturnValue)) : NavResult<NavSubmissionReceipt>.Fail(r.Error!);
            }
            var p = await OData.PostAsync(ctx, "Expense.Submit", new
            {
                externalReference = claim.IdempotencyKey, portalReference = claim.PortalReference, employeeNumber = claim.EmployeeNo,
                purpose = claim.Purpose, currencyCode = claim.Currency, projectCode = claim.ProjectCode ?? "", costCentreCode = claim.CostCentreCode ?? "",
                preApproved = claim.PreApproved, approvedBy = claim.ApprovedBy ?? "", linesJson
            }, claim.IdempotencyKey, ct);
            return p.Success ? NavResult<NavSubmissionReceipt>.Ok(ReceiptFrom(Str(p.Value, "documentNumber"), Str(p.Value, "status"))) : NavResult<NavSubmissionReceipt>.Fail(p.Error!);
        }, ct);

    public Task<NavResult<NavDocumentStatus?>> GetStatusAsync(Guid companyId, string navDocumentNo, CancellationToken ct) =>
        RunAsync(companyId, "Expense.Status", async ctx =>
        {
            var r = await Soap.InvokeCodeunitAsync(ctx, "Expense.Status", new (string, object?)[] { ("documentNo", navDocumentNo) }, false, ct);
            if (!r.Success) return NavResult<NavDocumentStatus?>.Fail(r.Error!);
            return NavResult<NavDocumentStatus?>.Ok(string.IsNullOrEmpty(r.Value!.ReturnValue) ? null
                : new NavDocumentStatus(navDocumentNo, r.Value.ReturnValue!, r.Value.OutParameters.GetValueOrDefault("statusReason"), null));
        }, ct);

    public Task<NavResult<NavSubmissionReceipt?>> FindByExternalReferenceAsync(Guid companyId, string idempotencyKey, CancellationToken ct) =>
        RunAsync(companyId, "Expense.FindByRef", async ctx =>
        {
            var r = await Soap.InvokeCodeunitAsync(ctx, "Expense.FindByRef", new (string, object?)[] { ("externalRef", idempotencyKey) }, false, ct);
            if (!r.Success) return NavResult<NavSubmissionReceipt?>.Fail(r.Error!);
            return NavResult<NavSubmissionReceipt?>.Ok(string.IsNullOrEmpty(r.Value!.ReturnValue) ? null : ReceiptFrom(r.Value.ReturnValue));
        }, ct);
}
