using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

public sealed class LmStudioProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "lmstudio";

    public LmStudioProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.LmStudio, "LM Studio")
        // Local inference can be slow on large prompts.
        => Http.Timeout = TimeSpan.FromMinutes(10);

    public override ChatProviderKind Kind => ChatProviderKind.LmStudio;

    /// <summary>
    /// Uses LM Studio's native listing (host-root "/api/v1/models", i.e. outside the
    /// OpenAI /v1 base) so non-chat models (embedders) can be filtered out by type.
    /// </summary>
    public override async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var url = new Uri(Http.BaseAddress!, "/api/v1/models");
        var res = await Http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var list = new List<ModelInfo>();
        foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            if (m.TryGetProperty("type", out var t) && t.GetString() != "llm")
                continue;

            var id = m.GetProperty("key").GetString()!;
            list.Add(new ModelInfo(
                Id: id,
                Name: m.TryGetProperty("display_name", out var dn) && !string.IsNullOrEmpty(dn.GetString()) ? dn.GetString()! : id,
                Description: m.TryGetProperty("params_string", out var ps) ? ps.GetString() : null,
                ContextLength: m.TryGetProperty("max_context_length", out var mc) && mc.TryGetInt32(out var mcv) ? mcv : null,
                PromptPrice: null,
                CompletionPrice: null));
        }
        return list;
    }
}
