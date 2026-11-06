using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Transcendence.Api.Users;

namespace Transcendence.Api.Auth;

public sealed class TokenService(IOptions<JwtSettings> options, TimeProvider clock)
{
    private readonly JwtSettings settings = options.Value;
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiresAt) Create(UserRecord user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim("name", user.Username),
            }),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256),
        };
        return (handler.CreateToken(descriptor), expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The player id carried in the token's "sub" claim.</summary>
    public static long UserId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue("sub") ?? throw new InvalidOperationException("token without a sub claim"),
            CultureInfo.InvariantCulture);
}
