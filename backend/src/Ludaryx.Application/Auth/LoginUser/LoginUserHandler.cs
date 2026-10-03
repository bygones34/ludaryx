using FluentValidation;

namespace Ludaryx.Application.Auth.LoginUser;

public sealed class LoginUserHandler(
    IValidator<LoginUserCommand> validator,
    IUserAccountStore users,
    IAccessTokenIssuer tokens,
    IRefreshSessionStore sessions)
{
    public async Task<LoginUserResult> HandleAsync(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());

            return new LoginUserResult.Invalid(errors);
        }

        var user = await users.VerifyPasswordAsync(
            command.Email, command.Password, cancellationToken);
        if (user is null)
        {
            return new LoginUserResult.Rejected();
        }

        var accessToken = tokens.Issue(user);
        var refreshSession = await sessions.CreateAsync(user.Id, cancellationToken);
        return new LoginUserResult.Authenticated(
            accessToken.Value, accessToken.ExpiresInSeconds, user, refreshSession);
    }
}
