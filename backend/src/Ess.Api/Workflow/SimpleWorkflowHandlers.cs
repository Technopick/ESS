using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Application.Workflow;
using Ess.Domain.Attendance;
using Ess.Domain.Common;
using Ess.Domain.Training;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Workflow;

/// <summary>HR requests: approval only authorises the request; HR then fulfils and resolves it.</summary>
public sealed class HrRequestWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    private readonly ISecretProtector _secrets;
    public HrRequestWorkflowHandler(EssDbContext db, ISecretProtector secrets)
    {
        _db = db;
        _secrets = secrets;
    }

    public string DocumentType => DocumentTypes.HrRequest;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var r = await _db.HrRequests.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Request not found.");
        r.Status = c.Status switch
        {
            WorkflowInstanceStatus.Approved => RequestStatus.Approved,
            WorkflowInstanceStatus.Rejected => RequestStatus.Rejected,
            _ => RequestStatus.ReturnedForCorrection
        };
        if (c.Status == WorkflowInstanceStatus.Rejected) { r.Resolution = c.Comment; r.ResolvedAtUtc = DateTime.UtcNow; }
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var r = await _db.HrRequests.AsNoTracking().FirstAsync(x => x.Id == documentId, ct);
        var type = await _db.HrRequestTypes.Where(t => t.Id == r.RequestTypeId).Select(t => t.Name).FirstAsync(ct);
        var json = r.PayloadJson is null ? "{}" : r.PayloadEncrypted ? _secrets.Unprotect(r.PayloadJson) : r.PayloadJson;
        var fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        return new[] { new DocumentDetail("Request", type), new DocumentDetail("Subject", r.Subject) }
            .Concat(fields.Select(kv => new DocumentDetail(kv.Key, kv.Value))).ToList();
    }
}

/// <summary>Approved corrections and missing punches update the attendance record; approved overtime is recorded for payroll.</summary>
public sealed class AttendanceWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    public AttendanceWorkflowHandler(EssDbContext db) => _db = db;
    public string DocumentType => DocumentTypes.Attendance;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var r = await _db.AttendanceRequests.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Request not found.");
        r.Status = c.Status switch
        {
            WorkflowInstanceStatus.Approved => RequestStatus.Approved,
            WorkflowInstanceStatus.Rejected => RequestStatus.Rejected,
            _ => RequestStatus.ReturnedForCorrection
        };
        if (c.Status != WorkflowInstanceStatus.Approved || r.Type == AttendanceRequestType.Overtime) return;

        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == r.EmployeeId && a.WorkDate == r.WorkDate, ct);
        if (rec is null)
        {
            rec = new AttendanceRecord { CompanyId = r.CompanyId, EmployeeId = r.EmployeeId, WorkDate = r.WorkDate, Source = AttendanceSource.Portal };
            _db.AttendanceRecords.Add(rec);
        }
        rec.ClockInUtc = r.RequestedClockInUtc ?? rec.ClockInUtc;
        rec.ClockOutUtc = r.RequestedClockOutUtc ?? rec.ClockOutUtc;
        rec.ExternalRef = r.Reference;
        if (rec.ClockInUtc is not null && rec.ClockOutUtc is not null)
            rec.WorkedHours = Math.Round((decimal)(rec.ClockOutUtc.Value - rec.ClockInUtc.Value).TotalHours, 2);
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var r = await _db.AttendanceRequests.AsNoTracking().FirstAsync(x => x.Id == documentId, ct);
        var rec = await _db.AttendanceRecords.AsNoTracking().FirstOrDefaultAsync(a => a.EmployeeId == r.EmployeeId && a.WorkDate == r.WorkDate, ct);
        static string? T(DateTime? utc) => utc?.AddHours(3).ToString("HH:mm");
        return new List<DocumentDetail>
        {
            new("Type", r.Type.ToString()), new("Date", r.WorkDate.ToString("ddd d MMM yyyy")),
            new("Recorded", rec is null ? "Nothing recorded" : $"{T(rec.ClockInUtc) ?? "–"} to {T(rec.ClockOutUtc) ?? "–"}"),
            new("Requested", r.Type == AttendanceRequestType.Overtime ? $"{r.OvertimeHours:0.#} hours overtime" : $"{T(r.RequestedClockInUtc) ?? "–"} to {T(r.RequestedClockOutUtc) ?? "–"}"),
            new("Reason", r.Reason),
        };
    }
}

public sealed class TrainingWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    public TrainingWorkflowHandler(EssDbContext db) => _db = db;
    public string DocumentType => DocumentTypes.Training;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var t = await _db.EmployeeTraining.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Application not found.");
        t.Status = c.Status == WorkflowInstanceStatus.Approved ? TrainingParticipationStatus.Approved : TrainingParticipationStatus.Rejected;
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var x = await (from t in _db.EmployeeTraining.AsNoTracking()
                       join p in _db.TrainingPrograms on t.TrainingProgramId equals p.Id
                       where t.Id == documentId
                       select new { p.Title, p.Provider, p.StartsAtUtc, p.EndsAtUtc, p.Venue, p.CostPerParticipant }).FirstAsync(ct);
        return new List<DocumentDetail>
        {
            new("Programme", x.Title), new("Provider", x.Provider), new("Venue", x.Venue),
            new("Dates", x.StartsAtUtc is null ? null : $"{x.StartsAtUtc.Value.AddHours(3):d MMM yyyy} to {x.EndsAtUtc?.AddHours(3):d MMM yyyy}"),
            new("Cost", x.CostPerParticipant is null ? null : $"KES {x.CostPerParticipant:N0}"),
        };
    }
}
