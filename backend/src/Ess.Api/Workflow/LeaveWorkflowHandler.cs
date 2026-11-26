using Ess.Api.Services;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Workflow;

/// <summary>Approved leave is queued for NAV as pre-approved; it shows as "Approved, pending NAV" until NAV confirms (rule 11).</summary>
public sealed class LeaveWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    private readonly IServiceProvider _sp;

    public LeaveWorkflowHandler(EssDbContext db, IServiceProvider sp)
    {
        _db = db;
        _sp = sp;
    }

    public string DocumentType => DocumentTypes.Leave;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var app = await _db.LeaveApplications.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Leave application not found.");
        switch (c.Status)
        {
            case WorkflowInstanceStatus.Approved:
                app.Status = RequestStatus.Approved;
                var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == app.EmployeeId, ct);
                var type = await _db.LeaveTypes.AsNoTracking().FirstAsync(t => t.Id == app.LeaveTypeId, ct);
                var approver = c.ActorUserId == Guid.Empty ? "ESS (auto)" : await _db.Users.Where(u => u.Id == c.ActorUserId).Select(u => u.UserName).FirstAsync(ct);
                // Resolved lazily: LeaveService depends on the workflow engine, which depends on this handler.
                await _sp.GetRequiredService<LeaveService>().EnqueueSubmitAsync(employee, type, app, preApproved: true, approver, ct);
                break;
            case WorkflowInstanceStatus.Rejected:
                app.Status = RequestStatus.Rejected;
                break;
            case WorkflowInstanceStatus.Returned:
                app.Status = RequestStatus.ReturnedForCorrection;
                break;
        }
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var a = await (from l in _db.LeaveApplications.AsNoTracking()
                       join t in _db.LeaveTypes on l.LeaveTypeId equals t.Id
                       join e in _db.Employees on l.EmployeeId equals e.Id
                       join r in _db.Employees on l.RelieverEmployeeId equals r.Id into rj from r in rj.DefaultIfEmpty()
                       where l.Id == documentId
                       select new { l, Type = t.Name, Employee = e.FirstName + " " + e.LastName, e.Id, Reliever = r != null ? r.FirstName + " " + r.LastName : null })
            .FirstAsync(ct);
        var balance = await _db.LeaveBalancesCache.AsNoTracking().Where(b => b.EmployeeId == a.Id && b.LeaveTypeId == a.l.LeaveTypeId)
            .Select(b => (decimal?)b.Available).FirstOrDefaultAsync(ct);
        return new List<DocumentDetail>
        {
            new("Employee", a.Employee),
            new("Leave type", a.Type),
            new("Dates", $"{a.l.StartDate:ddd d MMM yyyy}{(a.l.StartHalfDay ? " (afternoon)" : "")} to {a.l.EndDate:ddd d MMM yyyy}{(a.l.EndHalfDay ? " (morning)" : "")}"),
            new("Days", a.l.DaysRequested.ToString("0.#")),
            new("Available in NAV before this", balance?.ToString("0.#") ?? "Unknown"),
            new("Reliever", a.Reliever),
            new("Reason", a.l.Reason),
        };
    }
}
