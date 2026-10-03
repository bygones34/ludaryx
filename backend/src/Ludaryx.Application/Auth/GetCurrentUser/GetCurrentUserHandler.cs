namespace Ludaryx.Application.Auth.GetCurrentUser;

public sealed class GetCurrentUserHandler(IUserAccountStore users)
{
    public Task<UserAccount?> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        users.FindByIdAsync(userId, cancellationToken);
}
