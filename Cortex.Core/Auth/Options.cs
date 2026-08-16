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

    public class ProviderEndpoint
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string? ApiKey { get; set; }
    }
}

public class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
