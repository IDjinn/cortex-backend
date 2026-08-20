namespace Cortex.Core.Objects;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    public string? Model { get; set; }
    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }

    /// <summary>Chain-of-thought produced by reasoning models; null for plain answers.</summary>
    public string? Reasoning { get; set; }

    /// <summary>USD cost of the turn (tokens × model price); null for local/free models.</summary>
    public decimal? Cost { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
