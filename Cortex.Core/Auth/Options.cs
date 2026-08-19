using Cortex.Core.Objects;

namespace Cortex.Core.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "cortex";
    public string Audience { get; set; } = "cortex-app";
    public int AccessTokenMinutes { get; set; } = 15;
    public string SigningKey { get; set; } = string.Empty;
}

public class OAuthProviderOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string UserInformationEndpoint { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public class OAuthOptions
{
    public OAuthProviderOptions Google { get; set; } = new();
    public OAuthProviderOptions GitHub { get; set; } = new();
}

public class ProviderOptions
{
    public ProviderEndpoint OpenRouter { get; set; } = new();
    public ProviderEndpoint Ollama { get; set; } = new();
    public ProviderEndpoint LmStudio { get; set; } = new();
    public ProviderEndpoint OpenAI { get; set; } = new();
    public ProviderEndpoint Anthropic { get; set; } = new();
    public ProviderEndpoint Gemini { get; set; } = new();
    public ProviderEndpoint Xai { get; set; } = new();
    public ProviderEndpoint Mistral { get; set; } = new();
    public ProviderEndpoint DeepSeek { get; set; } = new();

    public ProviderEndpoint For(ChatProviderKind kind) => kind switch
    {
        ChatProviderKind.OpenRouter => OpenRouter,
        ChatProviderKind.Ollama => Ollama,
        ChatProviderKind.LmStudio => LmStudio,
        ChatProviderKind.OpenAI => OpenAI,
        ChatProviderKind.Anthropic => Anthropic,
        ChatProviderKind.Gemini => Gemini,
        ChatProviderKind.Xai => Xai,
        ChatProviderKind.Mistral => Mistral,
        ChatProviderKind.DeepSeek => DeepSeek,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public class ProviderEndpoint
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }

        /// <summary>True when ApiKey holds a real secret (not the user-secrets placeholder).</summary>
        public bool KeyConfigured =>
            !string.IsNullOrEmpty(ApiKey) && !ApiKey!.StartsWith("REPLACE_", StringComparison.Ordinal);

        /// <summary>Fallback when a conversation/request carries no model.
        /// Matched against model ids ignoring case, with ":latest" implied
        /// (e.g. "gemma3" matches "gemma3:latest").</summary>
        public string? DefaultModel { get; set; }
    }
}

public class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

/// <summary>Chat behaviour knobs. <see cref="SystemPromptTemplate"/> may use
/// the {language} placeholder, replaced per request with the caller's locale.</summary>
public class ChatOptions
{
    public string? SystemPromptTemplate { get; set; }
}
