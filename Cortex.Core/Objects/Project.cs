namespace Cortex.Core.Objects;

/// <summary>
/// A workspace project. Two levels: a root project (ParentId null) holds
/// folders (children); folders never nest further. Conversations file into
/// either a project or one of its folders via Conversation.ProjectId.
/// Deleting a project unfiles its conversations (never deletes them).
/// </summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Null for root projects; set for folders (parent must be a root).</summary>
    public Guid? ParentId { get; set; }
    public Project? Parent { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
