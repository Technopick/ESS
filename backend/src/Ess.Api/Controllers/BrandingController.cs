using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asp.Versioning;
using Ess.Api.Infrastructure;
using Ess.Application.Abstractions;
using Ess.Application.Security;
using Ess.Domain.Audit;
using Ess.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ess.Api.Controllers;

/// <summary>
/// Look and feel: portal name, welcome text, colours, corner style, default light/dark mode and logo. Read without signing in
/// (the sign-in page uses it); changed by administrators with settings.manage. Stored as system settings, the logo as a
/// small PNG/JPEG/WebP (no SVG, which can carry script).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/branding")]
[Route("api/branding")]
[Authorize]
public sealed class BrandingController : ControllerBase
{
    public const string SettingKey = "branding.theme";
    public const string LogoKey = "branding.logo";
    private const string CacheKey = "branding";
    private const int MaxLogoBytes = 512 * 1024;
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly EssDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAuditWriter _audit;

    public BrandingController(EssDbContext db, IMemoryCache cache, IAuditWriter audit)
    {
        _db = db;
        _cache = cache;
        _audit = audit;
    }

    public sealed record BrandingSettings(string AppName, string? WelcomeText, string Primary, string Secondary, string DefaultMode, int CornerRadius);
    public sealed record BrandingView(string AppName, string? WelcomeText, string Primary, string Secondary, string DefaultMode, int CornerRadius, string? LogoUrl);

    public static readonly BrandingSettings Defaults = new("Employee Self-Service", null, "#0b5394", "#38761d", "system", 10);

    [HttpGet]
    [AllowAnonymous]
    public async Task<BrandingView> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache";
        return await _cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var rows = await _db.SystemSettings.AsNoTracking().Where(s => s.CompanyId == null && (s.Key == SettingKey || s.Key == LogoKey)).ToListAsync(ct);
            var b = Read(rows.FirstOrDefault(r => r.Key == SettingKey)?.Value);
            var logo = rows.FirstOrDefault(r => r.Key == LogoKey);
            string? logoUrl = logo is null ? null : $"/api/branding/logo?v={Version(logo.Value)}";
            return new BrandingView(b.AppName, b.WelcomeText, b.Primary, b.Secondary, b.DefaultMode, b.CornerRadius, logoUrl);
        }) ?? throw new InvalidOperationException();
    }

    [HttpGet("logo")]
    [AllowAnonymous]
    public async Task<IActionResult> Logo(CancellationToken ct)
    {
        var value = await _db.SystemSettings.AsNoTracking().Where(s => s.CompanyId == null && s.Key == LogoKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (value is null) return NotFound();
        var (type, bytes) = ParseLogo(value);
        Response.Headers.CacheControl = "public, max-age=31536000, immutable"; // the URL changes with the content
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, type);
    }

    [HttpPut]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> Save(BrandingSettings input, CancellationToken ct)
    {
        var errors = Validate(input);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));
        var clean = input with
        {
            AppName = input.AppName.Trim(),
            WelcomeText = string.IsNullOrWhiteSpace(input.WelcomeText) ? null : input.WelcomeText.Trim(),
            Primary = input.Primary.ToLowerInvariant(),
            Secondary = input.Secondary.ToLowerInvariant(),
        };
        await UpsertAsync(SettingKey, JsonSerializer.Serialize(clean), ct);
        await _audit.WriteAsync("branding.updated", nameof(SystemSetting), SettingKey, clean, ct: ct);
        _cache.Remove(CacheKey);
        return Ok(await Get(ct));
    }

    public sealed class LogoUpload
    {
        public IFormFile File { get; set; } = default!;
    }

    [HttpPost("logo")]
    [HasPermission(Permissions.SettingsManage)]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public async Task<IActionResult> UploadLogo([FromForm] LogoUpload upload, CancellationToken ct)
    {
        var f = upload.File;
        if (f is null || f.Length == 0) return Field("file", "Choose an image.");
        if (f.Length > MaxLogoBytes) return Field("file", "The logo must be 512 KB or smaller.");
        await using var s = f.OpenReadStream();
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var type = Sniff(bytes);
        if (type is null) return Field("file", "Use a PNG, JPEG or WebP image. SVG is not accepted because it can contain script.");
        await UpsertAsync(LogoKey, $"{type};base64,{Convert.ToBase64String(bytes)}", ct);
        await _audit.WriteAsync("branding.logo.updated", nameof(SystemSetting), LogoKey, new { type, size = bytes.Length }, ct: ct);
        _cache.Remove(CacheKey);
        return Ok(await Get(ct));
    }

    [HttpDelete("logo")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> RemoveLogo(CancellationToken ct)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.CompanyId == null && s.Key == LogoKey, ct);
        if (row is not null)
        {
            _db.SystemSettings.Remove(row);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("branding.logo.removed", nameof(SystemSetting), LogoKey, null, ct: ct);
        }
        _cache.Remove(CacheKey);
        return Ok(await Get(ct));
    }

    // ------------------------------------------------------------------ helpers

    internal static BrandingSettings Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults;
        try
        {
            var b = JsonSerializer.Deserialize<BrandingSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return b is null || Validate(b).Count > 0 ? Defaults : b;
        }
        catch (JsonException) { return Defaults; }
    }

    internal static Dictionary<string, string[]> Validate(BrandingSettings b)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(b.AppName) || b.AppName.Trim().Length > 60) e["appName"] = new[] { "Enter a portal name of up to 60 characters." };
        if (b.WelcomeText is { Length: > 200 }) e["welcomeText"] = new[] { "Keep the welcome text to 200 characters." };
        if (b.Primary is null || !Hex.IsMatch(b.Primary)) e["primary"] = new[] { "Use a colour like #0b5394." };
        else if (Contrast(b.Primary, "#ffffff") < 3.0)
            e["primary"] = new[] { $"White text on {b.Primary} is too hard to read (contrast {Contrast(b.Primary, "#ffffff"):0.0}:1; at least 3:1 is needed). Choose a darker colour." };
        if (b.Secondary is null || !Hex.IsMatch(b.Secondary)) e["secondary"] = new[] { "Use a colour like #38761d." };
        if (b.DefaultMode is not ("light" or "dark" or "system")) e["defaultMode"] = new[] { "Choose light, dark or system." };
        if (b.CornerRadius is < 0 or > 20) e["cornerRadius"] = new[] { "Corner radius must be between 0 and 20." };
        return e;
    }

    /// <summary>WCAG 2 contrast ratio between two #rrggbb colours.</summary>
    internal static double Contrast(string a, string b)
    {
        static double L(string hex)
        {
            double C(int i)
            {
                var v = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber) / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * C(0) + 0.7152 * C(1) + 0.0722 * C(2);
        }
        var (l1, l2) = (L(a), L(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static string? Sniff(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P' ? "image/webp"
        : null;

    private static (string Type, byte[] Bytes) ParseLogo(string value)
    {
        var cut = value.IndexOf(";base64,", StringComparison.Ordinal);
        return (value[..cut], Convert.FromBase64String(value[(cut + 8)..]));
    }

    private static string Version(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value)))[..12].ToLowerInvariant();

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.CompanyId == null && s.Key == key, ct);
        if (row is null) _db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = "Look and feel (System administration, Look and feel)" });
        else row.Value = value;
        await _db.SaveChangesAsync(ct);
    }

    private IActionResult Field(string field, string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = new[] { message } }));
}
