namespace Ludaryx.Application.Auth;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(UserAccount user);
}

public sealed record IssuedAccessToken(string Value, int ExpiresInSeconds);
