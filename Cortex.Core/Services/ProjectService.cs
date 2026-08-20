using Cortex.Core.Data;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Services;

public interface IProjectService
{
    Task<List<ProjectResponse>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<Project> CreateAsync(Guid userId, string name, Guid? parentId, CancellationToken ct = default);
    Task<Project> RenameAsync(Guid userId, Guid id, string name, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<bool> AnyAsync(Guid userId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// Workspace projects: a root project holds folders (2 levels, enforced here),
/// conversations file into either via Conversation.ProjectId. Deleting a
/// project unfiles its conversations (SetNull) instead of deleting them.
/// </summary>
public class ProjectService : IProjectService
{
    private readonly AppDbContext _db;

    public ProjectService(AppDbContext db) => _db = db;

    public async Task<List<ProjectResponse>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var projects = await _db.Projects
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        var counts = await _db.Conversations
            .Where(c => c.UserId == userId && c.ProjectId != null)
            .GroupBy(c => c.ProjectId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return projects
            .Select(p => new ProjectResponse(
                p.Id, p.ParentId, p.Name, counts.GetValueOrDefault(p.Id), p.CreatedAt, p.UpdatedAt))
            .ToList();
    }

    public async Task<Project> CreateAsync(Guid userId, string name, Guid? parentId, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("Name is required");

        if (parentId is not null)
        {
            var parent = await _db.Projects.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == parentId, ct);
            if (parent is null) throw new InvalidOperationException("Parent project not found");
            if (parent.ParentId is not null) throw new InvalidOperationException("Folders cannot nest");
        }

        var project = new Project { UserId = userId, Name = trimmed, ParentId = parentId };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project> RenameAsync(Guid userId, Guid id, string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("Name is required");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == id, ct);
        if (project is null) throw new InvalidOperationException("Project not found");

        project.Name = trimmed;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == id, ct);
        if (project is null) return false;
        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> AnyAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        _db.Projects.AnyAsync(p => p.UserId == userId && p.Id == id, ct);
}
