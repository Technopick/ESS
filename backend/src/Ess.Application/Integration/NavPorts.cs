namespace Ess.Application.Integration;

// Ports the business modules depend on. Implemented by the NAV Integration Engine (Ess.Integration.Nav),
// which picks SOAP / OData / BC API per NAV version and per endpoint mapping. Every method is company-scoped
// because one portal can serve several NAV companies and service instances.

public interface INavEmployeeService
{
    Task<NavResult<NavEmployeeRecord?>> GetEmployeeAsync(Guid companyId, string employeeNo, CancellationToken ct);
    Task<NavResult<NavEmployeeProfile>> GetProfileAsync(Guid companyId, string employeeNo, CancellationToken ct);
    Task<NavResult<NavPage<NavEmployeeRecord>>> GetEmployeesChangedSinceAsync(Guid companyId, string? watermark, string? continuation, CancellationToken ct);
    Task<NavResult<NavSubmissionReceipt>> SubmitChangeAsync(Guid companyId, NavEmployeeChangeSubmission change, CancellationToken ct);
}

public interface INavOrganizationService
{
    Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetDepartmentsAsync(Guid companyId, CancellationToken ct);
    Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetPositionsAsync(Guid companyId, CancellationToken ct);
    Task<NavResult<IReadOnlyList<NavReferenceRecord>>> GetLocationsAsync(Guid companyId, CancellationToken ct);
    Task<NavResult<IReadOnlyList<DateOnly>>> GetNonWorkingDatesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken ct);
}

public interface INavLeaveService
{
    Task<NavResult<IReadOnlyList<NavLeaveTypeRecord>>> GetLeaveTypesAsync(Guid companyId, CancellationToken ct);
    Task<NavResult<IReadOnlyList<NavLeaveBalanceRecord>>> GetBalancesAsync(Guid companyId, string employeeNo, CancellationToken ct);
    /// <summary>Asks NAV to validate (balance, overlap, calendar) without creating anything, where NAV supports it.</summary>
    Task<NavResult<decimal>> ValidateAsync(Guid companyId, NavLeaveSubmission submission, CancellationToken ct);
    Task<NavResult<NavSubmissionReceipt>> SubmitAsync(Guid companyId, NavLeaveSubmission submission, CancellationToken ct);
    Task<NavResult<NavSubmissionReceipt>> CancelAsync(Guid companyId, string navDocumentNo, string idempotencyKey, string reason, CancellationToken ct);
    Task<NavResult<NavDocumentStatus?>> GetStatusAsync(Guid companyId, string navDocumentNo, CancellationToken ct);
    /// <summary>Looks a submission up by the portal's idempotency key; used by reconciliation after an uncertain outcome.</summary>
    Task<NavResult<NavSubmissionReceipt?>> FindByExternalReferenceAsync(Guid companyId, string idempotencyKey, CancellationToken ct);
}

public interface INavPayrollService
{
    Task<NavResult<IReadOnlyList<NavPayrollPeriodRecord>>> GetPeriodsAsync(Guid companyId, CancellationToken ct);
    Task<NavResult<NavPayslip?>> GetPayslipAsync(Guid companyId, string employeeNo, string periodCode, CancellationToken ct);
    /// <summary>NAV-rendered payslip PDF, where the NAV report is exposed (preferred over portal rendering).</summary>
    Task<NavResult<byte[]?>> GetPayslipPdfAsync(Guid companyId, string employeeNo, string periodCode, CancellationToken ct);
    /// <summary>Every employee's payslip totals and statutory lines for one period, for authorised payroll reports.</summary>
    Task<NavResult<IReadOnlyList<NavPayslipTotals>>> GetPeriodTotalsAsync(Guid companyId, string periodCode, CancellationToken ct);
}

public interface INavExpenseService
{
    Task<NavResult<NavSubmissionReceipt>> SubmitClaimAsync(Guid companyId, NavExpenseClaimSubmission claim, CancellationToken ct);
    Task<NavResult<NavDocumentStatus?>> GetStatusAsync(Guid companyId, string navDocumentNo, CancellationToken ct);
    Task<NavResult<NavSubmissionReceipt?>> FindByExternalReferenceAsync(Guid companyId, string idempotencyKey, CancellationToken ct);
}

public interface INavConnectivityService
{
    Task<NavConnectionTestResult> TestAsync(Guid integrationSettingsId, CancellationToken ct);
}

/// <summary>Writes an ESS → NAV transaction into the integration outbox in the caller's DB transaction.</summary>
public interface IIntegrationQueue
{
    Task<Guid> EnqueueAsync(Guid companyId, string operation, string entityType, Guid entityId,
        string idempotencyKey, object payload, bool sensitive, int priority = 0, CancellationToken ct = default);
}

/// <summary>Well-known outbox operation names. Each has one handler in the integration engine.</summary>
public static class NavOperations
{
    public const string LeaveSubmit = "Leave.Submit";
    public const string LeaveCancel = "Leave.Cancel";
    public const string ExpenseSubmit = "Expense.Submit";
    public const string EmployeeChange = "Employee.Change";
    public const string HrRequestSubmit = "HrRequest.Submit";
}
