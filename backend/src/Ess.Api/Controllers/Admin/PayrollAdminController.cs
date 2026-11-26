using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Api.Services;
using Ess.Application.Abstractions;
using Ess.Application.Notifications;
using Ess.Application.Security;
using Ess.Domain.Organization;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ess.Api.Controllers.Admin;

/// <summary>Payroll publication: employees see a period's payslips only after payroll staff publish it, and only once NAV has closed it.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/payroll")]
[Route("api/admin/payroll")]
[HasPermission(Permissions.PayrollPublish)]
public sealed class PayrollAdminController : ControllerBase
{
    private readonly EssDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditWriter _audit;

    public PayrollAdminController(EssDbContext db, ICurrentUser user, IAuditWriter audit)
    {
        _db = db;
        _user = user;
        _audit = audit;
    }

    [HttpGet("periods")]
    public async Task<IReadOnlyList<PayrollPeriodView>> Periods(CancellationToken ct) =>
        await _db.PayrollPeriodsCache.AsNoTracking().Where(p => p.CompanyId == _user.CompanyId).OrderByDescending(p => p.StartDate)
            .Select(p => new PayrollPeriodView(p.Id, p.NavKey, p.Name, p.StartDate, p.EndDate, p.PayDate, p.IsClosed, p.IsPublished, p.PublishedAtUtc)).ToListAsync(ct);

    public sealed record PublishInput(bool Notify = true);

    [HttpPost("periods/{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, PublishInput input, [FromServices] INotificationService notify, CancellationToken ct)
    {
        var p = await _db.PayrollPeriodsCache.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == _user.CompanyId, ct);
        if (p is null) return NotFound();
        if (!p.IsClosed) return Problem(statusCode: 409, title: "Period still open", detail: $"Close {p.Name} in NAV first. Payslips are published only for closed payroll periods.");
        if (p.IsPublished) return NoContent();
        p.IsPublished = true;
        p.PublishedAtUtc = DateTime.UtcNow;

        var recipients = 0;
        if (input.Notify)
        {
            var users = await (from m in _db.EmployeeMappings
                               join e in _db.Employees on m.EmployeeId equals e.Id
                               join u in _db.Users on m.UserId equals u.Id
                               where m.CompanyId == p.CompanyId && u.IsActive && e.Status != EmploymentStatus.Terminated
                               select u.Id).Distinct().ToListAsync(ct);
            foreach (var u in users)
                await notify.QueueAsync(new NotificationRequest(p.CompanyId, u, NotificationEvents.PayslipAvailable, $"Your {p.Name} payslip is available",
                    $"Your payslip for {p.Name} is now available in the portal.", "/payslips"), ct);
            recipients = users.Count;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("payroll.period_published", "PayrollPeriod", p.NavKey, new { recipients }, ct: ct);
        return NoContent();
    }

    [HttpPost("periods/{id:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct)
    {
        var p = await _db.PayrollPeriodsCache.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == _user.CompanyId, ct);
        if (p is null) return NotFound();
        p.IsPublished = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("payroll.period_unpublished", "PayrollPeriod", p.NavKey, ct: ct);
        return NoContent();
    }
}
