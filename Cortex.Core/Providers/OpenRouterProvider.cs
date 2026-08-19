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
    }

    public override ChatProviderKind Kind => ChatProviderKind.OpenRouter;

    protected override void OnClientCreated(HttpClient http)
    {
        http.DefaultRequestHeaders.Add("HTTP-Referer", "https://cortex.app");
        http.DefaultRequestHeaders.Add("X-Title", "Cortex");
    }
}
