using Ess.Domain.Common;

namespace Ess.Domain.Leave;

/// <summary>Leave type mirrored from NAV ("Cause of Absence" or the HR add-on's leave type table).</summary>
public class LeaveType : AuditableEntity, ICompanyScoped, INavMirrored
{
    public Guid CompanyId { get; set; }
    public required string NavKey { get; set; }
    public required string Name { get; set; }
    /// <summary>Portal-side rules. NAV rules win where NAV validates the same thing.</summary>
    public bool CountsWorkingDaysOnly { get; set; } = true;
    public bool RequiresAttachment { get; set; }
    public int? MaxDaysPerApplication { get; set; }
    public int MinNoticeDays { get; set; }
    public bool AllowNegativeBalance { get; set; }
    public bool AllowHalfDay { get; set; }
    /// <summary>Optional gender restriction (e.g. maternity/paternity), as configured by HR.</summary>
    public string? EligibleGender { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? WorkflowDefinitionId { get; set; }
    public DateTime LastSyncedAtUtc { get; set; }
    public string? NavVersionToken { get; set; }
}

/// <summary>Cached balance pulled from NAV. Never computed by the portal (NAV is authoritative).</summary>
public class LeaveBalanceCache : Entity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public required string LeavePeriodCode { get; set; }
    public decimal Entitlement { get; set; }
    public decimal BroughtForward { get; set; }
    public decimal Taken { get; set; }
    public decimal PendingApproval { get; set; }
    public decimal Available { get; set; }
    public DateTime RetrievedAtUtc { get; set; }
}

public class LeaveApplication : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    /// <summary>Human-readable reference, e.g. LV-2026-000123.</summary>
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool StartHalfDay { get; set; }
    public bool EndHalfDay { get; set; }
    public decimal DaysRequested { get; set; }
    public string? Reason { get; set; }
    public Guid? RelieverEmployeeId { get; set; }
    public RequestStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public NavSyncStatus NavStatus { get; set; }
    /// <summary>Document number NAV returned when it accepted the application.</summary>
    public string? NavDocumentNo { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public uint RowVersion { get; set; }
}

public class PublicHoliday : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    /// <summary>Base calendar code from NAV, when holidays are synced from a NAV base calendar.</summary>
    public string? NavCalendarCode { get; set; }
}
