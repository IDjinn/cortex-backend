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
    private readonly ICurrentUser _me;
    private readonly IMemoryService _svc;

    public MemoriesController(ICurrentUser me, IMemoryService svc)
    {
        _me = me;
        _svc = svc;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] MemoryScope? scope, [FromQuery] Guid? conversationId, CancellationToken ct)
    {
        var list = await _svc.ListAsync(_me.UserId, scope, conversationId, ct);
        return Ok(list.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMemoryRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new ErrorDetail("Content is required"));
        if (req.Scope == MemoryScope.Conversation && req.ConversationId is null)
            return BadRequest(new ErrorDetail("conversationId is required for the Conversation scope"));
        var memory = await _svc.CreateAsync(_me.UserId, req.Scope, req.ConversationId, req.Content, MemorySource.Manual, ct);
        return CreatedAtAction(nameof(List), ToResponse(memory));
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
        m.Id, m.Scope, m.ConversationId, m.Source, m.Content, m.CreatedAt, m.UpdatedAt);
}
