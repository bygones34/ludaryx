using Ludaryx.Application.Auth.RegisterUser;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ludaryx.Infrastructure.Identity;

public sealed class IdentityUserRegistrationStore(UserManager<ApplicationUser> userManager)
    : IUserRegistrationStore
{
    public async Task<RegisterUserResult> CreateAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.Username,
            Email = command.Email,
            DisplayName = command.Username,
            CreatedAt = DateTimeOffset.UtcNow
        };

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, command.Password);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName is "EmailIndex" or "UserNameIndex")
        {
            return new RegisterUserResult.Conflict();
        }

        if (result.Succeeded)
        {
            return new RegisterUserResult.Registered(user.Id, user.UserName!, user.Email!);
        }

        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
        {
            return new RegisterUserResult.Conflict();
        }

        var errors = result.Errors
            .Select(error => new
            {
                Field = error.Code switch
                {
                    "InvalidUserName" => "username",
                    "InvalidEmail" => "email",
                    var code when code.StartsWith("Password", StringComparison.Ordinal) => "password",
                    _ => throw new InvalidOperationException(
                        $"Unexpected Identity registration error: {error.Code}")
                },
                error.Description
            })
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

        return new RegisterUserResult.Invalid(errors);
    }
}
