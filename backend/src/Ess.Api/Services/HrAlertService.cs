using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Services;

/// <summary>HR alerts that are not tied to a request, such as contracts approaching their end date.</summary>
public sealed class HrAlertService
{
    private static readonly int[] ContractNoticeDays = { 60, 30, 7 };
    private readonly EssDbContext _db;
    private readonly INotificationService _notify;
    private readonly IConfiguration _config;

    public HrAlertService(EssDbContext db, INotificationService notify, IConfiguration config)
    {
        _db = db;
        _notify = notify;
        _config = config;
    }

    /// <summary>
    /// Tells the line manager and the HR staff responsible for the department when a contract ends in 60, 30 or 7 days.
    /// Runs daily; each employee is announced once per notice day because only exact dates match.
    /// </summary>
    public async Task<int> NotifyContractExpiriesAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(_config.GetValue("App:UtcOffsetHours", 3)));
        var targets = ContractNoticeDays.Select(d => today.AddDays(d)).ToList();
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.ContractEndDate != null && targets.Contains(e.ContractEndDate.Value) && e.Status != EmploymentStatus.Terminated)
            .Select(e => new { e.Id, e.CompanyId, e.DepartmentId, e.ManagerEmployeeId, Name = e.FirstName + " " + e.LastName, End = e.ContractEndDate!.Value })
            .ToListAsync(ct);
        var count = 0;
        foreach (var e in employees)
        {
            var recipients = new HashSet<Guid>(await UsersWithPermissionAsync(e.CompanyId, Permissions.EmployeesViewAll, e.DepartmentId, ct));
            if (e.ManagerEmployeeId is { } mgr)
                foreach (var u in await _db.EmployeeMappings.Where(m => m.EmployeeId == mgr).Select(m => m.UserId).ToListAsync(ct)) recipients.Add(u);
            // The employee's own account is never told through this alert; HR handles that conversation.
            foreach (var u in await _db.EmployeeMappings.Where(m => m.EmployeeId == e.Id).Select(m => m.UserId).ToListAsync(ct)) recipients.Remove(u);

            var days = e.End.DayNumber - today.DayNumber;
            foreach (var userId in recipients)
            {
                await _notify.QueueAsync(new NotificationRequest(e.CompanyId, userId, NotificationEvents.ContractExpiring,
                    $"{e.Name}'s contract ends on {e.End:d MMM yyyy}", $"{e.Name}'s contract ends in {days} days, on {e.End:d MMM yyyy}. Decide on renewal in good time.",
                    $"/hr/employees/{e.Id}", Values: new Dictionary<string, string> { ["employee"] = e.Name, ["date"] = e.End.ToString("d MMM yyyy"), ["days"] = days.ToString() }), ct);
                count++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>Active users holding a permission through a role that is company-wide or scoped to the department (or one of its parents).</summary>
    public async Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(Guid companyId, string permission, Guid? departmentId, CancellationToken ct)
    {
        var chain = new List<Guid>();
        var parents = await _db.Departments.AsNoTracking().Where(d => d.CompanyId == companyId).ToDictionaryAsync(d => d.Id, d => d.ParentDepartmentId, ct);
        for (var d = departmentId; d is not null && !chain.Contains(d.Value); d = parents.GetValueOrDefault(d.Value)) chain.Add(d.Value);
        return await _db.UserRoles.AsNoTracking()
            .Where(ur => (ur.CompanyId == null || ur.CompanyId == companyId) && (ur.ScopeDepartmentId == null || chain.Contains(ur.ScopeDepartmentId.Value))
                         && ur.Role!.RolePermissions.Any(rp => rp.Permission!.Code == permission)
                         && _db.Users.Any(u => u.Id == ur.UserId && u.IsActive))
            .Select(ur => ur.UserId).Distinct().ToListAsync(ct);
    }
}
