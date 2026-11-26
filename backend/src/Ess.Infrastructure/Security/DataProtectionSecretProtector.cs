using Ess.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Ess.Infrastructure.Security;

/// <summary>
/// Encrypts secrets at rest with ASP.NET Core Data Protection (AES-256-CBC + HMACSHA256, rotating keys).
/// In production the key ring must be persisted (database/file share) and protected with a certificate or DPAPI;
/// see docs/deployment.md.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Ess.Secrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
