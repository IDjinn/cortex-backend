using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly ICurrentUser _me;
    private readonly IConversationService _svc;
    private readonly IMemoryService _memories;

    public ConversationsController(ICurrentUser me, IConversationService svc, IMemoryService memories)
    {
        _me = me;
        _svc = svc;
        _memories = memories;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await _svc.ListAsync(_me.UserId, ct);
        return Ok(list.Select(x => ToResponse(x.Conv, x.MessageCount)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var conv = await _svc.GetAsync(_me.UserId, id, ct);
        if (conv is null) return NotFound();
        return Ok(new ConversationDetailResponse(
            conv.Id, conv.Title, conv.Provider, conv.Model, conv.Pinned,
            conv.CreatedAt, conv.UpdatedAt,
            conv.Messages.Select(m => new MessageResponse(
                m.Id, m.Role, m.Content, m.Model, m.TokensIn, m.TokensOut, m.Error, m.CreatedAt, m.Cost, m.Reasoning)).ToList(),
            conv.FallbackProvider, conv.FallbackModel, conv.ProjectId));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConversationRequest req, CancellationToken ct)
    {
        var conv = await _svc.CreateAsync(_me.UserId, req.Title, req.Provider, req.Model, req.ProjectId, ct);
        return CreatedAtAction(nameof(Get), new { id = conv.Id }, ToResponse(conv));
    }

    /// <summary>Guest → account migration: imports on-device conversations server-side.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportConversationsRequest req, CancellationToken ct)
    {
        if ((req.Conversations is null || req.Conversations.Count == 0)
            && (req.Memories is null || req.Memories.Count == 0))
            return BadRequest(new ErrorDetail("Nothing to import"));
        var imported = await _svc.ImportAsync(_me.UserId, req.Conversations ?? [], ct);
        var importedMemories = await _memories.ImportAsync(_me.UserId, req.Memories ?? [], ct);
        return Ok(new ImportResultResponse(imported + importedMemories));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConversationRequest req, CancellationToken ct)
    {
        var ok = await _svc.UpdateAsync(_me.UserId, id, req.Title, req.Pinned, req.Provider, req.Model, req.FallbackProvider, req.FallbackModel, req.ProjectId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_me.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    private static ConversationResponse ToResponse(Conversation c, int messageCount = 0) => new(
        c.Id, c.Title, c.Provider, c.Model, c.Pinned, c.CreatedAt, c.UpdatedAt, messageCount,
        c.FallbackProvider, c.FallbackModel, c.ProjectId);
}
