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

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
