using FluentValidation;

namespace Ludaryx.Application.Auth.RegisterUser;

public sealed class RegisterUserHandler(
    IValidator<RegisterUserCommand> validator,
    IUserRegistrationStore users)
{
    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());

            return new RegisterUserResult.Invalid(errors);
        }

        return await users.CreateAsync(command, cancellationToken);
    }
}
