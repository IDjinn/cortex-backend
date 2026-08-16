using System.Security.Claims;

namespace Cortex.Core.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _ctx;

    public CurrentUser(IHttpContextAccessor ctx)
    {
        _ctx = ctx;
    }

    public Guid UserId
    {
        get
        {
            var sub = _ctx.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? _ctx.HttpContext?.User?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
        }
    }

    public string Email =>
        _ctx.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
        ?? _ctx.HttpContext?.User?.FindFirstValue("email")
        ?? string.Empty;

    public bool IsAuthenticated =>
        _ctx.HttpContext?.User?.Identity?.IsAuthenticated == true;
}
