using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Workflow;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Queue;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Workflow;

/// <summary>Approved personal-detail changes go to NAV through the outbox; NAV stays the master record (spec §6).</summary>
public sealed class ProfileChangeWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EssDbContext _db;
    private readonly IIntegrationQueue _queue;
    private readonly ISecretProtector _secrets;

    public ProfileChangeWorkflowHandler(EssDbContext db, IIntegrationQueue queue, ISecretProtector secrets)
    {
        _db = db;
        _queue = queue;
        _secrets = secrets;
    }

    public string DocumentType => DocumentTypes.ProfileChange;

    public async Task OnCompletedAsync(WorkflowCompletion c, CancellationToken ct)
    {
        var pcr = await _db.ProfileChangeRequests.FindAsync(new object[] { c.DocumentId }, ct) ?? throw new KeyNotFoundException("Change request not found.");
        switch (c.Status)
        {
            case WorkflowInstanceStatus.Approved:
                pcr.Status = RequestStatus.Approved;
                pcr.NavStatus = NavSyncStatus.PendingIntegration;
                var employeeNo = await _db.Employees.Where(e => e.Id == pcr.EmployeeId).Select(e => e.NavKey).FirstAsync(ct);
                var changes = Changes(pcr);
                var approver = await ApproverNameAsync(c.ActorUserId, ct);
                var key = IntegrationQueue.KeyFor(NavOperations.EmployeeChange, pcr.Id);
                await _queue.EnqueueAsync(pcr.CompanyId, NavOperations.EmployeeChange, nameof(Domain.Requests.ProfileChangeRequest), pcr.Id, key,
                    new NavEmployeeChangeSubmission(key, pcr.Reference, employeeNo, pcr.Section, changes, approver), sensitive: pcr.ChangesEncrypted, ct: ct);
                break;
            case WorkflowInstanceStatus.Rejected:
                pcr.Status = RequestStatus.Rejected;
                break;
            case WorkflowInstanceStatus.Returned:
                pcr.Status = RequestStatus.ReturnedForCorrection;
                break;
        }
    }

    public async Task<IReadOnlyList<DocumentDetail>> DescribeAsync(Guid documentId, CancellationToken ct)
    {
        var pcr = await _db.ProfileChangeRequests.AsNoTracking().FirstAsync(p => p.Id == documentId, ct);
        var json = JsonDocument.Parse(pcr.ChangesEncrypted ? _secrets.Unprotect(pcr.ChangesJson) : pcr.ChangesJson).RootElement;
        var list = new List<DocumentDetail> { new("Section", pcr.Section) };
        list.AddRange(Changes(pcr).Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => new DocumentDetail(kv.Key, kv.Value)));
        if (json.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) list.Add(new("Reason", r.GetString()));
        return list;
    }

    private IReadOnlyDictionary<string, string?> Changes(Domain.Requests.ProfileChangeRequest pcr)
    {
        var json = pcr.ChangesEncrypted ? _secrets.Unprotect(pcr.ChangesJson) : pcr.ChangesJson;
        return JsonDocument.Parse(json).RootElement.GetProperty("changes").Deserialize<Dictionary<string, string?>>() ?? new();
    }

    private async Task<string> ApproverNameAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct) ?? "ESS";
}
