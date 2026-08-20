using Cortex.Core.Objects;

namespace Cortex.Core.Dtos;

public record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    UserProfile User);

public record UserProfile(
    Guid Id,
    string Email,
    string? Name,
    string? AvatarUrl,
    AuthProvider Provider,
    DateTimeOffset CreatedAt);

public record RefreshRequest(string RefreshToken);

public record CreateConversationRequest(
    string? Title,
    ChatProviderKind Provider,
    string Model,
    Guid? ProjectId = null);

public record UpdateConversationRequest(
    string? Title,
    bool? Pinned,
    ChatProviderKind? Provider = null,
    string? Model = null,
    /// <summary>Empty string clears the fallback.</summary>
    string? FallbackProvider = null,
    string? FallbackModel = null,
    /// <summary>Empty string unfiles the conversation from its project/folder.</summary>
    string? ProjectId = null);

public record ConversationResponse(
    Guid Id,
    string Title,
    ChatProviderKind Provider,
    string Model,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? FallbackProvider = null,
    string? FallbackModel = null,
    Guid? ProjectId = null);

public record ConversationDetailResponse(
    Guid Id,
    string Title,
    ChatProviderKind Provider,
    string Model,
    bool Pinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<MessageResponse> Messages,
    string? FallbackProvider = null,
    string? FallbackModel = null,
    Guid? ProjectId = null);

public record MessageResponse(
    Guid Id,
    MessageRole Role,
    string Content,
    string? Model,
    int? TokensIn,
    int? TokensOut,
    string? Error,
    DateTimeOffset CreatedAt,
    decimal? CostUsd = null,
    string? Reasoning = null);

public record ChatRequest(
    Guid ConversationId,
    string Content,
    string? Locale = null);

public record AnonymousChatRequest(
    ChatProviderKind Provider,
    string? Model,
    List<AnonymousChatMessage> Messages,
    double? Temperature = null,
    int? MaxTokens = null,
    string? Locale = null,
    string? BaseUrl = null);

public record AnonymousChatMessage(MessageRole Role, string Content);

public record ModelResponse(
    string Id,
    string Name,
    string? Description,
    int? ContextLength,
    decimal? PromptPrice,
    decimal? CompletionPrice,
    bool IsDefault = false,
    bool? SupportsTools = null,
    bool? SupportsVision = null);

/// <summary>Provider catalog entry: availability and key requirements for the picker.</summary>
public record ProviderResponse(
    ChatProviderKind Kind,
    string Name,
    bool IsLocal,
    bool RequiresKey,
    bool ServerKeyConfigured);

public record ErrorDetail(string Error, string? Detail = null);

// ---- BYOK vault ----

public record SaveProviderKeyRequest(string Key);

public record ProviderKeyResponse(
    ChatProviderKind Provider,
    DateTimeOffset UpdatedAt);

// ---- Usage & cost ----

public record UsageResponse(
    ChatProviderKind Provider,
    int Requests,
    int TokensIn,
    int TokensOut,
    decimal? CostUsd);

// ---- Guest → account migration ----

public record ImportConversationsRequest(List<ImportConversationDto> Conversations, List<ImportMemoryDto>? Memories = null);

public record ImportConversationDto(
    string Title,
    ChatProviderKind Provider,
    string Model,
    bool Pinned,
    List<ImportMessageDto> Messages);

public record ImportMessageDto(
    MessageRole Role,
    string Content,
    string? Model,
    int? TokensIn,
    int? TokensOut,
    string? Error,
    DateTimeOffset? CreatedAt,
    decimal? CostUsd = null);

public record ImportResultResponse(int Imported);

// ---- Memories ----

public record CreateMemoryRequest(
    MemoryScope Scope,
    Guid? ConversationId,
    Guid? ProjectId,
    string Content);

public record UpdateMemoryRequest(string Content);

public record MemoryResponse(
    Guid Id,
    MemoryScope Scope,
    Guid? ConversationId,
    Guid? ProjectId,
    MemorySource Source,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record BulkDeleteMemoriesRequest(IReadOnlyList<Guid> Ids);

/// <summary>At least one filter is required — clear is never "everything".</summary>
public record ClearMemoriesRequest(MemoryScope? Scope, Guid? ProjectId, Guid? ConversationId);

public record BulkMemoryResultResponse(int Deleted);

public record ImportMemoryDto(string Content);

// ---- Projects ----

/// <summary>ParentId set creates a folder inside that root project.</summary>
public record CreateProjectRequest(string Name, Guid? ParentId = null);

public record UpdateProjectRequest(string Name);

public record ProjectResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    /// <summary>Conversations filed directly in this project/folder (folders not summed).</summary>
    int ConversationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
