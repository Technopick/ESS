using Ess.Domain.Common;

namespace Ess.Domain.Expenses;

public class ExpenseCategory : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>NAV code the posting codeunit maps to a G/L account. The portal never posts to the G/L.</summary>
    public string? NavExpenseCode { get; set; }
    public bool RequiresReceipt { get; set; } = true;
    public decimal? MaxAmountPerLine { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ExpenseClaim : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    public required string Purpose { get; set; }
    public string Currency { get; set; } = "KES";
    public decimal TotalAmount { get; set; }
    /// <summary>NAV dimension values (project / cost centre), validated against NAV.</summary>
    public string? ProjectCode { get; set; }
    public string? CostCentreCode { get; set; }
    public RequestStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public NavSyncStatus NavStatus { get; set; }
    public string? NavDocumentNo { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public uint RowVersion { get; set; }
    public ICollection<ExpenseClaimLine> Lines { get; set; } = new List<ExpenseClaimLine>();
}

public class ExpenseClaimLine : Entity
{
    public Guid ExpenseClaimId { get; set; }
    public int LineNo { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KES";
    public decimal ExchangeRate { get; set; } = 1m;
    public Guid? ReceiptDocumentId { get; set; }
}
