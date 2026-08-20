using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/memories")]
public class MemoriesController : ControllerBase
{
    private const int BulkCap = 500;

    private readonly ICurrentUser _me;
    private readonly IMemoryService _svc;
    private readonly IProjectService _projects;

    public MemoriesController(ICurrentUser me, IMemoryService svc, IProjectService projects)
    {
        _me = me;
        _svc = svc;
        _projects = projects;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] MemoryScope? scope, [FromQuery] Guid? conversationId, [FromQuery] Guid? projectId, CancellationToken ct)
    {
        var list = await _svc.ListAsync(_me.UserId, scope, conversationId, projectId, ct);
        return Ok(list.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMemoryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new ErrorDetail("Content is required"));
        if (req.Scope == MemoryScope.Conversation && req.ConversationId is null)
            return BadRequest(new ErrorDetail("conversationId is required for the Conversation scope"));
        if (req.Scope == MemoryScope.Project)
        {
            if (req.ProjectId is null)
                return BadRequest(new ErrorDetail("projectId is required for the Project scope"));
            if (!await _projects.AnyAsync(_me.UserId, req.ProjectId.Value, ct))
                return BadRequest(new ErrorDetail("Unknown project"));
        }
        var memory = await _svc.CreateAsync(_me.UserId, req.Scope, req.ConversationId, req.ProjectId, req.Content, MemorySource.Manual, ct);
        return CreatedAtAction(nameof(List), ToResponse(memory));
    }

    [HttpPost("bulk-delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteMemoriesRequest req, CancellationToken ct)
    {
        if (req.Ids is null || req.Ids.Count == 0)
            return BadRequest(new ErrorDetail("ids is required"));
        if (req.Ids.Count > BulkCap)
            return BadRequest(new ErrorDetail($"ids is limited to {BulkCap} items"));
        var deleted = await _svc.BulkDeleteAsync(_me.UserId, req.Ids, ct);
        return Ok(new BulkMemoryResultResponse(deleted));
    }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear([FromBody] ClearMemoriesRequest req, CancellationToken ct)
    {
        if (req.Scope is null && req.ProjectId is null && req.ConversationId is null)
            return BadRequest(new ErrorDetail("At least one of scope, projectId or conversationId is required"));
        var deleted = await _svc.ClearAsync(_me.UserId, req.Scope, req.ConversationId, req.ProjectId, ct);
        return Ok(new BulkMemoryResultResponse(deleted));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMemoryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new ErrorDetail("Content is required"));
        var ok = await _svc.UpdateAsync(_me.UserId, id, req.Content, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_me.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    private static MemoryResponse ToResponse(Memory m) => new(
        m.Id, m.Scope, m.ConversationId, m.ProjectId, m.Source, m.Content, m.CreatedAt, m.UpdatedAt);
}
