using Cortex.Core.Auth;
using Cortex.Core.Data;
using Cortex.Core.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly ICurrentUser _me;
    private readonly AppDbContext _db;

    public MeController(ICurrentUser me, AppDbContext db)
    {
        _me = me;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(new object?[] { _me.UserId }, ct);
        if (user is null) return NotFound();
        return Ok(new UserProfile(user.Id, user.Email, user.Name, user.AvatarUrl, user.Provider, user.CreatedAt));
    }
}
