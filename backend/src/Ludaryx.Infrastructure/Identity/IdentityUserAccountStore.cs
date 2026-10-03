using Ludaryx.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace Ludaryx.Infrastructure.Identity;

public sealed class IdentityUserAccountStore(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn) : IUserAccountStore
{
    public async Task<UserAccount?> VerifyPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        var result = await signIn.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);
        return result.Succeeded ? ToAccount(user) : null;
    }

    public async Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? null : ToAccount(user);
    }

    private static UserAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.UserName!, user.Email!, user.DisplayName, user.CreatedAt);
}
