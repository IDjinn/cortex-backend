namespace Cortex.Core.Objects;

/// <summary>
/// A persistent fact about the user injected into the prompt as context.
/// Scoped: Global (all conversations), Conversation (a single conversation);
/// Project is reserved until the Project entity exists.
/// </summary>
public class Memory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public MemoryScope Scope { get; set; } = MemoryScope.Global;

    /// <summary>Required when Scope == Conversation; null otherwise.</summary>
    public Guid? ConversationId { get; set; }
    public Conversation? Conversation { get; set; }

    /// <summary>Manual (user-written) or Extracted (proposed by the assistant and confirmed).</summary>
    public MemorySource Source { get; set; } = MemorySource.Manual;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
