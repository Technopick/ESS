using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Application.Workflow;
using Ess.Domain.Identity;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ess.Infrastructure.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    /// <summary>Actor id recorded for actions taken by the system (escalations, auto-approval).</summary>
    public static readonly Guid SystemActor = Guid.Empty;

    private readonly EssDbContext _db;
    private readonly IClock _clock;
    private readonly INotificationService _notify;
    private readonly ICurrentUser _user;
    private readonly IEnumerable<IWorkflowDocumentHandler> _handlers;
    private readonly ILogger<WorkflowEngine> _log;
    private readonly string _fallbackRole;

    public WorkflowEngine(EssDbContext db, IClock clock, INotificationService notify, ICurrentUser user,
        IEnumerable<IWorkflowDocumentHandler> handlers, IConfiguration config, ILogger<WorkflowEngine> log)
    {
        _db = db;
        _clock = clock;
        _notify = notify;
        _user = user;
        _handlers = handlers;
        _log = log;
        _fallbackRole = config["Workflow:FallbackApproverRole"] ?? SystemRoles.HrManager;
    }

    // ---------------------------------------------------------------- start

    public async Task<WorkflowStartResult> StartAsync(WorkflowStartRequest r, CancellationToken ct)
    {
        var def = await ResolveDefinitionAsync(r.CompanyId, r.DocumentType, ct);
        if (def.Owner == WorkflowOwner.Nav) return new WorkflowStartResult(null, WorkflowOutcome.NavOwned);

        var previous = await _db.WorkflowInstances.Where(i => i.DocumentType == r.DocumentType && i.DocumentId == r.DocumentId)
            .Select(i => i.Status).ToListAsync(ct);
        if (previous.Contains(WorkflowInstanceStatus.Running))
            throw new WorkflowException("This request is already waiting for approval.");

        var instance = new WorkflowInstance
        {
            CompanyId = r.CompanyId,
            WorkflowDefinitionId = def.Id,
            DefinitionVersion = def.Version,
            DocumentType = r.DocumentType,
            DocumentId = r.DocumentId,
            DocumentReference = r.DocumentReference,
            Summary = Truncate(r.Summary, 1000),
            LinkUrl = r.LinkUrl,
            RequesterUserId = r.RequesterUserId,
            RequesterEmployeeId = r.RequesterEmployeeId,
            Amount = r.Amount,
            Status = WorkflowInstanceStatus.Running
        };
        _db.WorkflowInstances.Add(instance);
        History(instance, previous.Contains(WorkflowInstanceStatus.Returned) ? ApprovalAction.Resubmitted : ApprovalAction.Submitted, r.RequesterUserId);

        // A default definition created just now is not saved yet, so use its in-memory steps.
        var steps = _db.Entry(def).State == EntityState.Added
            ? def.Steps.ToList()
            : await _db.WorkflowSteps.AsNoTracking().Where(s => s.WorkflowDefinitionId == def.Id).ToListAsync(ct);
        await AdvanceAsync(instance, steps, ct);
        await SaveAsync(ct);
        return new WorkflowStartResult(instance.Id, instance.Status == WorkflowInstanceStatus.Approved ? WorkflowOutcome.Approved : WorkflowOutcome.Pending);
    }

    private async Task<WorkflowDefinition> ResolveDefinitionAsync(Guid companyId, string documentType, CancellationToken ct)
    {
        var candidates = new[] { documentType, documentType.Split(':')[0] }.Distinct().ToArray();
        var defs = await _db.ApprovalWorkflows.Where(d => d.CompanyId == companyId && d.IsActive && candidates.Contains(d.DocumentType)).ToListAsync(ct);
        var def = defs.FirstOrDefault(d => d.DocumentType == documentType) ?? defs.FirstOrDefault();
        if (def is not null) return def;

        // First use for this company: create the default route so HR can see and edit it.
        def = WorkflowDefaults.Create(companyId, candidates[^1]);
        _db.ApprovalWorkflows.Add(def);
        _log.LogInformation("Created default approval workflow {Code} for company {Company}", def.Code, companyId);
        return def;
    }

    // ---------------------------------------------------------------- routing

    /// <summary>Moves to the next sequence that has approvers, creating its tasks; approves the instance when none is left.</summary>
    private async Task AdvanceAsync(WorkflowInstance instance, List<WorkflowStep> steps, CancellationToken ct)
    {
        var anyTasksEver = instance.CurrentSequence > 0;
        while (true)
        {
            var applicable = steps.Where(s => s.Sequence > instance.CurrentSequence && AppliesTo(s, instance.Amount)).ToList();
            if (applicable.Count == 0)
            {
                if (!anyTasksEver) History(instance, ApprovalAction.AutoApproved, SystemActor, "No approval step applies to this request.");
                await CompleteAsync(instance, WorkflowInstanceStatus.Approved, SystemActor, null, ct);
                return;
            }

            var seq = applicable.Min(s => s.Sequence);
            instance.CurrentSequence = seq;
            var created = 0;
            foreach (var step in applicable.Where(s => s.Sequence == seq))
            {
                var hasLaterStep = steps.Any(s => s.Sequence > seq && AppliesTo(s, instance.Amount));
                var assignees = await ResolveApproversAsync(instance, step, hasLaterStep, ct);
                if (assignees is null)
                {
                    History(instance, ApprovalAction.AutoApproved, SystemActor, $"Step \"{step.Name}\" skipped: the requester is its only approver.");
                    continue;
                }
                foreach (var a in assignees)
                {
                    await CreateTaskAsync(instance, step, a.UserId, a.OnBehalfOf, step.DeadlineHours, null, ct);
                    created++;
                }
            }
            if (created > 0) return;
        }
    }

    private static bool AppliesTo(WorkflowStep s, decimal? amount) =>
        amount is null
            ? s.MinAmount is null
            : (s.MinAmount is null || amount >= s.MinAmount) && (s.MaxAmount is null || amount <= s.MaxAmount);

    private sealed record Assignee(Guid UserId, Guid? OnBehalfOf);

    /// <summary>
    /// Approvers for a step, after delegation. Never includes the requester. Returns null when the step should be skipped
    /// because the requester was its only approver, the step allows that and a later step will still approve the request
    /// (a skip never ends in auto-approval). Otherwise falls back to the escalation role, then the fallback approver role,
    /// and fails if nobody else can approve.
    /// </summary>
    private async Task<List<Assignee>?> ResolveApproversAsync(WorkflowInstance instance, WorkflowStep step, bool hasLaterStep, CancellationToken ct)
    {
        var candidates = await CandidatesAsync(instance, step.ApproverType, step.ApproverRole, step.ApproverUserId, ct);
        var others = candidates.Where(u => u != instance.RequesterUserId).Distinct().ToList();
        if (others.Count == 0 && candidates.Contains(instance.RequesterUserId) && step.SkipIfApproverIsRequester && hasLaterStep) return null;

        if (others.Count == 0 && step.EscalateToRole is not null)
            others = (await RoleUsersAsync(instance, step.EscalateToRole, ct)).Where(u => u != instance.RequesterUserId).ToList();
        if (others.Count == 0)
            others = (await RoleUsersAsync(instance, _fallbackRole, ct)).Where(u => u != instance.RequesterUserId).ToList();
        if (others.Count == 0)
            throw new WorkflowException($"Nobody can approve the step \"{step.Name}\". Ask HR to set your line manager in NAV or to assign the approver role.");

        var result = new List<Assignee>();
        foreach (var u in others)
        {
            var delegateTo = await ActiveDelegateAsync(instance, u, ct);
            result.Add(delegateTo is not null && delegateTo != instance.RequesterUserId ? new Assignee(delegateTo.Value, u) : new Assignee(u, null));
        }
        return result.DistinctBy(a => a.UserId).ToList();
    }

    private async Task<List<Guid>> CandidatesAsync(WorkflowInstance instance, ApproverType type, string? role, Guid? userId, CancellationToken ct)
    {
        switch (type)
        {
            case ApproverType.SpecificUser:
                return userId is null ? new() : await _db.Users.Where(u => u.Id == userId && u.IsActive).Select(u => u.Id).ToListAsync(ct);
            case ApproverType.Role:
                return role is null ? new() : await RoleUsersAsync(instance, role, ct);
            case ApproverType.LineManager:
            {
                var mgr = await ManagerOfAsync(instance.RequesterEmployeeId, ct);
                return mgr is null ? new() : await UsersForEmployeeAsync(instance.CompanyId, mgr.Value, ct);
            }
            case ApproverType.ManagerOfManager:
            {
                var mgr = await ManagerOfAsync(instance.RequesterEmployeeId, ct);
                var mgr2 = mgr is null ? null : await ManagerOfAsync(mgr.Value, ct);
                return mgr2 is null ? new() : await UsersForEmployeeAsync(instance.CompanyId, mgr2.Value, ct);
            }
            case ApproverType.DepartmentHead:
            {
                var head = await DepartmentHeadAsync(instance.RequesterEmployeeId, ct);
                return head is null ? new() : await UsersForEmployeeAsync(instance.CompanyId, head.Value, ct);
            }
            default:
                return new();
        }
    }

    /// <summary>The employee's line manager from NAV; when none is set, the head of their department.</summary>
    private async Task<Guid?> ManagerOfAsync(Guid employeeId, CancellationToken ct)
    {
        var mgr = await _db.Employees.Where(e => e.Id == employeeId).Select(e => e.ManagerEmployeeId).FirstOrDefaultAsync(ct);
        return mgr is not null && mgr != employeeId ? mgr : await DepartmentHeadAsync(employeeId, ct);
    }

    /// <summary>Head of the employee's department, walking up the department tree if the employee heads it.</summary>
    private async Task<Guid?> DepartmentHeadAsync(Guid employeeId, CancellationToken ct)
    {
        var deptId = await _db.Employees.Where(e => e.Id == employeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        for (var depth = 0; deptId is not null && depth < 10; depth++)
        {
            var d = await _db.Departments.Where(x => x.Id == deptId).Select(x => new { x.HeadEmployeeId, x.ParentDepartmentId }).FirstOrDefaultAsync(ct);
            if (d is null) return null;
            if (d.HeadEmployeeId is not null && d.HeadEmployeeId != employeeId) return d.HeadEmployeeId;
            deptId = d.ParentDepartmentId;
        }
        return null;
    }

    private Task<List<Guid>> UsersForEmployeeAsync(Guid companyId, Guid employeeId, CancellationToken ct) =>
        (from m in _db.EmployeeMappings
         join u in _db.Users on m.UserId equals u.Id
         where m.CompanyId == companyId && m.EmployeeId == employeeId && u.IsActive
         select u.Id).Distinct().ToListAsync(ct);

    /// <summary>Active users holding the role for this company, honouring department scope (a scoped grant covers its sub-departments).</summary>
    private async Task<List<Guid>> RoleUsersAsync(WorkflowInstance instance, string roleName, CancellationToken ct)
    {
        var normalized = roleName.ToUpperInvariant();
        var grants = await (from ur in _db.UserRoles
                            join u in _db.Users on ur.UserId equals u.Id
                            where ur.Role!.NormalizedName == normalized && u.IsActive && (ur.CompanyId == null || ur.CompanyId == instance.CompanyId)
                            select new { ur.UserId, ur.ScopeDepartmentId }).ToListAsync(ct);
        if (grants.Count == 0) return new();

        var chain = new HashSet<Guid>();
        var deptId = await _db.Employees.Where(e => e.Id == instance.RequesterEmployeeId).Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        for (var depth = 0; deptId is not null && depth < 10 && chain.Add(deptId.Value); depth++)
            deptId = await _db.Departments.Where(x => x.Id == deptId).Select(x => x.ParentDepartmentId).FirstOrDefaultAsync(ct);

        return grants.Where(g => g.ScopeDepartmentId is null || chain.Contains(g.ScopeDepartmentId.Value)).Select(g => g.UserId).Distinct().ToList();
    }

    private async Task<Guid?> ActiveDelegateAsync(WorkflowInstance instance, Guid userId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var baseType = instance.DocumentType.Split(':')[0];
        var d = await _db.ApprovalDelegations.AsNoTracking()
            .Where(x => x.CompanyId == instance.CompanyId && x.FromUserId == userId && x.StartsAtUtc <= now && x.EndsAtUtc > now
                        && (x.DocumentType == null || x.DocumentType == instance.DocumentType || x.DocumentType == baseType))
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (d is null) return null;
        var active = await _db.Users.AnyAsync(u => u.Id == d.ToUserId && u.IsActive, ct);
        return active ? d.ToUserId : null;
    }

    private async Task<ApprovalTask> CreateTaskAsync(WorkflowInstance instance, WorkflowStep step, Guid assignee, Guid? onBehalfOf,
        int? deadlineHours, Guid? escalatedFrom, CancellationToken ct)
    {
        var task = new ApprovalTask
        {
            WorkflowInstanceId = instance.Id,
            WorkflowStepId = step.Id,
            Sequence = step.Sequence,
            AssigneeUserId = assignee,
            OnBehalfOfUserId = onBehalfOf,
            Status = ApprovalTaskStatus.Pending,
            DueAtUtc = deadlineHours is > 0 ? _clock.UtcNow.AddHours(deadlineHours.Value) : null,
            EscalatedFromTaskId = escalatedFrom
        };
        _db.ApprovalTasks.Add(task);
        await _notify.QueueAsync(new NotificationRequest(instance.CompanyId, assignee,
            escalatedFrom is null ? NotificationEvents.ApprovalRequired : NotificationEvents.ApprovalEscalated,
            $"Approval needed: {instance.DocumentReference}",
            $"{instance.Summary ?? instance.DocumentReference} is waiting for your approval{(onBehalfOf is null ? "" : " (as a delegate)")}.",
            "/approvals"), ct);
        return task;
    }

    // ---------------------------------------------------------------- actions

    public async Task ActAsync(Guid taskId, Guid actorUserId, ApprovalDecision decision, string? comment, CancellationToken ct)
    {
        comment = string.IsNullOrWhiteSpace(comment) ? null : Truncate(comment.Trim(), 1000);
        if (decision != ApprovalDecision.Approve && comment is null)
            throw new WorkflowException(decision == ApprovalDecision.Reject ? "Give a reason for rejecting." : "Say what needs correcting.");

        var task = await _db.ApprovalTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw new KeyNotFoundException("Approval not found.");
        if (task.AssigneeUserId != actorUserId) throw new UnauthorizedAccessException("This approval is assigned to someone else.");
        if (task.Status != ApprovalTaskStatus.Pending) throw new WorkflowException("This approval has already been dealt with.");

        var instance = await _db.WorkflowInstances.FirstAsync(i => i.Id == task.WorkflowInstanceId, ct);
        if (instance.Status != WorkflowInstanceStatus.Running) throw new WorkflowException("This request is no longer waiting for approval.");
        if (instance.RequesterUserId == actorUserId) throw new UnauthorizedAccessException("You cannot approve your own request.");

        var now = _clock.UtcNow;
        var pending = await _db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id && t.Status == ApprovalTaskStatus.Pending).ToListAsync(ct);
        task.ActedAtUtc = now;
        task.Comment = comment;
        // Touch the instance so two approvers acting at once conflict on its row version instead of both advancing it.
        _db.Entry(instance).Property(x => x.CurrentSequence).IsModified = true;

        switch (decision)
        {
            case ApprovalDecision.Approve:
            {
                task.Status = ApprovalTaskStatus.Approved;
                History(instance, ApprovalAction.Approved, actorUserId, comment, task);
                var step = await _db.WorkflowSteps.AsNoTracking().FirstAsync(s => s.Id == task.WorkflowStepId, ct);
                var origin = task.EscalatedFromTaskId ?? task.Id;
                foreach (var t in pending.Where(t => t.Id != task.Id && t.WorkflowStepId == step.Id
                             && (step.Mode == StepMode.AnyOne || t.Id == origin || t.EscalatedFromTaskId == origin)))
                {
                    t.Status = ApprovalTaskStatus.Superseded;
                    t.ActedAtUtc = now;
                }
                if (!pending.Any(t => t.Status == ApprovalTaskStatus.Pending && t.Sequence == instance.CurrentSequence))
                {
                    var steps = await _db.WorkflowSteps.AsNoTracking().Where(s => s.WorkflowDefinitionId == instance.WorkflowDefinitionId).ToListAsync(ct);
                    await AdvanceAsync(instance, steps, ct);
                }
                break;
            }
            case ApprovalDecision.Reject:
            case ApprovalDecision.Return:
            {
                var rejected = decision == ApprovalDecision.Reject;
                task.Status = rejected ? ApprovalTaskStatus.Rejected : ApprovalTaskStatus.Returned;
                History(instance, rejected ? ApprovalAction.Rejected : ApprovalAction.Returned, actorUserId, comment, task);
                foreach (var t in pending.Where(t => t.Id != task.Id))
                {
                    t.Status = ApprovalTaskStatus.Superseded;
                    t.ActedAtUtc = now;
                }
                await CompleteAsync(instance, rejected ? WorkflowInstanceStatus.Rejected : WorkflowInstanceStatus.Returned, actorUserId, comment, ct);
                break;
            }
        }
        await SaveAsync(ct);
    }

    public async Task CancelAsync(Guid instanceId, Guid actorUserId, string? reason, CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct) ?? throw new KeyNotFoundException("Approval not found.");
        if (instance.Status != WorkflowInstanceStatus.Running) return;
        var now = _clock.UtcNow;
        foreach (var t in await _db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instanceId && t.Status == ApprovalTaskStatus.Pending).ToListAsync(ct))
        {
            t.Status = ApprovalTaskStatus.Superseded;
            t.ActedAtUtc = now;
        }
        instance.Status = WorkflowInstanceStatus.Cancelled;
        instance.CompletedAtUtc = now;
        History(instance, ApprovalAction.Cancelled, actorUserId, reason);
        // The document service that cancels updates its own record; no handler callback, to avoid loops.
        await SaveAsync(ct);
    }

    private async Task CompleteAsync(WorkflowInstance instance, WorkflowInstanceStatus status, Guid actor, string? comment, CancellationToken ct)
    {
        instance.Status = status;
        instance.CompletedAtUtc = _clock.UtcNow;

        var handler = _handlers.FirstOrDefault(h => h.DocumentType == instance.DocumentType)
                      ?? _handlers.FirstOrDefault(h => h.DocumentType == instance.DocumentType.Split(':')[0])
                      ?? throw new InvalidOperationException($"No workflow handler is registered for {instance.DocumentType}.");
        await handler.OnCompletedAsync(new WorkflowCompletion(instance.Id, instance.CompanyId, instance.DocumentType, instance.DocumentId, status, actor, comment), ct);

        var (evt, verb) = status switch
        {
            WorkflowInstanceStatus.Approved => (NotificationEvents.RequestApproved, "was approved"),
            WorkflowInstanceStatus.Rejected => (NotificationEvents.RequestRejected, "was rejected"),
            _ => (NotificationEvents.RequestReturned, "was returned to you for correction")
        };
        var body = $"{instance.Summary ?? instance.DocumentReference} {verb}." + (comment is null ? "" : $" Comment: {comment}");
        await _notify.QueueAsync(new NotificationRequest(instance.CompanyId, instance.RequesterUserId, evt, $"{instance.DocumentReference} {verb}", body, instance.LinkUrl), ct);
    }

    // ---------------------------------------------------------------- deadlines

    public async Task<int> ProcessDeadlinesAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var actions = 0;

        var overdue = await (from t in _db.ApprovalTasks
                             join i in _db.WorkflowInstances on t.WorkflowInstanceId equals i.Id
                             join s in _db.WorkflowSteps on t.WorkflowStepId equals s.Id
                             where t.Status == ApprovalTaskStatus.Pending && i.Status == WorkflowInstanceStatus.Running
                                   && t.DueAtUtc != null && t.DueAtUtc <= now && t.EscalatedAtUtc == null && t.EscalatedFromTaskId == null
                             select new { Task = t, Instance = i, Step = s }).Take(200).ToListAsync(ct);
        foreach (var o in overdue)
        {
            o.Task.EscalatedAtUtc = now;
            if (o.Step.EscalateToRole is null) continue;
            var existing = await _db.ApprovalTasks.Where(t => t.WorkflowInstanceId == o.Instance.Id && t.Status == ApprovalTaskStatus.Pending)
                .Select(t => t.AssigneeUserId).ToListAsync(ct);
            var targets = (await RoleUsersAsync(o.Instance, o.Step.EscalateToRole, ct))
                .Where(u => u != o.Instance.RequesterUserId && !existing.Contains(u)).ToList();
            foreach (var u in targets)
                await CreateTaskAsync(o.Instance, o.Step, u, null, null, o.Task.Id, ct);
            History(o.Instance, ApprovalAction.Escalated, SystemActor,
                targets.Count > 0 ? $"Deadline passed; escalated to {o.Step.EscalateToRole}." : $"Deadline passed; nobody holds {o.Step.EscalateToRole} to escalate to.", o.Task);
            actions++;
        }

        var reminders = await (from t in _db.ApprovalTasks
                               join i in _db.WorkflowInstances on t.WorkflowInstanceId equals i.Id
                               join s in _db.WorkflowSteps on t.WorkflowStepId equals s.Id
                               where t.Status == ApprovalTaskStatus.Pending && i.Status == WorkflowInstanceStatus.Running && s.ReminderEveryHours != null
                               select new { Task = t, Instance = i, s.ReminderEveryHours }).Take(500).ToListAsync(ct);
        foreach (var r in reminders.Where(r => (r.Task.LastReminderAtUtc ?? r.Task.CreatedAtUtc).AddHours(r.ReminderEveryHours!.Value) <= now))
        {
            r.Task.LastReminderAtUtc = now;
            await _notify.QueueAsync(new NotificationRequest(r.Instance.CompanyId, r.Task.AssigneeUserId, NotificationEvents.ApprovalReminder,
                $"Reminder: {r.Instance.DocumentReference} is waiting for you", $"{r.Instance.Summary ?? r.Instance.DocumentReference} is still waiting for your approval.", "/approvals"), ct);
            actions++;
        }

        await _db.SaveChangesAsync(ct);
        return actions;
    }

    // ---------------------------------------------------------------- helpers

    private void History(WorkflowInstance instance, ApprovalAction action, Guid actor, string? comment = null, ApprovalTask? task = null) =>
        _db.ApprovalHistory.Add(new ApprovalHistory
        {
            WorkflowInstanceId = instance.Id,
            ApprovalTaskId = task?.Id,
            Action = action,
            ActorUserId = actor,
            OnBehalfOfUserId = task?.OnBehalfOfUserId,
            Comment = comment,
            OccurredAtUtc = _clock.UtcNow,
            IpAddress = actor == SystemActor ? null : _user.IpAddress
        });

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new WorkflowException("Someone else acted on this request at the same time. Refresh and try again.");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
