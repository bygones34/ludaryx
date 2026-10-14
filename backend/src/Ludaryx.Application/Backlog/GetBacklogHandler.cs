namespace Ludaryx.Application.Backlog;

public sealed class GetBacklogHandler(IBacklogStore backlog)
{
    public Task<BacklogResponse> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        backlog.GetAsync(userId, cancellationToken);
}
