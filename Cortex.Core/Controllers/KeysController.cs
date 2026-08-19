using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

/// <summary>
/// User BYOK vault (encrypted at rest with Data Protection). GET never returns
/// key material — only which providers have a stored key.
/// </summary>
[ApiController]
[Authorize]
[Route("api/keys")]
public class KeysController : ControllerBase
{
    private readonly IProviderKeyStore _keys;
    private readonly ICurrentUser _me;

    public KeysController(IProviderKeyStore keys, ICurrentUser me)
    {
        _keys = keys;
        _me = me;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var keys = await _keys.ListAsync(_me.UserId, ct);
        return Ok(keys.Select(k => new ProviderKeyResponse(k.Item1, k.Item2)));
    }

    [HttpPut("{provider}")]
    public async Task<IActionResult> Save(string provider, [FromBody] SaveProviderKeyRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ChatProviderKind>(provider, ignoreCase: true, out var kind) ||
            kind is ChatProviderKind.Ollama or ChatProviderKind.LmStudio)
            return BadRequest(new ErrorDetail("Invalid provider", "Cloud providers only"));

        if (string.IsNullOrWhiteSpace(req.Key))
            return BadRequest(new ErrorDetail("Key cannot be empty"));

        await _keys.SetKeyAsync(_me.UserId, kind, req.Key.Trim(), ct);
        return NoContent();
    }

    [HttpDelete("{provider}")]
    public async Task<IActionResult> Remove(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<ChatProviderKind>(provider, ignoreCase: true, out var kind))
            return BadRequest(new ErrorDetail("Invalid provider"));

        var removed = await _keys.RemoveKeyAsync(_me.UserId, kind, ct);
        return removed ? NoContent() : NotFound();
    }
}
