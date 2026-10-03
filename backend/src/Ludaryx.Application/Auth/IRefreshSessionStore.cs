namespace Ludaryx.Application.Auth;

public interface IRefreshSessionStore
{
    Task<RefreshSession> CreateAsync(Guid userId, CancellationToken cancellationToken);

    Task<RotatedRefreshSession?> RotateAsync(string token, CancellationToken cancellationToken);

    Task RevokeFamilyAsync(string? token, CancellationToken cancellationToken);
}

public sealed record RefreshSession(string Token, DateTimeOffset ExpiresAt);

public sealed record RotatedRefreshSession(Guid UserId, RefreshSession Session);
