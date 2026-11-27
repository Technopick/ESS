namespace Ess.Integration.Nav.Configuration;

/// <summary>
/// The endpoint mappings for the recommended NAV customisation (docs/nav-customizations.md): read-only ESS pages published
/// as OData services and the "ESS Portal Gateway" codeunit published as a SOAP service. Applying the preset to a NAV
/// instance adds the mappings it does not have yet; existing rows (and their field maps) are left alone.
/// </summary>
public static class NavEndpointPresets
{
    public const string Gateway = "ESSPortalGateway";

    public sealed record Preset(string LogicalName, string Protocol, string ServiceName, string? Method = null, string? ModifiedField = null);

    public static IReadOnlyList<Preset> StandardGateway { get; } = new Preset[]
    {
        new("Employee", "OData", "ESSEmployees", ModifiedField: "Last_Date_Modified"),
        new("Department", "OData", "ESSDepartments"),
        new("Position", "OData", "ESSPositions"),
        new("Location", "OData", "ESSLocations"),
        new("LeaveType", "OData", "ESSLeaveTypes"),
        new("LeaveBalance", "OData", "ESSLeaveBalances"),
        new("PayrollPeriod", "OData", "ESSPayrollPeriods"),
        new("Payslip.Header", "OData", "ESSPayslipHeaders"),
        new("Payslip.Lines", "OData", "ESSPayslipLines"),
        new("Employee.Profile", "Soap", Gateway, "GetEmployeeProfile"),
        new("Leave.Submit", "Soap", Gateway, "SubmitLeaveApplication"),
        new("Leave.Cancel", "Soap", Gateway, "CancelLeaveApplication"),
        new("Leave.Status", "Soap", Gateway, "GetLeaveStatus"),
        new("Leave.FindByRef", "Soap", Gateway, "FindLeaveByExternalRef"),
        new("Expense.Submit", "Soap", Gateway, "SubmitExpenseClaim"),
        new("Expense.Status", "Soap", Gateway, "GetClaimStatus"),
        new("Expense.FindByRef", "Soap", Gateway, "FindClaimByExternalRef"),
    };
}
