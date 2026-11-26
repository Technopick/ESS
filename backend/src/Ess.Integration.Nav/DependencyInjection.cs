using Ess.Application.Integration;
using Ess.Integration.Nav.Adapters;
using Ess.Integration.Nav.Clients;
using Ess.Integration.Nav.Configuration;
using Ess.Integration.Nav.Monitoring;
using Ess.Integration.Nav.Queue;
using Ess.Integration.Nav.Security;
using Ess.Integration.Nav.Simulator;
using Ess.Integration.Nav.Sync;
using Ess.Integration.Nav.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ess.Integration.Nav;

public static class DependencyInjection
{
    /// <summary>Registers the NAV Integration Engine. Business modules only see the INav* ports and IIntegrationQueue.</summary>
    public static IServiceCollection AddNavIntegrationEngine(this IServiceCollection services, IConfiguration config, IHostEnvironment env,
        bool runWorker = true)
    {
        services.Configure<NavEngineOptions>(config.GetSection(NavEngineOptions.Section));
        services.AddHttpClient("nav-oauth");

        var allowSimulator = config.GetSection(NavEngineOptions.Section).GetValue<bool>(nameof(NavEngineOptions.AllowSimulator));
        if (allowSimulator && !env.IsProduction()) services.AddSingleton<NavSimulatorHandler>();

        services.AddSingleton<SensitiveDataMasker>();
        services.AddSingleton<NavAuthenticationManager>(sp => new NavAuthenticationManager(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NavAuthenticationManager>>(),
            sp.GetService<NavSimulatorHandler>()));
        services.AddSingleton<NavConnectionManager>();
        services.AddSingleton<NavTransport>();
        services.AddSingleton<NavODataClient>();
        services.AddSingleton<NavSoapClient>();

        services.AddSingleton<INavEmployeeService, NavEmployeeAdapter>();
        services.AddSingleton<INavOrganizationService, NavOrganizationAdapter>();
        services.AddSingleton<INavLeaveService, NavLeaveAdapter>();
        services.AddSingleton<INavPayrollService, NavPayrollAdapter>();
        services.AddSingleton<INavExpenseService, NavExpenseAdapter>();
        services.AddSingleton<INavConnectivityService, NavConnectivityService>();

        services.AddScoped<IIntegrationQueue, IntegrationQueue>();
        services.AddScoped<INavOperationHandler, LeaveSubmitHandler>();
        services.AddScoped<INavOperationHandler, LeaveCancelHandler>();
        services.AddScoped<INavOperationHandler, ExpenseSubmitHandler>();
        services.AddScoped<INavOperationHandler, EmployeeChangeHandler>();
        services.AddSingleton<IntegrationQueueProcessor>();
        services.AddSingleton<ReconciliationService>();
        services.AddSingleton<NavSyncService>();
        services.AddScoped<IntegrationDashboardService>();

        if (runWorker) services.AddHostedService<IntegrationWorker>();
        return services;
    }
}
