using Ess.Domain.Common;

namespace Ess.Domain.Attendance;

public enum AttendanceSource { Portal = 0, Biometric = 1, Nav = 2, Import = 3 }
public enum AttendanceRequestType { Correction = 0, MissingPunch = 1, Overtime = 2 }

public class AttendanceRecord : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTime? ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public AttendanceSource Source { get; set; }
    public string? SourceDeviceId { get; set; }
    public string? ExternalRef { get; set; }
    public int LateMinutes { get; set; }
    public decimal WorkedHours { get; set; }
    public string? ShiftCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public class Shift : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
}

public class AttendanceRequest : AuditableEntity, ICompanyScoped
{
    public Guid CompanyId { get; set; }
    public required string Reference { get; set; }
    public Guid EmployeeId { get; set; }
    public AttendanceRequestType Type { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTime? RequestedClockInUtc { get; set; }
    public DateTime? RequestedClockOutUtc { get; set; }
    public decimal? OvertimeHours { get; set; }
    public required string Reason { get; set; }
    public RequestStatus Status { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public NavSyncStatus NavStatus { get; set; }
}
