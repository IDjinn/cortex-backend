using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Microsoft.Extensions.Caching.Memory;

namespace Cortex.Core.Services;

public interface IModelService
{
    Task<IReadOnlyList<ModelInfo>> ListAsync(ChatProviderKind provider, bool refresh = false, CancellationToken ct = default);
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

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(ChatProviderKind provider, bool refresh = false, CancellationToken ct = default)
    {
        var key = "models:" + provider;
        if (refresh || !_cache.TryGetValue(key, out IReadOnlyList<ModelInfo>? cached) || cached is null)
        {
            var p = _factory.Get(provider);
            cached = await p.ListModelsAsync(ct);
            _cache.Set(key, cached, TimeSpan.FromMinutes(10));
        }
        return cached;
    }
}
