using Cortex.Core.Data;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Services;

public interface IMemoryService
{
    Task<List<Memory>> ListAsync(Guid userId, MemoryScope? scope, Guid? conversationId, Guid? projectId, CancellationToken ct = default);
    Task<Memory> CreateAsync(Guid userId, MemoryScope scope, Guid? conversationId, Guid? projectId, string content, MemorySource source, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid userId, Guid id, string content, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<int> BulkDeleteAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
    /// <summary>Bulk clear scoped to a filter (scope / project / conversation); never "everything".</summary>
    Task<int> ClearAsync(Guid userId, MemoryScope? scope, Guid? conversationId, Guid? projectId, CancellationToken ct = default);
    /// <summary>
    /// Memories relevant to a conversation: global scope, the conversation's
    /// own and the conversation's project chain (folder + root project),
    /// newest first, within a top-K / max-chars budget. Deterministic for
    /// now — semantic ranking lands with the embeddings epic.
    /// </summary>
    Task<List<Memory>> RelevantAsync(Guid userId, Guid conversationId, int topK, int maxChars, CancellationToken ct = default);
    /// <summary>Guest → account migration: on-device memories become global rows.</summary>
    Task<int> ImportAsync(Guid userId, IReadOnlyList<ImportMemoryDto> memories, CancellationToken ct = default);
}

public class MemoryService : IMemoryService
{
    private readonly AppDbContext _db;

    public MemoryService(AppDbContext db) => _db = db;

    public Task<List<Memory>> ListAsync(Guid userId, MemoryScope? scope, Guid? conversationId, Guid? projectId, CancellationToken ct = default)
    {
        var q = _db.Memories.AsNoTracking().Where(m => m.UserId == userId);
        if (scope is not null) q = q.Where(m => m.Scope == scope);
        if (conversationId is not null) q = q.Where(m => m.ConversationId == conversationId);
        if (projectId is not null) q = q.Where(m => m.ProjectId == projectId);
        return q.OrderByDescending(m => m.UpdatedAt).ToListAsync(ct);
    }

    public async Task<Memory> CreateAsync(Guid userId, MemoryScope scope, Guid? conversationId, Guid? projectId, string content, MemorySource source, CancellationToken ct = default)
    {
        var memory = new Memory
        {
            UserId = userId,
            Scope = scope,
            ConversationId = scope == MemoryScope.Conversation ? conversationId : null,
            ProjectId = scope == MemoryScope.Project ? projectId : null,
            Source = source,
            Content = content.Trim()
        };
        _db.Memories.Add(memory);
        await _db.SaveChangesAsync(ct);
        return memory;
    }

    public async Task<bool> UpdateAsync(Guid userId, Guid id, string content, CancellationToken ct = default)
    {
        var memory = await _db.Memories.FirstOrDefaultAsync(m => m.UserId == userId && m.Id == id, ct);
        if (memory is null) return false;
        memory.Content = content.Trim();
        memory.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var memory = await _db.Memories.FirstOrDefaultAsync(m => m.UserId == userId && m.Id == id, ct);
        if (memory is null) return false;
        _db.Memories.Remove(memory);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> BulkDeleteAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        return await _db.Memories
            .Where(m => m.UserId == userId && ids.Contains(m.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> ClearAsync(Guid userId, MemoryScope? scope, Guid? conversationId, Guid? projectId, CancellationToken ct = default)
    {
        var q = _db.Memories.Where(m => m.UserId == userId);
        if (scope is not null) q = q.Where(m => m.Scope == scope);
        if (conversationId is not null) q = q.Where(m => m.ConversationId == conversationId);
        if (projectId is not null) q = q.Where(m => m.ProjectId == projectId);
        return await q.ExecuteDeleteAsync(ct);
    }

    public async Task<List<Memory>> RelevantAsync(Guid userId, Guid conversationId, int topK, int maxChars, CancellationToken ct = default)
    {
        // Conversation's project chain (folder + its root project — 2 levels max).
        var chain = await _db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .Select(c => new { c.ProjectId, ParentId = c.Project != null ? c.Project.ParentId : null })
            .FirstOrDefaultAsync(ct);
        var projectIds = new[] { chain?.ProjectId, chain?.ParentId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        var candidates = await _db.Memories.AsNoTracking()
            .Where(m => m.UserId == userId
                && (m.Scope == MemoryScope.Global
                    || m.ConversationId == conversationId
                    || (m.ProjectId != null && projectIds.Contains(m.ProjectId.Value))))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(topK * 2)
            .ToListAsync(ct);

        var picked = new List<Memory>(topK);
        var used = 0;
        foreach (var m in candidates)
        {
            if (picked.Count >= topK) break;
            if (used + m.Content.Length > maxChars && picked.Count > 0) break;
            picked.Add(m);
            used += m.Content.Length;
        }
        return picked;
    }

    public async Task<int> ImportAsync(Guid userId, IReadOnlyList<ImportMemoryDto> memories, CancellationToken ct = default)
    {
        var imported = 0;
        foreach (var m in memories.Take(500))
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            _db.Memories.Add(new Memory
            {
                UserId = userId,
                Scope = MemoryScope.Global,
                Source = MemorySource.Manual,
                Content = m.Content.Trim()
            });
            imported++;
        }
        if (imported > 0) await _db.SaveChangesAsync(ct);
        return imported;
    }
}
