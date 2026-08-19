using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Providers;

/// <summary>Direct OpenAI connector (api.openai.com).</summary>
public sealed class OpenAiProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "openai";

    public OpenAiProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.OpenAI, "OpenAI")
    {
    }

    public override ChatProviderKind Kind => ChatProviderKind.OpenAI;
}

/// <summary>Direct xAI connector (Grok models, api.x.ai).</summary>
public sealed class XaiProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "xai";

    public XaiProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.Xai, "xAI")
    {
    }

    public override ChatProviderKind Kind => ChatProviderKind.Xai;
}

/// <summary>Direct Mistral connector (api.mistral.ai).</summary>
public sealed class MistralProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "mistral";

    public MistralProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.Mistral, "Mistral")
    {
    }

    public override ChatProviderKind Kind => ChatProviderKind.Mistral;
}

/// <summary>Direct DeepSeek connector (api.deepseek.com).</summary>
public sealed class DeepSeekProvider : OpenAiCompatibleProvider
{
    public const string HttpClientName = "deepseek";

    public DeepSeekProvider(IHttpClientFactory factory, IOptions<ProviderOptions> opts)
        : base(factory, HttpClientName, opts.Value.DeepSeek, "DeepSeek")
    {
    }

    public override ChatProviderKind Kind => ChatProviderKind.DeepSeek;
}
