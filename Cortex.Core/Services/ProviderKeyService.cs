using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Services;

public interface IProviderKeyStore
{
    /// <summary>Plaintext key from the user's vault, or null when absent.</summary>
    Task<string?> GetKeyAsync(Guid userId, ChatProviderKind provider, CancellationToken ct = default);

    Task<List<(ChatProviderKind Provider, DateTimeOffset UpdatedAt)>> ListAsync(Guid userId, CancellationToken ct = default);

    Task SetKeyAsync(Guid userId, ChatProviderKind provider, string key, CancellationToken ct = default);

    Task<bool> RemoveKeyAsync(Guid userId, ChatProviderKind provider, CancellationToken ct = default);
}

/// <summary>
/// Server-side BYOK vault: per-user provider keys encrypted with Data Protection.
/// Resolution order for outgoing provider calls is: request header > this vault >
/// the server's own configured key.
/// </summary>
public class ProviderKeyService : IProviderKeyStore
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _secrets;

    public ProviderKeyService(AppDbContext db, ISecretProtector secrets)
    {
        _db = db;
        _secrets = secrets;
    }

    private Task<ProviderKey?> FindAsync(Guid userId, ChatProviderKind provider, CancellationToken ct) =>
        _db.ProviderKeys.FirstOrDefaultAsync(
            k => k.UserId == userId && k.Provider == provider.ToString(), ct);

    public async Task<string?> GetKeyAsync(Guid userId, ChatProviderKind provider, CancellationToken ct = default)
    {
        var entity = await FindAsync(userId, provider, ct);
        return entity is null ? null : _secrets.Unprotect(entity.Protected);
    }

    public async Task<List<(ChatProviderKind, DateTimeOffset)>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.ProviderKeys
            .Where(k => k.UserId == userId)
            .Select(k => new { k.Provider, k.UpdatedAt })
            .ToListAsync(ct);
        return rows
            .Select(r => (Enum.Parse<ChatProviderKind>(r.Provider, ignoreCase: true), r.UpdatedAt))
            .ToList();
    }

    public async Task SetKeyAsync(Guid userId, ChatProviderKind provider, string key, CancellationToken ct = default)
    {
        var existing = await FindAsync(userId, provider, ct);
        if (existing is null)
        {
            _db.ProviderKeys.Add(new ProviderKey
            {
                UserId = userId,
                Provider = provider.ToString(),
                Protected = _secrets.Protect(key),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Protected = _secrets.Protect(key);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveKeyAsync(Guid userId, ChatProviderKind provider, CancellationToken ct = default)
    {
        var existing = await FindAsync(userId, provider, ct);
        if (existing is null) return false;
        _db.ProviderKeys.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
