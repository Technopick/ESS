using System.Security.Claims;
using Ess.Application.Abstractions;

namespace Ess.Api.Infrastructure;

/// <summary>Reads the caller's identity, company, linked employee and permissions from the validated access token.</summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    public const string CompanyClaim = "ess_company";
    public const string EmployeeClaim = "ess_employee";
    public const string PermissionClaim = "perm";

    private readonly IHttpContextAccessor _http;
    public HttpCurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"), out var g) ? g : null;
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name) ?? Principal?.FindFirstValue("name");
    public Guid? CompanyId => Guid.TryParse(Principal?.FindFirstValue(CompanyClaim), out var g) ? g : null;
    public Guid? EmployeeId => Guid.TryParse(Principal?.FindFirstValue(EmployeeClaim), out var g) ? g : null;
    public bool HasPermission(string permission) => Principal?.HasClaim(PermissionClaim, permission) == true;
    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => _http.HttpContext?.TraceIdentifier;
}
