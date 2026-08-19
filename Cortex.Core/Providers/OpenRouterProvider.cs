using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

public sealed class OpenRouterProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "openrouter";

    public OpenRouterProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.OpenRouter, "OpenRouter")
    {
        Http.DefaultRequestHeaders.Add("HTTP-Referer", "https://cortex.app");
        Http.DefaultRequestHeaders.Add("X-Title", "Cortex");
    }

    public override ChatProviderKind Kind => ChatProviderKind.OpenRouter;
}
