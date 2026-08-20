using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

/// <summary>
/// Server-side abstraction over LLM providers. Implementations normalize each
/// provider's native streaming and tool-call formats into <see cref="ChatChunk"/>.
/// </summary>
public interface IProvider
{
    ChatProviderKind Kind { get; }

    /// <summary>List of models exposed by the provider.</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ProviderCallContext? context = null, CancellationToken ct = default);

    /// <summary>
    /// Streams completion chunks for the given message history. Tokens arrive as
    /// deltas; tool calls arrive accumulated (one chunk per completed call).
    /// </summary>
    IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequestPayload payload,
        ProviderCallContext? context = null,
        CancellationToken ct = default);
}

/// <summary>
/// Per-call overrides: BYOK key proxied in a request header and a caller-supplied
/// base URL for local endpoints (LM Studio / llama.cpp on another host).
/// Falls back to server configuration when a member is null.
/// </summary>
public record ProviderCallContext(string? ApiKey = null, string? BaseUrl = null);

public record ModelInfo(
    string Id,
    string Name,
    string? Description,
    int? ContextLength,
    decimal? PromptPrice,
    decimal? CompletionPrice,
    bool? SupportsTools = null,
    bool? SupportsVision = null);

/// <summary>Provider-agnostic tool definition; <see cref="ParametersJson"/> is a JSON Schema object.</summary>
public record ToolDefinition(string Name, string Description, string? ParametersJson);

/// <summary>A completed tool call returned by the model; <see cref="ArgumentsJson"/> is the raw arguments JSON.</summary>
public record ToolCallPayload(string Id, string Name, string ArgumentsJson);

public record ChatRequestPayload(
    string Model,
    IReadOnlyList<ChatMessagePayload> Messages,
    double? Temperature = null,
    int? MaxTokens = null,
    IReadOnlyList<ToolDefinition>? Tools = null);

public record ChatMessagePayload(
    MessageRole Role,
    string Content,
    IReadOnlyList<ToolCallPayload>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null);

public abstract record ChatChunk
{
    public sealed record Token(string Text) : ChatChunk;
    /// <summary>Chain-of-thought delta (reasoning models) — displayed separately from the answer.</summary>
    public sealed record Reasoning(string Text) : ChatChunk;
    public sealed record ToolCall(string Id, string Name, string ArgumentsJson) : ChatChunk;
    public sealed record Usage(int? PromptTokens, int? CompletionTokens) : ChatChunk;
    public sealed record Done() : ChatChunk;
    public sealed record Error(string Message) : ChatChunk;
}

public interface IProviderFactory
{
    IProvider Get(ChatProviderKind kind);
}
