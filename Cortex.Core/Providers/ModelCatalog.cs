using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

public sealed record ModelMetadata(
    int? ContextLength,
    decimal? PromptPrice,
    decimal? CompletionPrice,
    bool SupportsTools,
    bool SupportsVision);

/// <summary>
/// Static capability/pricing table for direct connectors whose /models endpoints
/// don't expose metadata (OpenRouter serves its own live metadata). Prices are
/// USD per 1M tokens — review periodically.
/// </summary>
public static class ModelCatalog
{
    public static ModelMetadata? Find(ChatProviderKind kind, string id)
    {
        if (!Tables.TryGetValue(kind, out var table))
            return null;
        // default(Entry) carries a null Metadata — no match.
        return table.FirstOrDefault(e => e.Matches(id)).Metadata;
    }

    private readonly record struct Entry(string Prefix, ModelMetadata Metadata);

    private static bool Matches(this Entry e, string id) =>
        id.StartsWith(e.Prefix, StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<ChatProviderKind, Entry[]> Tables = new()
    {
        [ChatProviderKind.OpenAI] =
        [
            new("gpt-5.2", new(400_000, 1.25m, 10m, true, true)),
            new("gpt-5.1", new(400_000, 1.25m, 10m, true, true)),
            new("gpt-5-nano", new(400_000, 0.05m, 0.40m, true, true)),
            new("gpt-5-mini", new(400_000, 0.25m, 2m, true, true)),
            new("gpt-5", new(400_000, 1.25m, 10m, true, true)),
            new("gpt-4.1-nano", new(1_000_000, 0.10m, 0.40m, true, true)),
            new("gpt-4.1-mini", new(1_000_000, 0.40m, 1.60m, true, true)),
            new("gpt-4.1", new(1_000_000, 2m, 8m, true, true)),
            new("gpt-4o-mini", new(128_000, 0.15m, 0.60m, true, true)),
            new("gpt-4o", new(128_000, 2.50m, 10m, true, true)),
            new("o4-mini", new(200_000, 1.10m, 4.40m, true, true)),
            new("o3", new(200_000, 2m, 8m, true, true)),
        ],
        [ChatProviderKind.Anthropic] =
        [
            new("claude-opus-4", new(200_000, 5m, 25m, true, true)),
            new("claude-opus", new(200_000, 5m, 25m, true, true)),
            new("claude-sonnet-4", new(200_000, 3m, 15m, true, true)),
            new("claude-sonnet", new(200_000, 3m, 15m, true, true)),
            new("claude-haiku-4", new(200_000, 1m, 5m, true, true)),
            new("claude-haiku", new(200_000, 1m, 5m, true, true)),
            new("claude-3-7-sonnet", new(200_000, 3m, 15m, true, true)),
            new("claude-3-5-sonnet", new(200_000, 3m, 15m, true, true)),
            new("claude-3-5-haiku", new(200_000, 0.80m, 4m, true, true)),
        ],
        [ChatProviderKind.Gemini] =
        [
            new("gemini-3-pro", new(1_000_000, 2m, 12m, true, true)),
            new("gemini-3-flash", new(1_000_000, 0.30m, 2.50m, true, true)),
            new("gemini-2.5-pro", new(1_000_000, 1.25m, 10m, true, true)),
            new("gemini-2.5-flash-lite", new(1_000_000, 0.10m, 0.40m, true, true)),
            new("gemini-2.5-flash", new(1_000_000, 0.30m, 2.50m, true, true)),
            new("gemini-2.0-flash", new(1_000_000, 0.10m, 0.40m, true, true)),
        ],
        [ChatProviderKind.Xai] =
        [
            new("grok-4-fast", new(2_000_000, 0.20m, 0.50m, true, true)),
            new("grok-4", new(256_000, 3m, 15m, true, true)),
            new("grok-3-mini", new(131_072, 0.30m, 0.50m, true, false)),
            new("grok-3", new(131_072, 3m, 15m, true, false)),
            new("grok-2-vision", new(32_768, 2m, 10m, false, true)),
        ],
        [ChatProviderKind.Mistral] =
        [
            new("pixtral-large", new(128_000, 2m, 6m, true, true)),
            new("pixtral", new(128_000, 0.15m, 0.15m, false, true)),
            new("mistral-large", new(128_000, 2m, 6m, true, false)),
            new("mistral-medium", new(128_000, 0.40m, 2m, true, false)),
            new("mistral-small", new(128_000, 0.10m, 0.30m, true, false)),
            new("magistral", new(128_000, 2m, 5m, true, false)),
            new("codestral", new(256_000, 0.30m, 0.90m, true, false)),
            new("open-mistral-nemo", new(128_000, 0.15m, 0.15m, true, false)),
        ],
        [ChatProviderKind.DeepSeek] =
        [
            new("deepseek-reasoner", new(128_000, 0.55m, 2.19m, true, false)),
            new("deepseek-chat", new(128_000, 0.27m, 1.10m, true, false)),
        ],
    };
}
