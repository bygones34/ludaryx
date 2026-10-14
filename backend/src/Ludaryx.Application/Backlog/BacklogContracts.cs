namespace Ludaryx.Application.Backlog;

public sealed record BacklogGameSummary(long IgdbId, string Name, string? CoverUrl);
public sealed record BacklogItem(Guid Id, int Order, string? Priority, BacklogGameSummary Game);
public sealed record BacklogResponse(IReadOnlyList<BacklogItem> Items);
public sealed record BacklogOrderItem(Guid UserGameId, int Order);
public sealed record ReorderBacklogCommand(IReadOnlyList<BacklogOrderItem?>? Items);

public abstract record ReorderBacklogResult
{
    public sealed record Reordered(BacklogResponse Backlog) : ReorderBacklogResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ReorderBacklogResult;
    public sealed record NotFound : ReorderBacklogResult;
    public sealed record Conflict : ReorderBacklogResult;
}

public interface IBacklogStore
{
    Task<BacklogResponse> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<ReorderBacklogResult> ReorderAsync(Guid userId, ReorderBacklogCommand command, CancellationToken cancellationToken);
}
