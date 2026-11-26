using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Application.Notifications;
using Ess.Application.Workflow;
using Ess.Domain.Common;
using Ess.Domain.Expenses;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Queue;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

public sealed record ExpenseLineInput(Guid CategoryId, DateOnly ExpenseDate, string Description, decimal Amount, string? Currency, decimal? ExchangeRate, Guid? ReceiptDocumentId);
public sealed record ExpenseClaimInput(string Purpose, string? ProjectCode, string? CostCentreCode, IReadOnlyList<ExpenseLineInput> Lines);

/// <summary>
/// Expense claims (spec §6): drafted and submitted in the portal, approved by the line manager then finance (limits by amount),
/// then posted to NAV as a pre-approved claim; payment status comes back from NAV.
/// </summary>
public sealed class ExpenseService
{
    private readonly EssDbContext _db;
    private readonly IWorkflowEngine _workflow;
    private readonly IIntegrationQueue _queue;
    private readonly INavExpenseService _nav;
    private readonly IReferenceGenerator _refs;
    private readonly INotificationService _notify;
    private readonly IConfiguration _config;

    public ExpenseService(EssDbContext db, IWorkflowEngine workflow, IIntegrationQueue queue, INavExpenseService nav, IReferenceGenerator refs,
        INotificationService notify, IConfiguration config)
    {
        _db = db;
        _workflow = workflow;
        _queue = queue;
        _nav = nav;
        _refs = refs;
        _notify = notify;
        _config = config;
    }

    public string BaseCurrency => _config["Expenses:BaseCurrency"] ?? "KES";

    /// <summary>Default categories per company on first use; finance can change them.</summary>
    public async Task EnsureCategoriesAsync(Guid companyId, CancellationToken ct)
    {
        if (await _db.ExpenseCategories.AnyAsync(c => c.CompanyId == companyId, ct)) return;
        foreach (var (code, name, receipt) in new[] { ("TRAVEL", "Travel and transport", true), ("ACCOM", "Accommodation", true), ("MEALS", "Meals and subsistence", true),
                     ("FUEL", "Fuel and mileage", true), ("COMMS", "Airtime and data", false), ("OTHER", "Other", true) })
            _db.ExpenseCategories.Add(new ExpenseCategory { CompanyId = companyId, Code = code, Name = name, NavExpenseCode = code, RequiresReceipt = receipt });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Validates and returns errors (blocking) and warnings (e.g. possible duplicates).</summary>
    public async Task<(List<string> Errors, List<string> Warnings)> CheckAsync(Employee employee, ExpenseClaimInput input, Guid? claimId, bool forSubmit, CancellationToken ct)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Purpose) || input.Purpose.Trim().Length < 3) errors.Add("Describe the purpose of the claim.");
        if (input.Purpose?.Length > 250) errors.Add("Keep the purpose under 250 characters.");
        if (input.Lines.Count == 0) errors.Add("Add at least one expense.");
        if (input.Lines.Count > 50) errors.Add("A claim can have at most 50 lines.");

        var categories = await _db.ExpenseCategories.AsNoTracking().Where(c => c.CompanyId == employee.CompanyId && c.IsActive).ToDictionaryAsync(c => c.Id, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var maxAge = _config.GetValue("Expenses:MaxAgeDays", 90);
        var receiptIds = input.Lines.Where(l => l.ReceiptDocumentId is not null).Select(l => l.ReceiptDocumentId!.Value).ToList();
        var receipts = await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => receiptIds.Contains(d.Id)).Select(d => new { d.Id, d.EmployeeId, d.OwnerEntityId }).ToDictionaryAsync(d => d.Id, ct);

        for (var i = 0; i < input.Lines.Count; i++)
        {
            var l = input.Lines[i];
            var n = $"Line {i + 1}";
            if (!categories.TryGetValue(l.CategoryId, out var cat)) { errors.Add($"{n}: choose a category."); continue; }
            if (l.Amount <= 0) errors.Add($"{n}: enter an amount above zero.");
            if (string.IsNullOrWhiteSpace(l.Description)) errors.Add($"{n}: describe the expense.");
            if (l.ExpenseDate > today) errors.Add($"{n}: the date cannot be in the future.");
            if (l.ExpenseDate < today.AddDays(-maxAge)) errors.Add($"{n}: expenses older than {maxAge} days cannot be claimed here. Contact finance.");
            var currency = (l.Currency ?? BaseCurrency).ToUpperInvariant();
            if (currency.Length != 3 || !currency.All(char.IsLetter)) errors.Add($"{n}: use a three-letter currency code.");
            if (currency != BaseCurrency && l.ExchangeRate is not > 0) errors.Add($"{n}: enter the exchange rate to {BaseCurrency}.");
            var baseAmount = l.Amount * (currency == BaseCurrency ? 1 : l.ExchangeRate ?? 0);
            if (cat.MaxAmountPerLine is { } max && baseAmount > max) errors.Add($"{n}: {cat.Name} is limited to {BaseCurrency} {max:N0} per expense.");
            if (l.ReceiptDocumentId is { } rid)
            {
                if (!receipts.TryGetValue(rid, out var r) || r.EmployeeId != employee.Id || (r.OwnerEntityId is not null && r.OwnerEntityId != claimId))
                    errors.Add($"{n}: the receipt was not found. Upload it again.");
            }
            else if (forSubmit && cat.RequiresReceipt) errors.Add($"{n}: {cat.Name} needs a receipt.");
        }

        // Possible duplicates: same category, date and amount in another active claim.
        var active = new[] { RequestStatus.Submitted, RequestStatus.InApproval, RequestStatus.Approved, RequestStatus.Completed };
        foreach (var l in input.Lines)
        {
            var dup = await (from line in _db.ExpenseClaimLines
                             join c in _db.ExpenseClaims on line.ExpenseClaimId equals c.Id
                             where c.EmployeeId == employee.Id && c.Id != claimId && active.Contains(c.Status)
                                   && line.CategoryId == l.CategoryId && line.ExpenseDate == l.ExpenseDate && line.Amount == l.Amount
                             select c.Reference).FirstOrDefaultAsync(ct);
            if (dup is not null) warnings.Add($"An expense of {l.Amount:N2} on {l.ExpenseDate:d MMM} is already in claim {dup}. Check it is not a duplicate.");
        }
        return (errors, warnings.Distinct().ToList());
    }

    public async Task<ExpenseClaim> SaveDraftAsync(Employee employee, Guid? claimId, ExpenseClaimInput input, CancellationToken ct)
    {
        ExpenseClaim claim;
        if (claimId is null)
        {
            claim = new ExpenseClaim
            {
                CompanyId = employee.CompanyId, Reference = await _refs.NextAsync(employee.CompanyId, "EXP", ct), EmployeeId = employee.Id,
                Purpose = input.Purpose.Trim(), Status = RequestStatus.Draft, NavStatus = NavSyncStatus.NotRequired, Currency = BaseCurrency
            };
            _db.ExpenseClaims.Add(claim);
        }
        else
        {
            claim = await _db.ExpenseClaims.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == claimId && c.EmployeeId == employee.Id, ct)
                    ?? throw new KeyNotFoundException("Claim not found.");
            if (claim.Status is not (RequestStatus.Draft or RequestStatus.ReturnedForCorrection)) throw new WorkflowException("Only drafts and returned claims can be edited.");
            claim.Purpose = input.Purpose.Trim();
            // Release receipts no longer used, then replace the lines.
            var keep = input.Lines.Where(l => l.ReceiptDocumentId is not null).Select(l => l.ReceiptDocumentId).ToHashSet();
            foreach (var old in claim.Lines.Where(l => l.ReceiptDocumentId is not null && !keep.Contains(l.ReceiptDocumentId)))
                await _db.EmployeeDocuments.Where(d => d.Id == old.ReceiptDocumentId).ExecuteUpdateAsync(s => s.SetProperty(d => d.OwnerEntityId, (Guid?)null).SetProperty(d => d.OwnerEntityType, (string?)null), ct);
            _db.ExpenseClaimLines.RemoveRange(claim.Lines);
            claim.Lines.Clear();
        }
        claim.ProjectCode = string.IsNullOrWhiteSpace(input.ProjectCode) ? null : input.ProjectCode.Trim();
        claim.CostCentreCode = string.IsNullOrWhiteSpace(input.CostCentreCode) ? null : input.CostCentreCode.Trim();

        var no = 0;
        foreach (var l in input.Lines)
        {
            var currency = (l.Currency ?? BaseCurrency).ToUpperInvariant();
            // Added through the DbSet: a client-generated key on an existing claim's collection would otherwise be treated as an update.
            var line = _db.ExpenseClaimLines.Add(new ExpenseClaimLine
            {
                ExpenseClaimId = claim.Id, LineNo = (no += 10000), CategoryId = l.CategoryId, ExpenseDate = l.ExpenseDate, Description = l.Description.Trim(),
                Amount = Math.Round(l.Amount, 2), Currency = currency, ExchangeRate = currency == BaseCurrency ? 1 : l.ExchangeRate ?? 1, ReceiptDocumentId = l.ReceiptDocumentId
            }).Entity;
            if (!claim.Lines.Contains(line)) claim.Lines.Add(line); // EF fix-up usually adds it already
        }
        claim.TotalAmount = Math.Round(claim.Lines.Sum(l => l.Amount * l.ExchangeRate), 2);
        await _db.SaveChangesAsync(ct);

        var receiptIds = claim.Lines.Where(l => l.ReceiptDocumentId is not null).Select(l => l.ReceiptDocumentId!.Value).ToList();
        await _db.EmployeeDocuments.Where(d => receiptIds.Contains(d.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.OwnerEntityId, claim.Id).SetProperty(d => d.OwnerEntityType, DocumentService.ExpenseClaimOwner), ct);
        return claim;
    }

    public async Task SubmitAsync(Employee employee, Guid userId, Guid claimId, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == claimId && c.EmployeeId == employee.Id, ct)
                    ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.Status is not (RequestStatus.Draft or RequestStatus.ReturnedForCorrection)) throw new WorkflowException("This claim has already been submitted.");
        var (errors, _) = await CheckAsync(employee, ToInput(claim), claim.Id, forSubmit: true, ct);
        if (errors.Count > 0) throw new FluentValidation.ValidationException(errors.Select(e => new FluentValidation.Results.ValidationFailure("claim", e)));

        claim.Status = RequestStatus.Submitted;
        claim.SubmittedAtUtc = DateTime.UtcNow;
        var result = await _workflow.StartAsync(new WorkflowStartRequest(claim.CompanyId, DocumentTypes.Expense, claim.Id, claim.Reference, userId, employee.Id,
            claim.TotalAmount, $"{employee.FullName}: {claim.Purpose}, {claim.Currency} {claim.TotalAmount:N2}", $"/expenses/{claim.Id}"), ct);
        claim.WorkflowInstanceId = result.InstanceId;
        if (result.Outcome == WorkflowOutcome.Pending) claim.Status = RequestStatus.InApproval;
        else if (result.Outcome == WorkflowOutcome.NavOwned)
        {
            claim.Status = RequestStatus.InApproval;
            await EnqueueAsync(employee, claim, preApproved: false, approvedBy: null, ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task EnqueueAsync(Employee employee, ExpenseClaim claim, bool preApproved, string? approvedBy, CancellationToken ct)
    {
        var codes = await _db.ExpenseCategories.Where(c => c.CompanyId == claim.CompanyId).ToDictionaryAsync(c => c.Id, c => c.NavExpenseCode ?? c.Code, ct);
        var revision = await _db.IntegrationQueue.CountAsync(q => q.EntityId == claim.Id && q.Operation == NavOperations.ExpenseSubmit, ct);
        var key = IntegrationQueue.KeyFor(NavOperations.ExpenseSubmit, claim.Id, revision);
        claim.NavStatus = NavSyncStatus.PendingIntegration;
        await _queue.EnqueueAsync(claim.CompanyId, NavOperations.ExpenseSubmit, nameof(ExpenseClaim), claim.Id, key,
            new NavExpenseClaimSubmission(key, claim.Reference, employee.NavKey, claim.Purpose, claim.Currency, claim.ProjectCode, claim.CostCentreCode, preApproved, approvedBy,
                claim.Lines.OrderBy(l => l.LineNo).Select(l => new NavExpenseClaimLine(l.LineNo, codes[l.CategoryId], l.ExpenseDate, l.Description,
                    Math.Round(l.Amount * l.ExchangeRate, 2), claim.Currency)).ToList()), sensitive: false, ct: ct);
    }

    public async Task CancelAsync(Employee employee, Guid userId, Guid claimId, CancellationToken ct)
    {
        var claim = await _db.ExpenseClaims.FirstOrDefaultAsync(c => c.Id == claimId && c.EmployeeId == employee.Id, ct) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.Status is RequestStatus.Draft or RequestStatus.ReturnedForCorrection || (claim.Status == RequestStatus.InApproval && claim.NavDocumentNo is null && claim.NavStatus == NavSyncStatus.NotRequired))
        {
            if (claim.WorkflowInstanceId is not null && claim.Status == RequestStatus.InApproval) await _workflow.CancelAsync(claim.WorkflowInstanceId.Value, userId, "Withdrawn by the employee", ct);
            claim.Status = RequestStatus.Withdrawn;
            await _db.EmployeeDocuments.Where(d => d.OwnerEntityId == claim.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.OwnerEntityId, (Guid?)null).SetProperty(d => d.OwnerEntityType, (string?)null), ct);
            await _db.SaveChangesAsync(ct);
            return;
        }
        throw new WorkflowException("Approved claims can only be cancelled by finance.");
    }

    /// <summary>Scheduled: copies NAV's decision (NAV-owned approval) and payment back to the portal.</summary>
    public async Task<int> MirrorNavStatusesAsync(CancellationToken ct)
    {
        var claims = await _db.ExpenseClaims
            .Where(c => c.NavDocumentNo != null && c.NavStatus == NavSyncStatus.Posted && (c.Status == RequestStatus.InApproval || c.Status == RequestStatus.Approved))
            .OrderBy(c => c.SubmittedAtUtc).Take(200).ToListAsync(ct);
        var changed = 0;
        foreach (var c in claims)
        {
            var s = await _nav.GetStatusAsync(c.CompanyId, c.NavDocumentNo!, ct);
            if (!s.Success || s.Value is null) continue;
            var status = s.Value.Status.ToLowerInvariant();
            string? message = null;
            if (status is "paid")
            {
                c.Status = RequestStatus.Completed;
                c.PaidAtUtc = s.Value.LastModifiedUtc ?? DateTime.UtcNow;
                c.PaymentReference = s.Value.StatusReason;
                message = $"Your claim {c.Reference} has been paid.";
            }
            else if (status is "approved" or "released" or "posted" && c.Status == RequestStatus.InApproval) { c.Status = RequestStatus.Approved; message = $"Your claim {c.Reference} was approved in NAV."; }
            else if (status is "rejected" && c.Status == RequestStatus.InApproval) { c.Status = RequestStatus.Rejected; message = $"Your claim {c.Reference} was rejected in NAV."; }
            if (message is null) continue;
            changed++;
            var user = await _db.EmployeeMappings.Where(m => m.EmployeeId == c.EmployeeId).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
            if (user is not null)
                await _notify.QueueAsync(new NotificationRequest(c.CompanyId, user.Value, NotificationEvents.RequestApproved, message, message, $"/expenses/{c.Id}"), ct);
        }
        await _db.SaveChangesAsync(ct);
        return changed;
    }

    public static ExpenseClaimInput ToInput(ExpenseClaim c) =>
        new(c.Purpose, c.ProjectCode, c.CostCentreCode, c.Lines.OrderBy(l => l.LineNo)
            .Select(l => new ExpenseLineInput(l.CategoryId, l.ExpenseDate, l.Description, l.Amount, l.Currency, l.ExchangeRate, l.ReceiptDocumentId)).ToList());
}
