namespace Ess.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>The authenticated caller. Every module resolves "my" data through this, never from request input.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    Guid? CompanyId { get; }
    Guid? EmployeeId { get; }
    bool HasPermission(string permission);
    bool IsInRole(string role);
    string? IpAddress { get; }
    string? CorrelationId { get; }
}

/// <summary>Encrypts secrets and sensitive fields at rest (NAV passwords, MFA secrets, bank details).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public interface IAuditWriter
{
    Task WriteAsync(string action, string? entityType = null, string? entityId = null,
        object? changes = null, bool success = true, CancellationToken ct = default);
}

/// <summary>Allocates unique human-readable references such as LV-2026-000123.</summary>
public interface IReferenceGenerator
{
    Task<string> NextAsync(Guid companyId, string prefix, CancellationToken ct = default);
}
