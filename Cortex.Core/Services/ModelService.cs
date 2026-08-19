using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Microsoft.Extensions.Caching.Memory;

namespace Cortex.Core.Services;

public interface IModelService
{
    /// <summary>
    /// Lists models for a provider. When <paramref name="context"/> carries a BYOK
    /// key or custom base URL the call bypasses the cache (per-user data must not
    /// leak through the shared entry).
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> ListAsync(ChatProviderKind provider, ProviderCallContext? context = null, bool refresh = false, CancellationToken ct = default);
}

public class ModelService : IModelService
{
    private readonly IProviderFactory _factory;
    private readonly IMemoryCache _cache;

    public ModelService(IProviderFactory factory, IMemoryCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(ChatProviderKind provider, ProviderCallContext? context = null, bool refresh = false, CancellationToken ct = default)
    {
        var hasContext = !string.IsNullOrWhiteSpace(context?.ApiKey) || !string.IsNullOrWhiteSpace(context?.BaseUrl);
        if (hasContext)
            return await _factory.Get(provider).ListModelsAsync(context, ct);

        var key = "models:" + provider;
        if (refresh || !_cache.TryGetValue(key, out IReadOnlyList<ModelInfo>? cached) || cached is null)
        {
            cached = await _factory.Get(provider).ListModelsAsync(ct: ct);
            _cache.Set(key, cached, TimeSpan.FromMinutes(10));
        }
        return cached;
    }
}
