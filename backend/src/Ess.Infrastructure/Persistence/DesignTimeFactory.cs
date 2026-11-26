using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ess.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to build migrations. Runtime configuration comes from appsettings/environment.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EssDbContext>
{
    public EssDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ESS_DESIGN_CONNECTION")
                 ?? "Host=localhost;Database=ess_design;Username=postgres";
        var options = new DbContextOptionsBuilder<EssDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new EssDbContext(options);
    }
}
