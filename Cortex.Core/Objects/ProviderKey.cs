namespace Cortex.Core.Objects;

/// <summary>
/// A user's own API key for a cloud provider (BYOK), stored encrypted at rest
/// (ASP.NET Data Protection — see Auth/SecretProtector). Never returned by the API.
/// </summary>
public class ProviderKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>ChatProviderKind serialized as string (no EF enum conversion needed).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Data Protection payload — ciphertext only.</summary>
    public string Protected { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
