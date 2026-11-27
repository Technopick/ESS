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

        // Keys protect stored NAV credentials, bank details and every document, so they must survive restarts and be shared
        // by all servers: DataProtection:KeysPath points at a persistent folder (on Windows encrypted with machine DPAPI).
        var dp = services.AddDataProtection().SetApplicationName("Ess.Portal");
        var keysPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            // A certificate (LocalMachine\My) lets the keys be restored on another server, which disaster recovery needs;
            // machine DPAPI ties them to this server.
            var thumbprint = config["DataProtection:CertificateThumbprint"];
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.My, System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var cert = store.Certificates.Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint, thumbprint.Replace(" ", ""), false)
                    .OfType<System.Security.Cryptography.X509Certificates.X509Certificate2>().FirstOrDefault()
                    ?? throw new InvalidOperationException("DataProtection:CertificateThumbprint does not match a certificate in LocalMachine\\My.");
                dp.ProtectKeysWithCertificate(cert);
            }
            else if (OperatingSystem.IsWindows() && config.GetValue("DataProtection:ProtectWithDpapi", true))
                dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IReferenceGenerator, ReferenceGenerator>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<Ess.Application.Notifications.INotificationService, Notifications.NotificationService>();
        services.AddScoped<Ess.Application.Workflow.IWorkflowEngine, Workflow.WorkflowEngine>();
        services.AddSingleton<Ess.Application.Documents.IFileStorage, Storage.EncryptedFileStorage>();
        // ClamAV when configured; otherwise Microsoft Defender on Windows (Storage:Scanner = "ClamAv" or "Defender" forces one).
        services.AddSingleton<Ess.Application.Documents.IMalwareScanner>(sp =>
        {
            var choice = config["Storage:Scanner"];
            var defender = Storage.DefenderScanner.Locate();
            var useDefender = defender is not null && (string.Equals(choice, "Defender", StringComparison.OrdinalIgnoreCase)
                              || (string.IsNullOrEmpty(choice) && string.IsNullOrWhiteSpace(config["Storage:ClamAv:Host"])));
            if (useDefender)
            {
                var root = config["Storage:RootPath"] ?? Path.Combine(AppContext.BaseDirectory, "storage");
                return new Storage.DefenderScanner(defender!, Path.Combine(root, ".scan"), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Storage.DefenderScanner>>());
            }
            return ActivatorUtilities.CreateInstance<Storage.ClamAvScanner>(sp);
        });
        services.Configure<Email.SmtpOptions>(config.GetSection(Email.SmtpOptions.Section));
        services.AddSingleton<IEmailSender, Email.SmtpEmailSender>();
        services.Configure<Sms.SmsOptions>(config.GetSection(Sms.SmsOptions.Section));
        services.AddHttpClient<ISmsSender, Sms.ConfigurableSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(20));
        return services;
    }
}
