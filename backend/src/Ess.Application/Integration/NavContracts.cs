namespace Ess.Application.Integration;

// Canonical NAV-side records. Adapters translate each NAV version's objects into these shapes,
// so business modules never see SOAP envelopes, OData field names or BC API routes.

public sealed record NavEmployeeRecord(
    string No,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? JobTitle,
    string? PositionCode,
    string? DepartmentCode,
    string? LocationCode,
    string? ManagerNo,
    string? CompanyEmail,
    string? MobilePhone,
    string Status,
    DateOnly? EmploymentDate,
    DateOnly? ContractEndDate,
    DateOnly? BirthDate,
    string? Gender,
    string? VersionToken,
    DateTime? LastModifiedUtc,
    bool IsDeletedInNav = false);

/// <summary>Full profile for the employee's own profile page; fetched on demand, not bulk-synced.</summary>
public sealed record NavEmployeeProfile(
    NavEmployeeRecord Core,
    IReadOnlyDictionary<string, string?> PersonalDetails,
    IReadOnlyDictionary<string, string?> Address,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> EmergencyContacts,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> NextOfKin,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Dependants,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Qualifications,
    /// <summary>Masked bank details (last 4 digits only) unless the company allows full display.</summary>
    IReadOnlyDictionary<string, string?>? BankDetailsMasked);

public sealed record NavReferenceRecord(string Code, string Name, string? ParentCode, string? VersionToken, DateTime? LastModifiedUtc, bool IsDeletedInNav = false);

public sealed record NavLeaveTypeRecord(string Code, string Description, decimal? DefaultEntitlement, bool? PaidLeave, string? Gender, string? VersionToken);

public sealed record NavLeaveBalanceRecord(string EmployeeNo, string LeaveTypeCode, string PeriodCode,
    decimal Entitlement, decimal BroughtForward, decimal Taken, decimal Pending, decimal Available);

public sealed record NavLeaveSubmission(
    string IdempotencyKey,
    string PortalReference,
    string EmployeeNo,
    string LeaveTypeCode,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Days,
    string? Reason,
    string? RelieverNo,
    /// <summary>True when the portal already approved it (portal-owned workflow); false when NAV's own workflow must run.</summary>
    bool PreApproved,
    string? ApprovedBy);

public sealed record NavDocumentStatus(string NavDocumentNo, string Status, string? StatusReason, DateTime? LastModifiedUtc);

public sealed record NavPayrollPeriodRecord(string Code, string Name, DateOnly StartDate, DateOnly EndDate, DateOnly? PayDate, bool Closed, string? VersionToken);

public sealed record NavPayslipLine(string Code, string Description, string Group, decimal Amount, decimal? YearToDate, int SortOrder);

/// <summary>Group values: Earning, Allowance, Bonus, Overtime, Statutory (PAYE, NSSF, SHIF, Housing Levy), Deduction, Relief, Information.</summary>
public sealed record NavPayslip(
    string EmployeeNo,
    string PeriodCode,
    string Currency,
    decimal BasicPay,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    DateOnly? PaymentDate,
    IReadOnlyList<NavPayslipLine> Lines);

public sealed record NavExpenseClaimLine(int LineNo, string ExpenseCode, DateOnly Date, string Description, decimal Amount, string Currency);

public sealed record NavExpenseClaimSubmission(
    string IdempotencyKey,
    string PortalReference,
    string EmployeeNo,
    string Purpose,
    string Currency,
    string? ProjectCode,
    string? CostCentreCode,
    bool PreApproved,
    string? ApprovedBy,
    IReadOnlyList<NavExpenseClaimLine> Lines);

public sealed record NavEmployeeChangeSubmission(
    string IdempotencyKey,
    string PortalReference,
    string EmployeeNo,
    string Section,
    IReadOnlyDictionary<string, string?> Changes,
    string ApprovedBy);

public sealed record NavConnectionTestResult(bool Success, string Message, string? NavVersion, long LatencyMs,
    IReadOnlyList<NavEndpointProbe> Endpoints);

public sealed record NavEndpointProbe(string LogicalName, string Protocol, string ServiceName, bool Reachable, int? HttpStatus, string? Message);
