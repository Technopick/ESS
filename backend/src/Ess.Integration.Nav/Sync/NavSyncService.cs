using Ess.Application.Abstractions;
using Ess.Application.Integration;
using Ess.Domain.Integration;
using Ess.Domain.Leave;
using Ess.Domain.Organization;
using Ess.Domain.Payroll;
using Ess.Infrastructure.Persistence;
using Ess.Integration.Nav.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ess.Integration.Nav.Sync;

/// <summary>
/// Background Synchronization Service (spec §3, component 7): NAV → ESS for master and reference data.
/// Incremental by NAV modification marker; a full sync also deactivates records that disappeared from NAV
/// (deletes cannot be seen incrementally). Every run is recorded in nav_sync_jobs.
/// </summary>
public sealed class NavSyncService
{
    public static readonly string[] Entities = { "Department", "Position", "Location", "Employee", "LeaveType", "PayrollPeriod" };

    private readonly IServiceScopeFactory _scopes;
    private readonly IClock _clock;
    private readonly ILogger<NavSyncService> _log;

    public NavSyncService(IServiceScopeFactory scopes, IClock clock, ILogger<NavSyncService> log)
    {
        _scopes = scopes;
        _clock = clock;
        _log = log;
    }

    /// <summary>Syncs every entity for every active company that has NAV settings (the scheduled job).</summary>
    public async Task SyncAllCompaniesAsync(bool full, string triggeredBy, CancellationToken ct)
    {
        List<Guid> companies;
        using (var scope = _scopes.CreateScope())
            companies = await scope.ServiceProvider.GetRequiredService<EssDbContext>().Companies
                .Where(c => c.IsActive && c.IntegrationSettingsId != null).Select(c => c.Id).ToListAsync(ct);
        foreach (var c in companies)
            await SyncCompanyAsync(c, full, triggeredBy, ct);
    }

    /// <summary>Syncs all entities of one company in dependency order (reference data before employees).</summary>
    public async Task SyncCompanyAsync(Guid companyId, bool full, string triggeredBy, CancellationToken ct)
    {
        foreach (var e in Entities)
            await SyncAsync(companyId, e, full, triggeredBy, ct);
    }

    public async Task<NavSyncJob> SyncAsync(Guid companyId, string entity, bool full, string triggeredBy, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<EssDbContext>();

        // One sync per company and entity at a time, across every server: the scheduled incremental and nightly full jobs
        // can start together (for example after a restart), and two runs would both insert the same new employees.
        await db.Database.OpenConnectionAsync(ct);
        var lockKey = LockKey(companyId, entity);
        var waitedSince = _clock.UtcNow;
        while (!await TryLockAsync(db, lockKey, ct))
        {
            if (_clock.UtcNow - waitedSince > TimeSpan.FromMinutes(30))
                throw new TimeoutException($"Another {entity} sync for this company has been running for over 30 minutes.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        try
        {
            return await RunLockedAsync(sp, db, companyId, entity, full, triggeredBy, ct);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0}, {1})", new object[] { LockNamespace, lockKey }, CancellationToken.None);
        }
    }

    private const int LockNamespace = 0x455353; // "ESS"

    private static int LockKey(Guid companyId, string entity)
    {
        var h = 2166136261u;
        foreach (var c in companyId.ToString("N") + ":" + entity) h = (h ^ c) * 16777619u;
        return unchecked((int)h);
    }

    private static async Task<bool> TryLockAsync(EssDbContext db, int key, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}, {1}) AS \"Value\"", LockNamespace, key).SingleAsync(ct);

    private async Task<NavSyncJob> RunLockedAsync(IServiceProvider sp, EssDbContext db, Guid companyId, string entity, bool full, string triggeredBy, CancellationToken ct)
    {
        var lastWatermark = full ? null : await db.NavSyncJobs.AsNoTracking()
            .Where(j => j.CompanyId == companyId && j.EntityName == entity && j.Status == SyncJobStatus.Succeeded)
            .OrderByDescending(j => j.StartedAtUtc).Select(j => j.Watermark).FirstOrDefaultAsync(ct);

        var job = new NavSyncJob { CompanyId = companyId, EntityName = entity, IsFullSync = full || lastWatermark is null,
            TriggeredBy = triggeredBy, Status = SyncJobStatus.Running, StartedAtUtc = _clock.UtcNow };
        db.NavSyncJobs.Add(job);
        await db.SaveChangesAsync(ct);

        using var _ = NavCallScope.Begin(syncJobId: job.Id);
        try
        {
            var error = entity switch
            {
                "Employee" => await SyncEmployeesAsync(sp, db, job, lastWatermark, ct),
                "Department" => await SyncReferenceAsync(db, job, await sp.GetRequiredService<INavOrganizationService>().GetDepartmentsAsync(companyId, ct), db.Departments,
                    (code, name) => new Department { CompanyId = companyId, NavKey = code, Name = name }, (d, n) => d.Name = n, ct),
                "Position" => await SyncReferenceAsync(db, job, await sp.GetRequiredService<INavOrganizationService>().GetPositionsAsync(companyId, ct), db.Positions,
                    (code, name) => new Position { CompanyId = companyId, NavKey = code, Title = name }, (p, n) => p.Title = n, ct),
                "Location" => await SyncReferenceAsync(db, job, await sp.GetRequiredService<INavOrganizationService>().GetLocationsAsync(companyId, ct), db.Locations,
                    (code, name) => new Location { CompanyId = companyId, NavKey = code, Name = name }, (l, n) => l.Name = n, ct),
                "LeaveType" => await SyncLeaveTypesAsync(sp, db, job, ct),
                "PayrollPeriod" => await SyncPayrollPeriodsAsync(sp, db, job, ct),
                _ => new NavError(NavErrorCategory.Configuration, $"Unknown sync entity '{entity}'.")
            };
            job.Status = error is null ? (job.RecordsFailed > 0 ? SyncJobStatus.PartiallySucceeded : SyncJobStatus.Succeeded) : SyncJobStatus.Failed;
            job.Error = error?.Message;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Sync of {Entity} for company {Company} failed", entity, companyId);
            db.ChangeTracker.Clear();
            db.Attach(job);
            job.Status = SyncJobStatus.Failed;
            job.Error = ex.Message;
        }
        job.FinishedAtUtc = _clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return job;
    }

    private async Task<NavError?> SyncEmployeesAsync(IServiceProvider sp, EssDbContext db, NavSyncJob job, string? watermark, CancellationToken ct)
    {
        var nav = sp.GetRequiredService<INavEmployeeService>();
        var companyId = job.CompanyId;
        var depts = await db.Departments.Where(d => d.CompanyId == companyId).ToDictionaryAsync(d => d.NavKey, d => d.Id, ct);
        var positions = await db.Positions.Where(d => d.CompanyId == companyId).ToDictionaryAsync(d => d.NavKey, d => d.Id, ct);
        var locations = await db.Locations.Where(d => d.CompanyId == companyId).ToDictionaryAsync(d => d.NavKey, d => d.Id, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var managerLinks = new List<(Guid EmployeeId, string ManagerNo)>();
        string? continuation = null;
        var nextWatermark = watermark;

        do
        {
            var page = await nav.GetEmployeesChangedSinceAsync(companyId, job.IsFullSync ? null : watermark, continuation, ct);
            if (!page.Success) return page.Error;
            var keys = page.Value!.Items.Select(i => i.No).ToList();
            var existing = await db.Employees.IgnoreQueryFilters().Where(e => e.CompanyId == companyId && keys.Contains(e.NavKey)).ToDictionaryAsync(e => e.NavKey, ct);

            foreach (var r in page.Value.Items)
            {
                job.RecordsRead++;
                seen.Add(r.No);
                try
                {
                    if (!existing.TryGetValue(r.No, out var e))
                    {
                        e = new Employee { CompanyId = companyId, NavKey = r.No, FirstName = r.FirstName, LastName = r.LastName };
                        db.Employees.Add(e);
                        job.RecordsCreated++;
                    }
                    else job.RecordsUpdated++;

                    e.IsDeleted = false;
                    e.FirstName = r.FirstName;
                    e.MiddleName = r.MiddleName;
                    e.LastName = r.LastName;
                    e.WorkEmail = r.CompanyEmail;
                    e.MobilePhone = r.MobilePhone;
                    e.DepartmentId = r.DepartmentCode is not null && depts.TryGetValue(r.DepartmentCode, out var d) ? d : null;
                    e.PositionId = r.PositionCode is not null && positions.TryGetValue(r.PositionCode, out var p) ? p : null;
                    e.LocationId = r.LocationCode is not null && locations.TryGetValue(r.LocationCode, out var l) ? l : null;
                    e.Status = MapStatus(r.Status);
                    e.EmploymentDate = r.EmploymentDate;
                    e.ContractEndDate = r.ContractEndDate;
                    e.TerminationDate = r.TerminationDate;
                    e.Gender = r.Gender;
                    e.BirthDate = r.BirthDate;
                    e.NavVersionToken = r.VersionToken;
                    e.LastSyncedAtUtc = _clock.UtcNow;
                    if (r.ManagerNo is not null) managerLinks.Add((e.Id, r.ManagerNo));
                }
                catch (Exception ex)
                {
                    job.RecordsFailed++;
                    _log.LogWarning(ex, "Could not map NAV employee {No}", r.No);
                }
            }
            await db.SaveChangesAsync(ct);
            continuation = page.Value.HasMore ? page.Value.ContinuationToken : null;
            nextWatermark = page.Value.NextWatermark ?? nextWatermark;
        } while (continuation is not null);

        // Resolve line managers after all employees exist.
        if (managerLinks.Count > 0)
        {
            var byNo = await db.Employees.Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.NavKey, e => e.Id, ct);
            var byId = await db.Employees.Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Id, ct);
            foreach (var (empId, mgrNo) in managerLinks)
                if (byId.TryGetValue(empId, out var emp)) emp.ManagerEmployeeId = byNo.TryGetValue(mgrNo, out var m) ? m : null;
        }

        if (job.IsFullSync)
        {
            // Employees no longer returned by NAV are deactivated, never hard-deleted (history and audit stay intact).
            var gone = await db.Employees.Where(e => e.CompanyId == companyId && e.Status != EmploymentStatus.Inactive).ToListAsync(ct);
            foreach (var e in gone.Where(e => !seen.Contains(e.NavKey)))
            {
                e.Status = EmploymentStatus.Inactive;
                job.RecordsDeactivated++;
            }
        }
        job.Watermark = nextWatermark;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private async Task<NavError?> SyncReferenceAsync<T>(EssDbContext db, NavSyncJob job, NavResult<IReadOnlyList<NavReferenceRecord>> result, DbSet<T> set,
        Func<string, string, T> create, Action<T, string> update, CancellationToken ct) where T : Domain.Common.AuditableEntity, Domain.Common.INavMirrored, Domain.Common.ICompanyScoped
    {
        if (!result.Success) return result.Error;
        var existing = await set.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId).ToDictionaryAsync(x => x.NavKey, StringComparer.OrdinalIgnoreCase, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in result.Value!)
        {
            job.RecordsRead++;
            seen.Add(r.Code);
            if (!existing.TryGetValue(r.Code, out var row))
            {
                row = create(r.Code, r.Name);
                set.Add(row);
                job.RecordsCreated++;
            }
            else job.RecordsUpdated++;
            update(row, r.Name);
            row.IsDeleted = false;
            row.LastSyncedAtUtc = _clock.UtcNow;
            row.NavVersionToken = r.VersionToken;
        }
        foreach (var gone in existing.Values.Where(x => !x.IsDeleted && !seen.Contains(x.NavKey)))
        {
            set.Remove(gone); // soft delete via interceptor
            job.RecordsDeactivated++;
        }
        job.Watermark = _clock.UtcNow.ToString("O");
        await db.SaveChangesAsync(ct);
        return null;
    }

    private async Task<NavError?> SyncLeaveTypesAsync(IServiceProvider sp, EssDbContext db, NavSyncJob job, CancellationToken ct)
    {
        var r = await sp.GetRequiredService<INavLeaveService>().GetLeaveTypesAsync(job.CompanyId, ct);
        if (!r.Success) return r.Error;
        var existing = await db.LeaveTypes.IgnoreQueryFilters().Where(x => x.CompanyId == job.CompanyId).ToDictionaryAsync(x => x.NavKey, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var t in r.Value!)
        {
            job.RecordsRead++;
            if (!existing.TryGetValue(t.Code, out var lt))
            {
                // Portal-side rules (notice days, attachments, workflow) are configured by HR after the first sync.
                lt = new LeaveType { CompanyId = job.CompanyId, NavKey = t.Code, Name = t.Description, EligibleGender = t.Gender };
                db.LeaveTypes.Add(lt);
                job.RecordsCreated++;
            }
            else job.RecordsUpdated++;
            lt.Name = t.Description;
            lt.IsDeleted = false;
            lt.LastSyncedAtUtc = _clock.UtcNow;
            lt.NavVersionToken = t.VersionToken;
        }
        foreach (var gone in existing.Values.Where(x => x.IsActive && r.Value.All(t => !t.Code.Equals(x.NavKey, StringComparison.OrdinalIgnoreCase))))
        {
            gone.IsActive = false;
            job.RecordsDeactivated++;
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    private async Task<NavError?> SyncPayrollPeriodsAsync(IServiceProvider sp, EssDbContext db, NavSyncJob job, CancellationToken ct)
    {
        var r = await sp.GetRequiredService<INavPayrollService>().GetPeriodsAsync(job.CompanyId, ct);
        if (!r.Success) return r.Error;
        var existing = await db.PayrollPeriodsCache.Where(x => x.CompanyId == job.CompanyId).ToDictionaryAsync(x => x.NavKey, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var p in r.Value!)
        {
            job.RecordsRead++;
            if (!existing.TryGetValue(p.Code, out var row))
            {
                row = new PayrollPeriodCache { CompanyId = job.CompanyId, NavKey = p.Code, Name = p.Name };
                db.PayrollPeriodsCache.Add(row);
                job.RecordsCreated++;
            }
            else job.RecordsUpdated++;
            row.Name = p.Name;
            row.StartDate = p.StartDate;
            row.EndDate = p.EndDate;
            row.PayDate = p.PayDate;
            row.IsClosed = p.Closed;
            row.LastSyncedAtUtc = _clock.UtcNow;
            row.NavVersionToken = p.VersionToken;
            // IsPublished is controlled by payroll staff in the portal, never by sync.
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static EmploymentStatus MapStatus(string navStatus) => navStatus.Trim().ToLowerInvariant() switch
    {
        "active" or "0" => EmploymentStatus.Active,
        "inactive" or "1" => EmploymentStatus.Inactive,
        "terminated" or "2" => EmploymentStatus.Terminated,
        "probation" or "on probation" => EmploymentStatus.OnProbation,
        "suspended" => EmploymentStatus.Suspended,
        // Unknown NAV status values must not grant portal access; map them explicitly in configuration.
        _ => EmploymentStatus.Inactive
    };
}
