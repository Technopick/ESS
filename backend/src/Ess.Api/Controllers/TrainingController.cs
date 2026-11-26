using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Application.Workflow;
using Ess.Domain.Training;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers;

/// <summary>Training catalogue, applications with approval and capacity limits, attendance and feedback (spec §6).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/training")]
[Route("api/training")]
public sealed class TrainingController : ControllerBase
{
    private static readonly TrainingParticipationStatus[] Holding = { TrainingParticipationStatus.Applied, TrainingParticipationStatus.Approved, TrainingParticipationStatus.Registered, TrainingParticipationStatus.Attended, TrainingParticipationStatus.Completed };

    private readonly EssDbContext _db;
    private readonly EmployeeContext _me;
    private readonly ICurrentUser _user;

    public TrainingController(EssDbContext db, EmployeeContext me, ICurrentUser user)
    {
        _db = db;
        _me = me;
        _user = user;
    }

    private Guid CompanyId => _user.CompanyId ?? throw new UnauthorizedAccessException("Your account has no company.");

    public sealed record ProgramView(Guid Id, string Code, string Title, string? Description, string? Provider, DateTime? StartsAtUtc, DateTime? EndsAtUtc, string? Venue,
        int? Capacity, int Taken, decimal? CostPerParticipant, bool RequiresApproval, bool IsPublished, TrainingParticipationStatus? MyStatus);
    public sealed record ProgramInput(string Code, string Title, string? Description, string? Provider, DateTime? StartsAtUtc, DateTime? EndsAtUtc, string? Venue,
        int? Capacity, decimal? CostPerParticipant, bool RequiresApproval, bool IsPublished);
    public sealed record ParticipationView(Guid Id, string Reference, Guid ProgramId, string Program, DateTime? StartsAtUtc, DateTime? EndsAtUtc,
        TrainingParticipationStatus Status, int? FeedbackRating, string? FeedbackComment, string? EmployeeName, Guid? WorkflowInstanceId);

    [HttpGet("programs")]
    [HasPermission(Permissions.TrainingApply)]
    public async Task<IReadOnlyList<ProgramView>> Programs([FromQuery] bool includePast = false, CancellationToken ct = default)
    {
        var me = await _me.GetAsync(ct);
        var manage = _user.HasPermission(Permissions.TrainingManage);
        var now = DateTime.UtcNow;
        return await _db.TrainingPrograms.AsNoTracking()
            .Where(p => p.CompanyId == CompanyId && (p.IsPublished || manage) && (includePast || p.EndsAtUtc == null || p.EndsAtUtc >= now))
            .OrderBy(p => p.StartsAtUtc)
            .Select(p => new ProgramView(p.Id, p.Code, p.Title, p.Description, p.Provider, p.StartsAtUtc, p.EndsAtUtc, p.Venue, p.Capacity,
                _db.EmployeeTraining.Count(t => t.TrainingProgramId == p.Id && Holding.Contains(t.Status)), p.CostPerParticipant, p.RequiresApproval, p.IsPublished,
                me == null ? null : _db.EmployeeTraining.Where(t => t.TrainingProgramId == p.Id && t.EmployeeId == me.Id).OrderByDescending(t => t.CreatedAtUtc).Select(t => (TrainingParticipationStatus?)t.Status).FirstOrDefault()))
            .ToListAsync(ct);
    }

    [HttpPost("programs")]
    [HasPermission(Permissions.TrainingManage)]
    public async Task<IActionResult> CreateProgram(ProgramInput input, CancellationToken ct)
    {
        var error = Check(input);
        if (error is not null) return Field("program", error);
        if (await _db.TrainingPrograms.AnyAsync(p => p.CompanyId == CompanyId && p.Code == input.Code.Trim(), ct)) return Field("code", "That code is already used.");
        var p = new TrainingProgram { CompanyId = CompanyId, Code = input.Code.Trim(), Title = input.Title.Trim() };
        Apply(p, input);
        _db.TrainingPrograms.Add(p);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/training/programs/{p.Id}", new { p.Id });
    }

    [HttpPut("programs/{id:guid}")]
    [HasPermission(Permissions.TrainingManage)]
    public async Task<IActionResult> UpdateProgram(Guid id, ProgramInput input, CancellationToken ct)
    {
        var p = await _db.TrainingPrograms.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (p is null) return NotFound();
        var error = Check(input);
        if (error is not null) return Field("program", error);
        Apply(p, input);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("programs/{id:guid}/apply")]
    [HasPermission(Permissions.TrainingApply)]
    public async Task<IActionResult> ApplyFor(Guid id, [FromServices] IReferenceGenerator refs, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var p = await _db.TrainingPrograms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId && x.IsPublished, ct);
        if (p is null) return NotFound();
        if (p.StartsAtUtc is not null && p.StartsAtUtc < DateTime.UtcNow) return Problem(statusCode: 409, title: "Started", detail: "This programme has already started.");
        if (await _db.EmployeeTraining.AnyAsync(t => t.TrainingProgramId == id && t.EmployeeId == me.Id && Holding.Contains(t.Status), ct))
            return Problem(statusCode: 409, title: "Already applied", detail: "You have already applied for this programme.");
        if (p.Capacity is { } cap && await _db.EmployeeTraining.CountAsync(t => t.TrainingProgramId == id && Holding.Contains(t.Status), ct) >= cap)
            return Problem(statusCode: 409, title: "Full", detail: "This programme is full.");

        var t = new EmployeeTraining
        {
            CompanyId = CompanyId, Reference = await refs.NextAsync(CompanyId, "TRN", ct), EmployeeId = me.Id, TrainingProgramId = id,
            Status = p.RequiresApproval ? TrainingParticipationStatus.Applied : TrainingParticipationStatus.Registered
        };
        _db.EmployeeTraining.Add(t);
        if (p.RequiresApproval)
        {
            var result = await workflow.StartAsync(new WorkflowStartRequest(CompanyId, DocumentTypes.Training, t.Id, t.Reference, _user.UserId!.Value, me.Id,
                p.CostPerParticipant, $"{me.FullName}: {p.Title}", "/training"), ct);
            t.WorkflowInstanceId = result.InstanceId;
        }
        await _db.SaveChangesAsync(ct);
        return Created($"/api/training/mine", new { t.Id, t.Reference, t.Status });
    }

    [HttpGet("mine")]
    [HasPermission(Permissions.TrainingApply)]
    public async Task<IReadOnlyList<ParticipationView>> Mine(CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        return (await Views(_db.EmployeeTraining.Where(t => t.EmployeeId == me.Id)).ToListAsync(ct)).OrderByDescending(v => v.StartsAtUtc).ToList();
    }

    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(Permissions.TrainingApply)]
    public async Task<IActionResult> Withdraw(Guid id, [FromServices] IWorkflowEngine workflow, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var t = await _db.EmployeeTraining.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct);
        if (t is null) return NotFound();
        if (t.Status is not (TrainingParticipationStatus.Applied or TrainingParticipationStatus.Approved or TrainingParticipationStatus.Registered))
            return Problem(statusCode: 409, title: "Cannot withdraw", detail: "This application can no longer be withdrawn.");
        if (t.Status == TrainingParticipationStatus.Applied && t.WorkflowInstanceId is not null) await workflow.CancelAsync(t.WorkflowInstanceId.Value, _user.UserId!.Value, "Withdrawn", ct);
        t.Status = TrainingParticipationStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record FeedbackInput(int Rating, string? Comment);

    [HttpPost("{id:guid}/feedback")]
    [HasPermission(Permissions.TrainingApply)]
    public async Task<IActionResult> Feedback(Guid id, FeedbackInput input, CancellationToken ct)
    {
        var me = await _me.RequireAsync(ct);
        var t = await _db.EmployeeTraining.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct);
        if (t is null) return NotFound();
        if (t.Status is not (TrainingParticipationStatus.Attended or TrainingParticipationStatus.Completed))
            return Problem(statusCode: 409, title: "Not yet", detail: "You can give feedback after attending.");
        if (input.Rating is < 1 or > 5) return Field("rating", "Rate from 1 to 5.");
        t.FeedbackRating = input.Rating;
        t.FeedbackComment = input.Comment?.Trim() is { Length: > 0 } c ? c[..Math.Min(c.Length, 1000)] : null;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>HR: participants of a programme.</summary>
    [HttpGet("programs/{id:guid}/participants")]
    [HasPermission(Permissions.TrainingManage)]
    public async Task<IReadOnlyList<ParticipationView>> Participants(Guid id, CancellationToken ct) =>
        (await Views(_db.EmployeeTraining.Where(t => t.TrainingProgramId == id && t.CompanyId == CompanyId)).ToListAsync(ct)).OrderBy(v => v.EmployeeName).ToList();

    public sealed record StatusInput(TrainingParticipationStatus Status);

    /// <summary>HR records attendance or completion after the programme.</summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.TrainingManage)]
    public async Task<IActionResult> SetStatus(Guid id, StatusInput input, CancellationToken ct)
    {
        if (input.Status is not (TrainingParticipationStatus.Registered or TrainingParticipationStatus.Attended or TrainingParticipationStatus.Completed or TrainingParticipationStatus.NoShow))
            return Field("status", "Use Registered, Attended, Completed or NoShow.");
        var t = await _db.EmployeeTraining.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId, ct);
        if (t is null) return NotFound();
        if (t.Status is TrainingParticipationStatus.Applied or TrainingParticipationStatus.Rejected or TrainingParticipationStatus.Cancelled)
            return Problem(statusCode: 409, title: "Not approved", detail: "Only approved participants can be marked.");
        t.Status = input.Status;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private IQueryable<ParticipationView> Views(IQueryable<EmployeeTraining> q) =>
        from t in q.AsNoTracking()
        join p in _db.TrainingPrograms on t.TrainingProgramId equals p.Id
        join e in _db.Employees on t.EmployeeId equals e.Id
        select new ParticipationView(t.Id, t.Reference, p.Id, p.Title, p.StartsAtUtc, p.EndsAtUtc, t.Status, t.FeedbackRating, t.FeedbackComment,
            e.FirstName + " " + e.LastName, t.WorkflowInstanceId);

    private static string? Check(ProgramInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Code) || i.Code.Length > 30) return "Enter a code of up to 30 characters.";
        if (string.IsNullOrWhiteSpace(i.Title) || i.Title.Length > 200) return "Enter a title of up to 200 characters.";
        if (i.StartsAtUtc is not null && i.EndsAtUtc is not null && i.EndsAtUtc < i.StartsAtUtc) return "The end must be after the start.";
        if (i.Capacity is < 1) return "Capacity must be at least 1.";
        if (i.CostPerParticipant is < 0) return "Cost cannot be negative.";
        return null;
    }

    private static void Apply(TrainingProgram p, ProgramInput i)
    {
        p.Title = i.Title.Trim();
        p.Description = i.Description?.Trim();
        p.Provider = i.Provider?.Trim();
        p.StartsAtUtc = i.StartsAtUtc;
        p.EndsAtUtc = i.EndsAtUtc;
        p.Venue = i.Venue?.Trim();
        p.Capacity = i.Capacity;
        p.CostPerParticipant = i.CostPerParticipant;
        p.RequiresApproval = i.RequiresApproval;
        p.IsPublished = i.IsPublished;
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
