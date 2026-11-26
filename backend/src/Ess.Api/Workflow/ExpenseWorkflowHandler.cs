using Ess.Api.Services;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Workflow;

public sealed class ExpenseWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    private readonly IServiceProvider _sp;

    public ExpenseWorkflowHandler(EssDbContext db, IServiceProvider sp)
    {
        _db = db;
        _sp = sp;
    }

    public string DocumentType => DocumentTypes.Expense;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Claim not found.");
        await _db.Entry(claim).Collection(x => x.Lines).LoadAsync(ct);
        switch (c.Status)
        {
            case WorkflowInstanceStatus.Approved:
                claim.Status = RequestStatus.Approved;
                var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == claim.EmployeeId, ct);
                var approver = c.ActorUserId == Guid.Empty ? "ESS (auto)" : await _db.Users.Where(u => u.Id == c.ActorUserId).Select(u => u.UserName).FirstAsync(ct);
                await _sp.GetRequiredService<ExpenseService>().EnqueueAsync(employee, claim, preApproved: true, approver, ct);
                break;
            case WorkflowInstanceStatus.Rejected:
                claim.Status = RequestStatus.Rejected;
                break;
            case WorkflowInstanceStatus.Returned:
                claim.Status = RequestStatus.ReturnedForCorrection;
                break;
        }
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.Id == documentId, ct);
        var cats = await _db.ExpenseCategories.Where(x => x.CompanyId == claim.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var list = new List<DocumentDetail>
        {
            new("Purpose", claim.Purpose),
            new("Total", $"{claim.Currency} {claim.TotalAmount:N2}"),
            new("Project", claim.ProjectCode),
            new("Cost centre", claim.CostCentreCode),
        };
        foreach (var l in claim.Lines.OrderBy(l => l.LineNo))
            list.Add(new($"{l.ExpenseDate:d MMM} · {cats.GetValueOrDefault(l.CategoryId, "Expense")}",
                $"{l.Description}: {l.Currency} {l.Amount:N2}{(l.Currency != claim.Currency ? $" × {l.ExchangeRate}" : "")}{(l.ReceiptDocumentId is null ? " (no receipt)" : $" [receipt:{l.ReceiptDocumentId}]")}"));
        return list;
    }
}
