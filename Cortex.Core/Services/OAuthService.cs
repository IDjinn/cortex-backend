using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cortex.Core.Auth;
using Cortex.Core.Objects;
using Microsoft.Extensions.Options;

namespace Cortex.Core.Services;

public interface IOAuthService
{
    /// <summary>
    /// Builds the external authorize URL the app/web should redirect to.
    /// </summary>
    (string url, string state) BuildAuthorizeUrl(AuthProvider provider, string redirectUri);

    /// <summary>
    /// Exchanges the OAuth code for an access token at the provider and returns the user identity.
    /// </summary>
    Task<ExternalIdentity> ExchangeCodeAsync(AuthProvider provider, string code, string redirectUri, CancellationToken ct = default);
}

public record ExternalIdentity(
    AuthProvider Provider,
    string ProviderUid,
    string Email,
    string? Name,
    string? AvatarUrl);

public class OAuthService : IOAuthService
{
    private readonly IHttpClientFactory _http;
    private readonly OAuthOptions _opts;
    private static readonly string[] StateBox = new string[1];

    public OAuthService(IHttpClientFactory http, IOptions<OAuthOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    private static string RandomToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public (string url, string state) BuildAuthorizeUrl(AuthProvider provider, string redirectUri)
    {
        var opt = GetOptions(provider);
        var state = RandomToken();
        var scopes = string.Join(" ", opt.Scopes);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = opt.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = scopes
        };
        if (provider == AuthProvider.GitHub) query["allow_signup"] = "true";

        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value ?? "")}"));
        return ($"{opt.AuthorizationEndpoint}?{qs}", state);
    }

    public async Task<ExternalIdentity> ExchangeCodeAsync(AuthProvider provider, string code, string redirectUri, CancellationToken ct = default)
    {
        var opt = GetOptions(provider);
        var client = _http.CreateClient("oauth-" + provider.ToString().ToLowerInvariant());

        var tokenReqParams = new Dictionary<string, string>
        {
            ["client_id"] = opt.ClientId,
            ["client_secret"] = opt.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        var tokenReq = new HttpRequestMessage(HttpMethod.Post, opt.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(tokenReqParams)
        };
        if (provider == AuthProvider.GitHub)
        {
            tokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        using var tokenRes = await client.SendAsync(tokenReq, ct);
        if (!tokenRes.IsSuccessStatusCode)
        {
            // The provider returns JSON with an `error` / `error_description`
            // (GitHub sometimes uses `error`/`error_uri`). Surface it instead
            // of throwing on the bare status code so the caller can react.
            var body = await tokenRes.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Token endpoint returned {(int)tokenRes.StatusCode} {tokenRes.StatusCode}: {body}");
        }

        var tokenJson = await tokenRes.Content.ReadAsStringAsync(ct);
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken = tokenDoc.RootElement.GetProperty(
            provider == AuthProvider.GitHub ? "access_token" : "access_token").GetString()
            ?? throw new InvalidOperationException("Missing access_token from provider");

        // Fetch profile
        var profileReq = new HttpRequestMessage(HttpMethod.Get, opt.UserInformationEndpoint);
        if (provider == AuthProvider.GitHub)
        {
            profileReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            profileReq.Headers.UserAgent.ParseAdd("Cortex/1.0");
        }
        else
        {
            profileReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        using var profileRes = await client.SendAsync(profileReq, ct);
        profileRes.EnsureSuccessStatusCode();

        var profileJson = await profileRes.Content.ReadAsStringAsync(ct);
        using var profile = JsonDocument.Parse(profileJson);
        var root = profile.RootElement;

        return provider switch
        {
            AuthProvider.Google => new ExternalIdentity(
                AuthProvider.Google,
                root.GetProperty("id").GetString()!,
                root.GetProperty("email").GetString()!,
                root.TryGetProperty("name", out var n) ? n.GetString() : null,
                root.TryGetProperty("picture", out var p) ? p.GetString() : null),
            AuthProvider.GitHub => new ExternalIdentity(
                AuthProvider.GitHub,
                root.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture),
                root.GetProperty("email").GetString() ?? await GetPrimaryEmailGithubAsync(client, accessToken, ct),
                root.TryGetProperty("name", out var n) ? n.GetString() : root.GetProperty("login").GetString(),
                root.TryGetProperty("avatar_url", out var a) ? a.GetString() : null),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private async Task<string> GetPrimaryEmailGithubAsync(HttpClient client, string token, CancellationToken ct)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cortex/1.0");
        var emails = await client.GetFromJsonAsync<List<GithubEmail>>("https://api.github.com/user/emails", ct);
        return emails?.FirstOrDefault(e => e.Primary)?.Email
            ?? emails?.FirstOrDefault()?.Email
            ?? throw new InvalidOperationException("GitHub returned no usable email");
    }

    private OAuthProviderOptions GetOptions(AuthProvider provider) => provider switch
    {
        AuthProvider.Google => _opts.Google,
        AuthProvider.GitHub => _opts.GitHub,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private sealed class GithubEmail
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("primary")] public bool Primary { get; set; }
        [JsonPropertyName("verified")] public bool Verified { get; set; }
    }
}
