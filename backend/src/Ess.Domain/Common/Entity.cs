namespace Ess.Domain.Common;

/// <summary>Base type for every portal-side entity. Portal keys are GUIDs (spec §10).</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>Adds UTC audit stamps and soft delete. Values are filled by the DbContext, never by callers.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>Marks data that belongs to one NAV company. Queries are filtered by company.</summary>
public interface ICompanyScoped
{
    Guid CompanyId { get; set; }
}

/// <summary>Marks a portal record that is a cached copy of a NAV record (NAV stays the source of truth).</summary>
public interface INavMirrored
{
    /// <summary>The record's primary key in NAV, e.g. Employee "No." or a composite key rendered as text.</summary>
    string NavKey { get; set; }
    /// <summary>When the portal last pulled this record from NAV.</summary>
    DateTime LastSyncedAtUtc { get; set; }
    /// <summary>NAV's own modification marker (timestamp/rowversion/SystemModifiedAt) used for incremental sync.</summary>
    string? NavVersionToken { get; set; }
}
