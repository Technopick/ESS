using Ess.Domain.Common;

namespace Ess.Domain.Audit;

/// <summary>Append-only audit trail of critical operations (spec §9, Kenyan DPA accountability).</summary>
public class AuditLog : Entity
{
    public DateTime OccurredAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? CompanyId { get; set; }
    /// <summary>e.g. "auth.login", "payslip.download", "leave.approve", "nav.retry".</summary>
    public required string Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    /// <summary>Changed fields, masked where sensitive.</summary>
    public string? ChangesJson { get; set; }
    public bool Success { get; set; } = true;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
}

public class SystemSetting : AuditableEntity
{
    public Guid? CompanyId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public bool IsSecret { get; set; }
    public string? Description { get; set; }
}

/// <summary>Allocates human-readable sequential references (LV-2026-000001) per company, prefix and year.</summary>
public class ReferenceSequence
{
    public Guid CompanyId { get; set; }
    public required string Prefix { get; set; }
    public int Year { get; set; }
    public long LastValue { get; set; }
}
