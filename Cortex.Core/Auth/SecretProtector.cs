using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Cortex.Core.Auth;

public interface ISecretProtector
{
    string Protect(string plain);
    string? Unprotect(string payload);
}

/// <summary>
/// Encrypts/decrypts user secrets (BYOK provider keys) with ASP.NET Core Data
/// Protection — the key ring lives outside the database, so the stored column
/// holds ciphertext only.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Cortex.ProviderKeys.v1");
    }

    public string Protect(string plain) => _protector.Protect(plain);

    public string? Unprotect(string payload)
    {
        try
        {
            return _protector.Unprotect(payload);
        }
        catch (CryptographicException)
        {
            // Key ring rotated or payload corrupted — treat as no key.
            return null;
        }
    }
}
