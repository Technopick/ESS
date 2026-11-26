using Ess.Domain.Integration;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Integration.Nav.Simulator;

/// <summary>
/// DEVELOPMENT ONLY. Creates a "SIMULATOR" NAV instance and a "SIM" company wired to the simulator, with endpoint
/// mappings that mirror the recommended NAV customisation (docs/nav-customizations.md). Called only when
/// DemoData:Enabled is true and the environment is not Production. Every record is clearly labelled.
/// </summary>
public static class SimulatorSetup
{
    public const string Gateway = "ESSPortalGateway";

    public static async Task EnsureAsync(EssDbContext db, CancellationToken ct = default)
    {
        if (await db.NavIntegrationSettings.AnyAsync(s => s.Name == "SIMULATOR", ct)) return;

        var settings = new NavIntegrationSettings
        {
            Name = "SIMULATOR",
            Platform = NavPlatform.Nav2018,
            ServerHost = "nav-simulator.invalid",
            ServiceInstance = "DynamicsNAV110",
            SoapBaseUrl = "http://nav-simulator.invalid:7047/DynamicsNAV110/WS",
            ODataBaseUrl = "http://nav-simulator.invalid:7048/DynamicsNAV110/ODataV4",
            AuthMethod = NavAuthMethod.NavUserPassword,
            UserName = "SIMULATOR",
            UseSimulator = true
        };
        db.NavIntegrationSettings.Add(settings);
        db.Companies.Add(new Company { Code = "SIM", DisplayName = "Simulated Company (demo data)", NavCompanyName = "CRONUS Simulated Ltd", IntegrationSettingsId = settings.Id });

        void Map(string logical, string protocol, string service, string? method = null, string? modified = null) =>
            db.NavEndpointMappings.Add(new NavEndpointMapping
            {
                IntegrationSettingsId = settings.Id, LogicalName = logical, Protocol = protocol, ServiceName = service, Method = method, ModifiedField = modified
            });

        Map("Employee", "OData", "ESSEmployees", modified: "Last_Date_Modified");
        Map("Department", "OData", "ESSDepartments");
        Map("Position", "OData", "ESSPositions");
        Map("Location", "OData", "ESSLocations");
        Map("LeaveType", "OData", "ESSLeaveTypes");
        Map("LeaveBalance", "OData", "ESSLeaveBalances");
        Map("PayrollPeriod", "OData", "ESSPayrollPeriods");
        Map("Employee.Profile", "Soap", Gateway, "GetEmployeeProfile");
        Map("Leave.Submit", "Soap", Gateway, "SubmitLeaveApplication");
        Map("Leave.Cancel", "Soap", Gateway, "CancelLeaveApplication");
        Map("Leave.Status", "Soap", Gateway, "GetLeaveStatus");
        Map("Leave.FindByRef", "Soap", Gateway, "FindLeaveByExternalRef");
        Map("Expense.Submit", "Soap", Gateway, "SubmitExpenseClaim");
        Map("Expense.FindByRef", "Soap", Gateway, "FindClaimByExternalRef");
        await db.SaveChangesAsync(ct);
    }
}
