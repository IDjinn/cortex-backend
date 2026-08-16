using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/models")]
public class ModelsController : ControllerBase
{
    private readonly IModelService _models;

    public ModelsController(IModelService models) => _models = models;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string provider,
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ChatProviderKind>(provider, ignoreCase: true, out var p))
            return BadRequest(new ErrorDetail("Invalid provider", "Use 'openrouter' or 'ollama'"));

        var list = await _models.ListAsync(p, refresh, ct);
        return Ok(list.Select(m => new ModelResponse(
            m.Id, m.Name, m.Description, m.ContextLength, m.PromptPrice, m.CompletionPrice)));
    }
}
