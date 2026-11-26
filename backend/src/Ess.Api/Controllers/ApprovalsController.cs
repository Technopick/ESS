using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Application.Workflow;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>
/// Approver inbox, decisions, request timelines and delegation (spec §5). Deciding needs no permission beyond being the
/// assignee: line managers come from NAV's hierarchy and may not hold an approver role. Delegation needs approvals.act.
/// </summary>
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/approvals")]
[Route("api/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IWorkflowEngine _engine;
    private readonly IEnumerable<IWorkflowDocumentHandler> _handlers;

    public ApprovalsController(EssDbContext db, ICurrentUser user, IWorkflowEngine engine, IEnumerable<IWorkflowDocumentHandler> handlers)
    {
        _db = db;
        _user = user;
        _engine = engine;
        _handlers = handlers;
    }

    private Guid Me => _user.UserId!.Value;

    public sealed record InboxItem(Guid TaskId, Guid InstanceId, string DocumentType, string Reference, string? Summary, string Requester, string Step,
        DateTime AssignedAtUtc, DateTime? DueAtUtc, bool Overdue, string? OnBehalfOf, bool Escalated, decimal? Amount);

    [HttpGet("inbox")]
    public async Task<IReadOnlyList<InboxItem>> Inbox([FromQuery] string? documentType, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await (from t in _db.ApprovalTasks.AsNoTracking()
                      join i in _db.WorkflowInstances on t.WorkflowInstanceId equals i.Id
                      join s in _db.WorkflowSteps on t.WorkflowStepId equals s.Id
                      join e in _db.Employees on i.RequesterEmployeeId equals e.Id
                      join o in _db.Users on t.OnBehalfOfUserId equals o.Id into oj from o in oj.DefaultIfEmpty()
                      where t.AssigneeUserId == Me && t.Status == ApprovalTaskStatus.Pending && i.Status == WorkflowInstanceStatus.Running
                            && (documentType == null || i.DocumentType.StartsWith(documentType))
                      orderby t.DueAtUtc ?? DateTime.MaxValue, t.CreatedAtUtc
                      select new InboxItem(t.Id, i.Id, i.DocumentType, i.DocumentReference, i.Summary, e.FirstName + " " + e.LastName, s.Name, t.CreatedAtUtc,
                          t.DueAtUtc, t.DueAtUtc != null && t.DueAtUtc < now, o != null ? o.UserName : null, t.EscalatedFromTaskId != null, i.Amount))
            .Take(500).ToListAsync(ct);
    }

    public sealed record DecidedItem(Guid TaskId, Guid InstanceId, string DocumentType, string Reference, string? Summary, string Requester,
        ApprovalTaskStatus Decision, string? Comment, DateTime? ActedAtUtc, WorkflowInstanceStatus RequestStatus);

    [HttpGet("decided")]
    public async Task<PagedResult<DecidedItem>> Decided([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var acted = new[] { ApprovalTaskStatus.Approved, ApprovalTaskStatus.Rejected, ApprovalTaskStatus.Returned };
        var q = from t in _db.ApprovalTasks.AsNoTracking()
                join i in _db.WorkflowInstances on t.WorkflowInstanceId equals i.Id
                join e in _db.Employees on i.RequesterEmployeeId equals e.Id
                where t.AssigneeUserId == Me && acted.Contains(t.Status)
                orderby t.ActedAtUtc descending
                select new DecidedItem(t.Id, i.Id, i.DocumentType, i.DocumentReference, i.Summary, e.FirstName + " " + e.LastName, t.Status, t.Comment, t.ActedAtUtc, i.Status);
        return await PagedResult<DecidedItem>.CreateAsync(q, page, pageSize, ct);
    }

    public sealed record TimelineEntry(ApprovalAction Action, string Actor, string? OnBehalfOf, string? Comment, DateTime OccurredAtUtc);
    public sealed record PendingWith(string Step, string Approver, DateTime? DueAtUtc);
    public sealed record InstanceView(Guid Id, string DocumentType, Guid DocumentId, string Reference, string? Summary, string? LinkUrl, string Requester,
        WorkflowInstanceStatus Status, DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<DocumentDetail> Details,
        IReadOnlyList<PendingWith> PendingWith, IReadOnlyList<TimelineEntry> Timeline, Guid? MyTaskId);

    /// <summary>A request's approval timeline. Visible to the requester, anyone it was assigned to, and approval monitors.</summary>
    [HttpGet("instances/{id:guid}")]
    public async Task<ActionResult<InstanceView>> Instance(Guid id, CancellationToken ct)
    {
        var i = await _db.WorkflowInstances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == _user.CompanyId, ct);
        if (i is null) return NotFound();
        var tasks = await _db.ApprovalTasks.AsNoTracking().Where(t => t.WorkflowInstanceId == id).ToListAsync(ct);
        if (i.RequesterUserId != Me && tasks.All(t => t.AssigneeUserId != Me) && !_user.HasPermission(Permissions.ApprovalsMonitor)) return NotFound();

        var userIds = tasks.Select(t => t.AssigneeUserId).Concat(tasks.Where(t => t.OnBehalfOfUserId != null).Select(t => t.OnBehalfOfUserId!.Value)).ToList();
        var history = await _db.ApprovalHistory.AsNoTracking().Where(h => h.WorkflowInstanceId == id).OrderBy(h => h.OccurredAtUtc).ToListAsync(ct);
        userIds.AddRange(history.Select(h => h.ActorUserId));
        userIds.AddRange(history.Where(h => h.OnBehalfOfUserId != null).Select(h => h.OnBehalfOfUserId!.Value));
        var names = await NamesAsync(userIds.Distinct().ToList(), ct);
        var steps = await _db.WorkflowSteps.AsNoTracking().Where(s => s.WorkflowDefinitionId == i.WorkflowDefinitionId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var requester = await _db.Employees.Where(e => e.Id == i.RequesterEmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstAsync(ct);

        var handler = _handlers.FirstOrDefault(h => h.DocumentType == i.DocumentType) ?? _handlers.FirstOrDefault(h => h.DocumentType == i.DocumentType.Split(':')[0]);
        var details = handler is null ? Array.Empty<DocumentDetail>() : await handler.DescribeAsync(i.DocumentId, ct);

        return new InstanceView(i.Id, i.DocumentType, i.DocumentId, i.DocumentReference, i.Summary, i.LinkUrl, requester, i.Status, i.CreatedAtUtc, i.CompletedAtUtc,
            details.Where(d => !string.IsNullOrWhiteSpace(d.Value)).ToList(),
            tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).Select(t => new PendingWith(steps.GetValueOrDefault(t.WorkflowStepId, "Approval"), names.GetValueOrDefault(t.AssigneeUserId, "Unknown"), t.DueAtUtc)).ToList(),
            history.Select(h => new TimelineEntry(h.Action, h.ActorUserId == Guid.Empty ? "System" : names.GetValueOrDefault(h.ActorUserId, "Unknown"),
                h.OnBehalfOfUserId is null ? null : names.GetValueOrDefault(h.OnBehalfOfUserId.Value), h.Comment, h.OccurredAtUtc)).ToList(),
            tasks.FirstOrDefault(t => t.AssigneeUserId == Me && t.Status == ApprovalTaskStatus.Pending)?.Id);
    }

    public sealed record DecisionInput(string? Comment);

    [HttpPost("tasks/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, DecisionInput input, CancellationToken ct)
    {
        await _engine.ActAsync(id, Me, ApprovalDecision.Approve, input.Comment, ct);
        return NoContent();
    }

    [HttpPost("tasks/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, DecisionInput input, CancellationToken ct)
    {
        await _engine.ActAsync(id, Me, ApprovalDecision.Reject, input.Comment, ct);
        return NoContent();
    }

    [HttpPost("tasks/{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, DecisionInput input, CancellationToken ct)
    {
        await _engine.ActAsync(id, Me, ApprovalDecision.Return, input.Comment, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- delegation

    public sealed record DelegationView(Guid Id, Guid ToUserId, string ToUser, DateTime StartsAtUtc, DateTime EndsAtUtc, string? DocumentType, string? Reason, bool Active);
    public sealed record DelegationInput(Guid ToUserId, DateTime StartsAtUtc, DateTime EndsAtUtc, string? DocumentType, string? Reason);

    [HttpGet("delegations")]
    [HasPermission(Permissions.ApprovalsAct)]
    public async Task<IReadOnlyList<DelegationView>> Delegations(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await (from d in _db.ApprovalDelegations.AsNoTracking()
                      join u in _db.Users on d.ToUserId equals u.Id
                      where d.FromUserId == Me && d.EndsAtUtc > now.AddDays(-30)
                      orderby d.StartsAtUtc descending
                      select new DelegationView(d.Id, d.ToUserId, u.UserName, d.StartsAtUtc, d.EndsAtUtc, d.DocumentType, d.Reason, d.StartsAtUtc <= now && d.EndsAtUtc > now))
            .ToListAsync(ct);
    }

    /// <summary>Delegates the caller's approvals for a period. New approvals go to the delegate; ones already assigned stay with the caller.</summary>
    [HttpPost("delegations")]
    [HasPermission(Permissions.ApprovalsAct)]
    public async Task<IActionResult> Delegate(DelegationInput input, [FromServices] IAuditWriter audit, CancellationToken ct)
    {
        if (input.ToUserId == Me) return Field("toUserId", "Choose someone other than yourself.");
        if (input.EndsAtUtc <= input.StartsAtUtc) return Field("endsAtUtc", "The end must be after the start.");
        if (input.EndsAtUtc > DateTime.UtcNow.AddDays(180)) return Field("endsAtUtc", "Delegate for at most 180 days at a time.");
        if (input.DocumentType is not null && !typeof(DocumentTypes).GetFields().Any(f => (string?)f.GetValue(null) == input.DocumentType))
            return Field("documentType", "Unknown request type.");
        var target = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == input.ToUserId && u.IsActive, ct);
        if (target is null) return Field("toUserId", "Choose an active user.");
        var canApprove = await _db.UserRoles.AnyAsync(ur => ur.UserId == target.Id && (ur.CompanyId == null || ur.CompanyId == _user.CompanyId)
            && ur.Role!.RolePermissions.Any(rp => rp.Permission!.Code == Permissions.ApprovalsAct), ct);
        if (!canApprove) return Field("toUserId", $"{target.UserName} cannot approve requests. Ask an administrator to give them an approver role.");

        var d = new ApprovalDelegation
        {
            CompanyId = _user.CompanyId!.Value, FromUserId = Me, ToUserId = target.Id, StartsAtUtc = input.StartsAtUtc, EndsAtUtc = input.EndsAtUtc,
            DocumentType = input.DocumentType, Reason = input.Reason?.Trim()
        };
        _db.ApprovalDelegations.Add(d);
        await _db.SaveChangesAsync(ct);
        await audit.WriteAsync("approvals.delegated", nameof(ApprovalDelegation), d.Id.ToString(), new { to = target.UserName, d.StartsAtUtc, d.EndsAtUtc, d.DocumentType }, ct: ct);
        return NoContent();
    }

    [HttpDelete("delegations/{id:guid}")]
    [HasPermission(Permissions.ApprovalsAct)]
    public async Task<IActionResult> EndDelegation(Guid id, CancellationToken ct)
    {
        var d = await _db.ApprovalDelegations.FirstOrDefaultAsync(x => x.Id == id && x.FromUserId == Me, ct);
        if (d is null) return NotFound();
        if (d.StartsAtUtc > DateTime.UtcNow) _db.ApprovalDelegations.Remove(d);
        else d.EndsAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record ApproverOption(Guid UserId, string Name);

    /// <summary>Users who can approve, for choosing a delegate. Returns names only.</summary>
    [HttpGet("approvers")]
    [HasPermission(Permissions.ApprovalsAct)]
    public async Task<IReadOnlyList<ApproverOption>> Approvers([FromQuery] string? search, CancellationToken ct)
    {
        var s = search?.Trim().ToLower();
        return await (from u in _db.Users.AsNoTracking()
                      where u.IsActive && u.Id != Me
                            && u.UserRoles.Any(ur => (ur.CompanyId == null || ur.CompanyId == _user.CompanyId) && ur.Role!.RolePermissions.Any(rp => rp.Permission!.Code == Permissions.ApprovalsAct))
                            && (s == null || u.UserName.ToLower().Contains(s) || u.Email.ToLower().Contains(s))
                      orderby u.UserName
                      select new ApproverOption(u.Id, u.UserName)).Take(50).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- monitoring

    public sealed record MonitorItem(Guid InstanceId, string DocumentType, string Reference, string? Summary, string Requester, DateTime SubmittedAtUtc,
        string? PendingWith, DateTime? DueAtUtc, bool Overdue);

    /// <summary>All running approvals for HR monitoring (approvals.monitor), oldest first.</summary>
    [HttpGet("monitor")]
    [HasPermission(Permissions.ApprovalsMonitor)]
    public async Task<PagedResult<MonitorItem>> Monitor([FromQuery] bool overdueOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var q = from i in _db.WorkflowInstances.AsNoTracking()
                join e in _db.Employees on i.RequesterEmployeeId equals e.Id
                let t = _db.ApprovalTasks.Where(t => t.WorkflowInstanceId == i.Id && t.Status == ApprovalTaskStatus.Pending).OrderBy(t => t.DueAtUtc).FirstOrDefault()
                where i.CompanyId == _user.CompanyId && i.Status == WorkflowInstanceStatus.Running && (!overdueOnly || (t != null && t.DueAtUtc < now))
                orderby i.CreatedAtUtc
                select new MonitorItem(i.Id, i.DocumentType, i.DocumentReference, i.Summary, e.FirstName + " " + e.LastName, i.CreatedAtUtc,
                    t == null ? null : _db.Users.Where(u => u.Id == t.AssigneeUserId).Select(u => u.UserName).FirstOrDefault(), t == null ? null : t.DueAtUtc,
                    t != null && t.DueAtUtc < now);
        return await PagedResult<MonitorItem>.CreateAsync(q, page, pageSize, ct);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(List<Guid> userIds, CancellationToken ct)
    {
        // Prefer the employee name linked to the user; fall back to the user name.
        var rows = await (from u in _db.Users.AsNoTracking()
                          where userIds.Contains(u.Id)
                          let e = (from m in _db.EmployeeMappings join e in _db.Employees on m.EmployeeId equals e.Id where m.UserId == u.Id select e.FirstName + " " + e.LastName).FirstOrDefault()
                          select new { u.Id, Name = e ?? u.UserName }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
