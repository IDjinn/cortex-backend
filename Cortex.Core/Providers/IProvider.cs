using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

/// <summary>
/// Server-side abstraction over LLM providers (OpenRouter, Ollama, future ones).
/// </summary>
public interface IProvider
{
    ChatProviderKind Kind { get; }

    /// <summary>List of models exposed by the provider.</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Streams completion chunks for the given message history. Each yielded string is
    /// a token fragment (delta content). The caller is responsible for assembling.
    /// </summary>
    IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequestPayload payload,
        CancellationToken ct = default);
}

public record ModelInfo(
    string Id,
    string Name,
    string? Description,
    int? ContextLength,
    decimal? PromptPrice,
    decimal? CompletionPrice);

public record ChatRequestPayload(
    string Model,
    IReadOnlyList<ChatMessagePayload> Messages,
    double? Temperature = null,
    int? MaxTokens = null);

public record ChatMessagePayload(MessageRole Role, string Content);

public abstract record ChatChunk
{
    public sealed record Token(string Text) : ChatChunk;
    public sealed record Usage(int? PromptTokens, int? CompletionTokens) : ChatChunk;
    public sealed record Done() : ChatChunk;
    public sealed record Error(string Message) : ChatChunk;
}

public interface IProviderFactory
{
    IProvider Get(ChatProviderKind kind);
    IProvider GetOpenRouter();
    IProvider GetOllama();
}
