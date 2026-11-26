using Ess.Application.Abstractions;
using Ess.Infrastructure.Persistence;
using Ess.Infrastructure.Security;
using Ess.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ess.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEssInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Ess")
                 ?? throw new InvalidOperationException("ConnectionStrings:Ess is not configured.");

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditStampInterceptor>();
        services.AddDbContext<EssDbContext>((sp, o) => o
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history");
                npg.EnableRetryOnFailure(3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditStampInterceptor>()));

        services.AddDataProtection().SetApplicationName("Ess.Portal");
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IReferenceGenerator, ReferenceGenerator>();
        services.AddScoped<DatabaseSeeder>();
        services.Configure<Email.SmtpOptions>(config.GetSection(Email.SmtpOptions.Section));
        services.AddSingleton<IEmailSender, Email.SmtpEmailSender>();
        return services;
    }
}
