namespace Ludaryx.Application.Auth.RegisterUser;

public interface IUserRegistrationStore
{
    Task<RegisterUserResult> CreateAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken);
}
