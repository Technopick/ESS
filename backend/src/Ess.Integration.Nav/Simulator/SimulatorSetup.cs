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
        // Idempotent: creates the simulator once, and adds mappings introduced by later releases to an existing one.
        var settings = await db.NavIntegrationSettings.FirstOrDefaultAsync(s => s.Name == "SIMULATOR", ct);
        var existing = new HashSet<string>();
        if (settings is not null)
            existing.UnionWith(await db.NavEndpointMappings.Where(m => m.IntegrationSettingsId == settings.Id).Select(m => m.LogicalName).ToListAsync(ct));
        else
        {
            settings = new NavIntegrationSettings
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
        }

        void Map(string logical, string protocol, string service, string? method = null, string? modified = null)
        {
            if (!existing.Add(logical)) return;
            db.NavEndpointMappings.Add(new NavEndpointMapping
            {
                IntegrationSettingsId = settings.Id, LogicalName = logical, Protocol = protocol, ServiceName = service, Method = method, ModifiedField = modified
            });
        }

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
        Map("Payslip.Header", "OData", "ESSPayslipHeaders");
        Map("Payslip.Lines", "OData", "ESSPayslipLines");
        await db.SaveChangesAsync(ct);
    }
}
