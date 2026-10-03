using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ludaryx.Application.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.Infrastructure.Authentication;

public sealed class JwtAccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    private const int LifetimeSeconds = 15 * 60;

    public IssuedAccessToken Issue(UserAccount user)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: now,
            expires: now.AddSeconds(LifetimeSeconds),
            signingCredentials: new SigningCredentials(
                settings.SigningKey, SecurityAlgorithms.HmacSha256));

        return new IssuedAccessToken(
            new JwtSecurityTokenHandler().WriteToken(token), LifetimeSeconds);
    }
}
