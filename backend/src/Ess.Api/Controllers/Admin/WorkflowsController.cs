using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Application.Workflow;
using Ess.Domain.Identity;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Ess.Infrastructure.Workflow;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>
/// Approval routes per document type (spec §5). Saving creates a new version; approvals already running keep the
/// version they started with. Owner = Nav hands approval to NAV's own workflow for that type (no duplicate approvals).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/workflows")]
[Route("api/admin/workflows")]
[HasPermission(Permissions.WorkflowConfigure)]
public sealed class WorkflowsController : ControllerBase
{
    private static readonly string[] KnownTypes = { DocumentTypes.Leave, DocumentTypes.Expense, DocumentTypes.ProfileChange, DocumentTypes.HrRequest, DocumentTypes.Attendance, DocumentTypes.Training };

    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public WorkflowsController(EssDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    public sealed record StepDto(int Sequence, string Name, ApproverType ApproverType, string? ApproverRole, Guid? ApproverUserId, StepMode Mode,
        decimal? MinAmount, decimal? MaxAmount, int? DeadlineHours, int? ReminderEveryHours, string? EscalateToRole, bool SkipIfApproverIsRequester);

    public sealed record WorkflowDto(Guid? Id, string DocumentType, string Name, WorkflowOwner Owner, int Version, bool IsDefault, int RunningCount, IReadOnlyList<StepDto> Steps);

    public sealed record WorkflowInput(string Name, WorkflowOwner Owner, IReadOnlyList<StepDto> Steps);

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException("Choose a company first.");

    /// <summary>The route for each document type, including the built-in default for types not configured yet.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<WorkflowDto>> List(CancellationToken ct)
    {
        var defs = await _db.ApprovalWorkflows.AsNoTracking().Include(d => d.Steps).Where(d => d.CompanyId == CompanyId && d.IsActive).ToListAsync(ct);
        var running = await _db.WorkflowInstances.Where(i => i.CompanyId == CompanyId && i.Status == WorkflowInstanceStatus.Running)
            .GroupBy(i => i.DocumentType).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var result = defs.Select(d => ToDto(d, false, running.Where(r => r.Key == d.DocumentType || r.Key.StartsWith(d.DocumentType + ":")).Sum(r => r.Value))).ToList();
        foreach (var t in KnownTypes.Where(t => defs.All(d => d.DocumentType != t)))
            result.Add(ToDto(WorkflowDefaults.Create(CompanyId, t), true, 0) with { Id = null });
        return result.OrderBy(d => d.DocumentType).ToList();
    }

    [HttpPut("{documentType}")]
    public async Task<IActionResult> Save(string documentType, WorkflowInput input, CancellationToken ct)
    {
        var baseType = documentType.Split(':')[0];
        if (!KnownTypes.Contains(baseType)) return Field("documentType", "Unknown request type.");
        if (string.IsNullOrWhiteSpace(input.Name)) return Field("name", "Enter a name.");
        if (input.Owner == WorkflowOwner.Nav && baseType != DocumentTypes.Leave && baseType != DocumentTypes.Expense)
            return Field("owner", "Only leave and expense claims can use NAV's own approval workflow.");
        if (input.Owner == WorkflowOwner.Portal && input.Steps.Count == 0) return Field("steps", "Add at least one approval step, or every request will be approved automatically.");
        if (input.Steps.Count > 10) return Field("steps", "Use at most 10 steps.");

        var roles = await _db.Roles.Select(r => r.Name).ToListAsync(ct);
        for (var i = 0; i < input.Steps.Count; i++)
        {
            var s = input.Steps[i];
            var key = $"steps[{i}]";
            if (string.IsNullOrWhiteSpace(s.Name)) return Field(key, "Each step needs a name.");
            if (s.Sequence is < 1 or > 20) return Field(key, "Step order must be between 1 and 20.");
            if (s.ApproverType == ApproverType.Role && (s.ApproverRole is null || !roles.Contains(s.ApproverRole))) return Field(key, $"Choose a role for \"{s.Name}\".");
            if (s.ApproverType == ApproverType.SpecificUser && (s.ApproverUserId is null || !await _db.Users.AnyAsync(u => u.Id == s.ApproverUserId && u.IsActive, ct)))
                return Field(key, $"Choose an active user for \"{s.Name}\".");
            if (s.EscalateToRole is not null && !roles.Contains(s.EscalateToRole)) return Field(key, $"Unknown escalation role for \"{s.Name}\".");
            if (s.MinAmount is not null && s.MaxAmount is not null && s.MinAmount > s.MaxAmount) return Field(key, $"Minimum amount is above maximum for \"{s.Name}\".");
            if (s.DeadlineHours is < 1 or > 720 || s.ReminderEveryHours is < 1 or > 720) return Field(key, "Deadlines and reminders must be between 1 and 720 hours.");
        }

        var current = await _db.ApprovalWorkflows.FirstOrDefaultAsync(d => d.CompanyId == CompanyId && d.DocumentType == documentType && d.IsActive, ct);
        var def = new WorkflowDefinition
        {
            CompanyId = CompanyId,
            Code = current?.Code ?? documentType.ToUpperInvariant().Replace(':', '-'),
            Name = input.Name.Trim(),
            DocumentType = documentType,
            Owner = input.Owner,
            Version = (current?.Version ?? 0) + 1
        };
        if (current is not null)
        {
            // Retire the old version without deleting its steps: running approvals still use them.
            current.IsActive = false;
            current.IsDeleted = true;
            current.DeletedAtUtc = DateTime.UtcNow;
            current.DeletedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
        }
        foreach (var s in input.Steps)
            def.Steps.Add(new WorkflowStep
            {
                WorkflowDefinitionId = def.Id, Sequence = s.Sequence, Name = s.Name.Trim(), ApproverType = s.ApproverType,
                ApproverRole = s.ApproverType == ApproverType.Role ? s.ApproverRole : null,
                ApproverUserId = s.ApproverType == ApproverType.SpecificUser ? s.ApproverUserId : null,
                Mode = s.Mode, MinAmount = s.MinAmount, MaxAmount = s.MaxAmount, DeadlineHours = s.DeadlineHours, ReminderEveryHours = s.ReminderEveryHours,
                EscalateToRole = s.EscalateToRole, SkipIfApproverIsRequester = s.SkipIfApproverIsRequester
            });
        _db.ApprovalWorkflows.Add(def);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("workflow.saved", nameof(WorkflowDefinition), def.Id.ToString(), new { documentType, def.Version, def.Owner, steps = input.Steps }, ct: ct);
        return NoContent();
    }

    /// <summary>Role names for the step editor.</summary>
    [HttpGet("roles")]
    public async Task<IReadOnlyList<string>> Roles(CancellationToken ct) => await _db.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(ct);

    private static WorkflowDto ToDto(WorkflowDefinition d, bool isDefault, int running) =>
        new(d.Id, d.DocumentType, d.Name, d.Owner, d.Version, isDefault, running,
            d.Steps.OrderBy(s => s.Sequence).ThenBy(s => s.Name).Select(s => new StepDto(s.Sequence, s.Name, s.ApproverType, s.ApproverRole, s.ApproverUserId, s.Mode,
                s.MinAmount, s.MaxAmount, s.DeadlineHours, s.ReminderEveryHours, s.EscalateToRole, s.SkipIfApproverIsRequester)).ToList());

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
