using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Data;
using Cortex.Core.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cortex.Core.Controllers;

/// <summary>
/// Token usage and estimated cost per provider for the authenticated user
/// (assistant turns only; costs come from message rows computed at finalize).
/// </summary>
[ApiController]
[Authorize]
[Route("api/usage")]
public class UsageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _me;

    public UsageController(AppDbContext db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpGet]
    public async Task<IActionResult> Month([FromQuery] string? month, CancellationToken ct)
    {
        // month = "yyyy-MM"; defaults to the current UTC month.
        DateTimeOffset start;
        if (string.IsNullOrWhiteSpace(month) ||
            !DateTimeOffset.TryParse($"{month}-01T00:00:00Z", out var parsed))
        {
            var now = DateTimeOffset.UtcNow;
            start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        }
        else
        {
            start = parsed;
        }
        var end = start.AddMonths(1);

        var rows = await _db.Conversations
            .Where(c => c.UserId == _me.UserId)
            .Join(
                _db.Messages.Where(m => m.Role == MessageRole.Assistant && m.CreatedAt >= start && m.CreatedAt < end),
                c => c.Id,
                m => m.ConversationId,
                (c, m) => new { c.Provider, m.TokensIn, m.TokensOut, m.Cost })
            .GroupBy(x => x.Provider)
            .Select(g => new UsageResponse(
                g.Key,
                g.Count(),
                g.Sum(x => x.TokensIn ?? 0),
                g.Sum(x => x.TokensOut ?? 0),
                g.Sum(x => x.Cost)))
            .ToListAsync(ct);

        return Ok(rows);
    }
}
