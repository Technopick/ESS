using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ess.Api.Infrastructure;
using Ess.Domain.Identity;
using Ess.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Ess.Api.Auth;

/// <summary>
/// Issues short-lived JWT access tokens and rotating refresh tokens. Refresh tokens are stored hashed; reuse of a
/// rotated token revokes the whole token family (stolen-token detection).
/// </summary>
public sealed class TokenService
{
    private readonly EssDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public TokenService(EssDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
    }

    public sealed record IssuedTokens(string AccessToken, DateTime AccessTokenExpiresUtc, string RefreshToken, DateTime RefreshTokenExpiresUtc);

    public async Task<IssuedTokens> IssueAsync(User user, string? familyId, string? ip, string? userAgent, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var roles = await _db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.Role!.Name).Distinct().ToListAsync(ct);
        var perms = await _db.UserRoles.Where(ur => ur.UserId == user.Id)
            .SelectMany(ur => ur.Role!.RolePermissions.Select(rp => rp.Permission!.Code)).Distinct().ToListAsync(ct);
        var companyId = user.DefaultCompanyId;
        var employeeId = companyId is null ? null : await _db.EmployeeMappings
            .Where(m => m.UserId == user.Id && m.CompanyId == companyId).Select(m => (Guid?)m.EmployeeId).FirstOrDefaultAsync(ct);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("name", user.UserName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("sstamp", user.SecurityStamp)
        };
        if (companyId is not null) claims.Add(new(HttpCurrentUser.CompanyClaim, companyId.Value.ToString()));
        if (employeeId is not null) claims.Add(new(HttpCurrentUser.EmployeeClaim, employeeId.Value.ToString()));
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        claims.AddRange(perms.Select(p => new Claim(HttpCurrentUser.PermissionClaim, p)));

        var accessExpires = now.AddMinutes(_config.GetValue("Jwt:AccessTokenMinutes", 15));
        var token = new JwtSecurityToken(_config["Jwt:Issuer"] ?? "ess-portal", _config["Jwt:Audience"] ?? "ess-portal", claims, now, accessExpires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey())), SecurityAlgorithms.HmacSha256));

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshExpires = now.AddDays(_config.GetValue("Jwt:RefreshTokenDays", 7));
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = Hash(raw), FamilyId = familyId ?? Guid.NewGuid().ToString("N"),
            CreatedAtUtc = now, ExpiresAtUtc = refreshExpires, CreatedByIp = ip, UserAgent = userAgent?.Length > 1000 ? userAgent[..1000] : userAgent
        });
        await _db.SaveChangesAsync(ct);
        return new IssuedTokens(new JwtSecurityTokenHandler().WriteToken(token), accessExpires, raw, refreshExpires);
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private string SigningKey()
    {
        var k = _config["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(k) && k.Length >= 32) return k;
        if (_env.IsDevelopment()) return "development-only-signing-key-change-me-0123456789";
        throw new InvalidOperationException("Jwt:SigningKey is not configured.");
    }
}
