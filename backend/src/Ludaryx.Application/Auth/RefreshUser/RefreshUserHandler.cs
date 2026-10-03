namespace Ludaryx.Application.Auth.RefreshUser;

public sealed class RefreshUserHandler(
    IRefreshSessionStore sessions,
    IUserAccountStore users,
    IAccessTokenIssuer tokens)
{
    public async Task<RefreshUserResult?> HandleAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var rotated = await sessions.RotateAsync(token, cancellationToken);
        if (rotated is null)
        {
            return null;
        }

        var user = await users.FindByIdAsync(rotated.UserId, cancellationToken);
        if (user is null)
        {
            await sessions.RevokeFamilyAsync(rotated.Session.Token, cancellationToken);
            return null;
        }

        var accessToken = tokens.Issue(user);
        return new RefreshUserResult(
            accessToken.Value, accessToken.ExpiresInSeconds, rotated.Session);
    }
}

public sealed record RefreshUserResult(
    string AccessToken,
    int ExpiresIn,
    RefreshSession Session);
