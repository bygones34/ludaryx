namespace Ludaryx.Application.Auth;

public interface IUserAccountStore
{
    Task<UserAccount?> VerifyPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
