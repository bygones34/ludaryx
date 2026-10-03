namespace Ludaryx.Application.Auth.LogoutUser;

public sealed class LogoutUserHandler(IRefreshSessionStore sessions)
{
    public Task HandleAsync(string? token, CancellationToken cancellationToken) =>
        sessions.RevokeFamilyAsync(token, cancellationToken);
}
