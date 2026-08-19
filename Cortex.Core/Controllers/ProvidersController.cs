using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

/// <summary>
/// Static provider catalog — tells clients which connectors exist, which need a
/// key and whether the server already holds one (so the picker can group them).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/providers")]
public class ProvidersController : ControllerBase
{
    private static readonly (ChatProviderKind Kind, string Name, bool IsLocal)[] Catalog =
    {
        (ChatProviderKind.LmStudio, "LM Studio", true),
        (ChatProviderKind.Ollama, "Ollama", true),
        (ChatProviderKind.OpenAI, "OpenAI", false),
        (ChatProviderKind.Anthropic, "Anthropic", false),
        (ChatProviderKind.Gemini, "Google Gemini", false),
        (ChatProviderKind.Xai, "xAI Grok", false),
        (ChatProviderKind.Mistral, "Mistral", false),
        (ChatProviderKind.DeepSeek, "DeepSeek", false),
        (ChatProviderKind.OpenRouter, "OpenRouter", false),
    };

    private readonly Auth.ProviderOptions _providers;

    public ProvidersController(Microsoft.Extensions.Options.IOptions<Auth.ProviderOptions> providers)
    {
        _providers = providers.Value;
    }

    [HttpGet]
    public IActionResult List()
    {
        return Ok(Catalog.Select(e => new ProviderResponse(
            e.Kind,
            e.Name,
            e.IsLocal,
            RequiresKey: !e.IsLocal,
            ServerKeyConfigured: !e.IsLocal && _providers.For(e.Kind).KeyConfigured)));
    }
}
