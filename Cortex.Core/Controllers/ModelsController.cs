using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Providers;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/models")]
public class ModelsController : ControllerBase
{
    private readonly IModelService _models;
    private readonly ProviderOptions _providers;
    private readonly IProviderKeyStore _keyStore;

    public ModelsController(IModelService models, IOptions<ProviderOptions> providers, IProviderKeyStore keyStore)
    {
        _models = models;
        _providers = providers.Value;
        _keyStore = keyStore;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string provider,
        [FromQuery] bool refresh = false,
        [FromHeader(Name = "X-Provider-Key")] string? providerKey = null,
        [FromQuery] string? baseUrl = null,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ChatProviderKind>(provider, ignoreCase: true, out var p))
            return BadRequest(new ErrorDetail("Invalid provider", "Use one of: openrouter, ollama, lmstudio, openai, anthropic, gemini, xai, mistral, deepseek"));

        // Custom base URLs are only meaningful for local endpoints (LM Studio /
        // llama.cpp / Ollama on another host) — never override a cloud provider's URL.
        if (!string.IsNullOrWhiteSpace(baseUrl) && p is not (ChatProviderKind.Ollama or ChatProviderKind.LmStudio))
            return BadRequest(new ErrorDetail("baseUrl is only allowed for local providers"));

        // BYOK resolution mirrors chat: header key > user vault (when authenticated) > server key.
        if (string.IsNullOrWhiteSpace(providerKey) && User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            providerKey = await _keyStore.GetKeyAsync(userId, p, ct);
        }

        var context = new ProviderCallContext(
            ApiKey: providerKey,
            BaseUrl: baseUrl);
        var defaultModel = _providers.For(p).DefaultModel;

        // Local endpoints (Ollama, LM Studio) are routinely offline — an unreachable
        // or failing provider must degrade to a structured error, not an unhandled 500.
        IReadOnlyList<ModelInfo> list;
        try
        {
            list = await _models.ListAsync(p, context, refresh, ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ErrorDetail("Provider unreachable", $"Failed to list models from {p.ToString().ToLowerInvariant()}: {ex.Message}"));
        }

        return Ok(list.Select(m => new ModelResponse(
            m.Id, m.Name, m.Description, m.ContextLength, m.PromptPrice, m.CompletionPrice,
            IsDefault(m.Id, defaultModel),
            m.SupportsTools, m.SupportsVision)));
    }

    private static bool IsDefault(string id, string? defaultModel) =>
        defaultModel is not null
            && (id.Equals(defaultModel, StringComparison.OrdinalIgnoreCase)
            || id.Equals(defaultModel + ":latest", StringComparison.OrdinalIgnoreCase));
}
