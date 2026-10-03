namespace Ludaryx.Application.Auth.LoginUser;

public abstract record LoginUserResult
{
    public sealed record Authenticated(
        string AccessToken,
        int ExpiresIn,
        UserAccount User,
        RefreshSession RefreshSession) : LoginUserResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : LoginUserResult;

    public sealed record Rejected : LoginUserResult;
}
