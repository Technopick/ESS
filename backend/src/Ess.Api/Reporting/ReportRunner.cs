using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Reporting;

/// <summary>Runs catalogue reports for the signed-in user with their permission and department scope applied.</summary>
public sealed class ReportRunner
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly EmployeeContext _me;
    private readonly IConfiguration _config;
    private readonly IServiceProvider _services;
    private readonly IClock _clock;

    public ReportRunner(EssDbContext db, ICurrentUser user, EmployeeContext me, IConfiguration config, IServiceProvider services, IClock clock)
    {
        _db = db;
        _user = user;
        _me = me;
        _config = config;
        _services = services;
        _clock = clock;
    }

    public int Offset => _config.GetValue("App:UtcOffsetHours", 3);
    public DateTime LocalNow => _clock.UtcNow.AddHours(Offset);

    public bool CanRun(ReportDefinition r) => _user.HasPermission(r.Permission);

    public async Task<ReportContext> ContextAsync(string permission, ReportFilter f, CancellationToken ct)
    {
        var scope = await _me.DepartmentScopeAsync(permission, ct);
        var ctx = new ReportContext
        {
            Db = _db, CompanyId = _user.CompanyId ?? throw new UnauthorizedAccessException(), Scope = scope,
            Today = DateOnly.FromDateTime(LocalNow), UtcOffsetHours = Offset, Services = _services
        };
        if (f.DepartmentId is { } dept)
        {
            if (scope is not null && !scope.Contains(dept)) throw new UnauthorizedAccessException("That department is outside your area.");
            ctx.DepartmentWithChildren = await WithChildrenAsync(ctx.CompanyId, dept, ct);
        }
        return ctx;
    }

    public async Task<ReportTable> RunAsync(ReportDefinition r, ReportFilter f, CancellationToken ct)
    {
        if (!CanRun(r)) throw new UnauthorizedAccessException("You do not have access to this report.");
        return await r.Run(await ContextAsync(r.Permission, f, ct), f, ct);
    }

    public async Task<string> GeneratedByAsync(CancellationToken ct) =>
        (await _me.GetAsync(ct))?.FullName ?? _user.UserName ?? "system";

    private async Task<IReadOnlyList<Guid>> WithChildrenAsync(Guid companyId, Guid root, CancellationToken ct)
    {
        var all = await _db.Departments.AsNoTracking().Where(d => d.CompanyId == companyId).Select(d => new { d.Id, d.ParentDepartmentId }).ToListAsync(ct);
        var set = new HashSet<Guid> { root };
        bool added;
        do
        {
            added = false;
            foreach (var d in all.Where(d => d.ParentDepartmentId is { } p && set.Contains(p) && !set.Contains(d.Id))) added |= set.Add(d.Id);
        } while (added);
        return set.ToList();
    }
}
