using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.Infrastructure.Authentication;

public sealed record JwtSettings(
    string Issuer,
    string Audience,
    SymmetricSecurityKey SigningKey);
