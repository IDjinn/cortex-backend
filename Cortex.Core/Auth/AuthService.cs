using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cortex.Core.Data;
using Cortex.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cortex.Core.Auth;

public interface IAuthService
{
    (string accessToken, DateTimeOffset expiresAt) IssueAccessToken(User user);
    Task<(string rawToken, RefreshToken entity)> IssueRefreshTokenAsync(User user, CancellationToken ct = default);
    Task<User?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default);
    Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly JwtOptions _jwt;

    public AuthService(AppDbContext db, IOptions<JwtOptions> jwt)
    {
        _db = db;
        _jwt = jwt.Value;
    }

    public (string accessToken, DateTimeOffset expiresAt) IssueAccessToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public async Task<(string rawToken, RefreshToken entity)> IssueRefreshTokenAsync(User user, CancellationToken ct = default)
    {
        var raw = RefreshToken.Generate();
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = RefreshToken.HashToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
        return (raw, entity);
    }

    public async Task<User?> ValidateRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = RefreshToken.HashToken(rawToken);
        var entity = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (entity is null || !entity.IsActive) return null;
        return entity.User;
    }

    public async Task RevokeRefreshTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = RefreshToken.HashToken(rawToken);
        var entity = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is not null && entity.IsActive)
        {
            entity.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
