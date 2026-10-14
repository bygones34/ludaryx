using Ludaryx.Application.Backlog;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Backlog;

public sealed class EfBacklogStore(LudaryxDbContext context, TimeProvider time) : IBacklogStore
{
    public async Task<BacklogResponse> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        new(await context.UserGames.AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == GameStatus.Backlog)
            .OrderBy(item => item.BacklogOrder)
            .Select(item => new BacklogItem(item.Id, item.BacklogOrder!.Value, item.Priority == null ? null : item.Priority.ToString(),
                new BacklogGameSummary(item.Game.IgdbId, item.Game.Name, item.Game.CoverUrl)))
            .ToListAsync(cancellationToken));

    public async Task<ReorderBacklogResult> ReorderAsync(Guid userId, ReorderBacklogCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var users = await context.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (users.Count == 0) return new ReorderBacklogResult.NotFound();

        // The handler has validated shape, IDs, and contiguous positions.
        var orders = command.Items!.ToDictionary(item => item!.UserGameId, item => item!.Order);
        var ids = orders.Keys.ToArray();
        var supplied = await context.UserGames.AsNoTracking().Where(item => item.UserId == userId && ids.Contains(item.Id))
            .Select(item => new { item.Id, item.Status }).ToListAsync(cancellationToken);
        if (supplied.Count != ids.Length) return new ReorderBacklogResult.NotFound();
        if (supplied.Any(item => item.Status != GameStatus.Backlog))
            return new ReorderBacklogResult.Invalid(new Dictionary<string, string[]> { ["items"] = ["Every supplied item must be in Backlog."] });

        var current = await context.UserGames.AsNoTracking().Include(item => item.Game)
            .Where(item => item.UserId == userId && item.Status == GameStatus.Backlog).ToListAsync(cancellationToken);
        if (current.Count != orders.Count) return new ReorderBacklogResult.Conflict();

        await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS \"UQ_UserGames_UserId_BacklogOrder\" DEFERRED", cancellationToken);
        var now = time.GetUtcNow();
        foreach (var item in current.Where(item => item.BacklogOrder != orders[item.Id]))
        {
            item.Reorder(orders[item.Id], now);
            // Direct updates avoid EF's unique-index dependency ordering during swaps.
            // The user lock and deferred database constraint protect the whole transaction.
            var changed = await context.UserGames.Where(stored => stored.UserId == userId && stored.Id == item.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(stored => stored.BacklogOrder, item.BacklogOrder)
                    .SetProperty(stored => stored.UpdatedAt, item.UpdatedAt), cancellationToken);
            if (changed != 1) throw new InvalidOperationException("Backlog item disappeared during reorder.");
        }
        var saved = new BacklogResponse(current.OrderBy(item => item.BacklogOrder).Select(item =>
            new BacklogItem(item.Id, item.BacklogOrder!.Value, item.Priority?.ToString(),
                new BacklogGameSummary(item.Game.IgdbId, item.Game.Name, item.Game.CoverUrl))).ToArray());
        await transaction.CommitAsync(cancellationToken);
        return new ReorderBacklogResult.Reordered(saved);
    }
}
