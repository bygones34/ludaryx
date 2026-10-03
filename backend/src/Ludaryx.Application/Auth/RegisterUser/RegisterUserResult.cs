namespace Ludaryx.Application.Auth.RegisterUser;

public abstract record RegisterUserResult
{
    public sealed record Registered(Guid Id, string Username, string Email) : RegisterUserResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : RegisterUserResult;

    public sealed record Conflict : RegisterUserResult;
}
