using System.Text.Json;
using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private const string StateCookie = "cortex_oauth_state";
    private const string RedirectCookie = "cortex_oauth_redirect";
    private const string CallbackCookie = "cortex_oauth_callback";

    private readonly IOAuthService _oauth;
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;

    public AuthController(IOAuthService oauth, IAuthService auth, AppDbContext db)
    {
        _oauth = oauth;
        _auth = auth;
        _db = db;
    }

    private static string BackendCallbackUrl(HttpRequest req, string provider) =>
        $"{req.Scheme}://{req.Host}{req.PathBase}/api/auth/{provider.ToLowerInvariant()}/callback";

    private static CookieOptions ShortCookie() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UtcNow.AddMinutes(10)
    };

    [HttpGet("{provider}/login")]
    public IActionResult Login(string provider, [FromQuery] string redirectUri)
    {
        if (!Enum.TryParse<AuthProvider>(provider, ignoreCase: true, out var p))
            return BadRequest(new ErrorDetail("Invalid provider", "Use 'google' or 'github'"));

        if (string.IsNullOrWhiteSpace(redirectUri))
            return BadRequest(new ErrorDetail("Missing redirectUri"));

        // The provider (GitHub/Google) only accepts https callback URLs, so we
        // always redirect back to our own backend endpoint here. The mobile
        // app's custom-scheme redirect is stashed in a cookie and replayed in
        // the callback to bounce the user-agent back to the app.
        var backendCallback = BackendCallbackUrl(Request, provider);
        var (url, state) = _oauth.BuildAuthorizeUrl(p, backendCallback);

        Response.Cookies.Append(StateCookie, state, ShortCookie());
        Response.Cookies.Append(RedirectCookie, redirectUri, ShortCookie());
        Response.Cookies.Append(CallbackCookie, backendCallback, ShortCookie());

        return Redirect(url);
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string code, [FromQuery] string state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!Enum.TryParse<AuthProvider>(provider, ignoreCase: true, out var p))
            return BadRequest(new ErrorDetail("Invalid provider"));

        if (!string.IsNullOrEmpty(error))
            return BadRequest(new ErrorDetail("OAuth provider returned an error", error));

        var expectedState = Request.Cookies[StateCookie];
        if (string.IsNullOrEmpty(expectedState) || expectedState != state)
            return BadRequest(new ErrorDetail("Invalid OAuth state"));

        var appRedirect = Request.Cookies[RedirectCookie];
        var backendCallback = Request.Cookies[CallbackCookie];
        Response.Cookies.Delete(StateCookie);
        Response.Cookies.Delete(RedirectCookie);
        Response.Cookies.Delete(CallbackCookie);

        if (string.IsNullOrWhiteSpace(appRedirect))
            return BadRequest(new ErrorDetail("Missing app redirect"));
        if (string.IsNullOrWhiteSpace(backendCallback))
            return BadRequest(new ErrorDetail("Missing OAuth callback URL"));

        ExternalIdentity identity;
        try
        {
            // Reuse the exact callback URL stashed in /login so the provider
            // sees a byte-for-byte identical redirect_uri on both legs.
            identity = await _oauth.ExchangeCodeAsync(p, code, backendCallback, ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new ErrorDetail("OAuth exchange failed", ex.Message));
        }

        // upsert user
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Provider == identity.Provider && u.ProviderUid == identity.ProviderUid, ct);
        if (user is null)
        {
            user = new User
            {
                Email = identity.Email,
                Name = identity.Name,
                AvatarUrl = identity.AvatarUrl,
                Provider = identity.Provider,
                ProviderUid = identity.ProviderUid
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            var dirty = false;
            if (user.Email != identity.Email) { user.Email = identity.Email; dirty = true; }
            if (user.Name != identity.Name && identity.Name is not null) { user.Name = identity.Name; dirty = true; }
            if (user.AvatarUrl != identity.AvatarUrl && identity.AvatarUrl is not null) { user.AvatarUrl = identity.AvatarUrl; dirty = true; }
            if (dirty) await _db.SaveChangesAsync(ct);
        }

        var (accessToken, expiresAt) = _auth.IssueAccessToken(user);
        var (rawRefresh, _) = await _auth.IssueRefreshTokenAsync(user, ct);

        var response = new AuthResponse(accessToken, expiresAt, rawRefresh, ToProfile(user));

        // Hand the tokens to the app via its custom-scheme redirect. The in-app
        // browser intercepts the navigation and closes, returning this URL.
        var payload = Uri.EscapeDataString(JsonSerializer.Serialize(response, JsonOpts));
        var separator = appRedirect.Contains('?') ? "&" : "?";
        return Redirect($"{appRedirect}{separator}data={payload}");
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var user = await _auth.ValidateRefreshTokenAsync(req.RefreshToken, ct);
        if (user is null) return Unauthorized(new ErrorDetail("Invalid refresh token"));

        // rotate: revoke old, issue new
        await _auth.RevokeRefreshTokenAsync(req.RefreshToken, ct);
        var (accessToken, expiresAt) = _auth.IssueAccessToken(user);
        var (rawRefresh, _) = await _auth.IssueRefreshTokenAsync(user, ct);
        return Ok(new AuthResponse(accessToken, expiresAt, rawRefresh, ToProfile(user)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        await _auth.RevokeRefreshTokenAsync(req.RefreshToken, ct);
        return NoContent();
    }

    private static UserProfile ToProfile(User u) => new(u.Id, u.Email, u.Name, u.AvatarUrl, u.Provider, u.CreatedAt);
}
