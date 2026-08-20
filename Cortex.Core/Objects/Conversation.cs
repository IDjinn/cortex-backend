namespace Cortex.Core.Objects;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Title { get; set; } = "Nova conversa";
    public ChatProviderKind Provider { get; set; } = ChatProviderKind.OpenRouter;
    public string Model { get; set; } = string.Empty;
    public bool Pinned { get; set; }

    /// <summary>Manual routing fallback — tried when the primary provider fails
    /// before emitting any token. Stored as ChatProviderKind string; null = disabled.</summary>
    public string? FallbackProvider { get; set; }
    public string? FallbackModel { get; set; }

    /// <summary>Workspace project (or folder) this conversation is filed under; null = unfiled.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
