using System.Data;
using Ludaryx.Application.Dashboard.GetDashboard;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Dashboard;

public sealed class EfDashboardQueryStore(LudaryxDbContext context) : IDashboardQueryStore
{
    public async Task<DashboardResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Keep statistics and both bounded previews on the same snapshot during concurrent edits.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var owned = context.UserGames.AsNoTracking().Where(item => item.UserId == userId);
        var statistics = await owned.GroupBy(item => 1)
            .Select(group => new DashboardStatistics(group.Count(),
                group.Count(item => item.Status == GameStatus.Playing),
                group.Count(item => item.Status == GameStatus.Completed),
                group.Count(item => item.Status == GameStatus.Backlog),
                group.Count(item => item.Status == GameStatus.OnHold),
                group.Count(item => item.Status == GameStatus.Dropped),
                group.Average(item => (decimal?)item.Rating)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new DashboardStatistics(0, 0, 0, 0, 0, 0, null);

        var playing = await owned.Where(item => item.Status == GameStatus.Playing)
            .OrderBy(item => item.StartedAt == null).ThenByDescending(item => item.StartedAt)
            .ThenByDescending(item => item.AddedAt).ThenBy(item => item.Id)
            .Take(5)
            .Select(item => new CurrentlyPlayingItem(item.Id, item.Game.IgdbId, item.Game.Name,
                item.Game.CoverUrl, item.Rating))
            .ToListAsync(cancellationToken);

        var upNext = await owned.Where(item => item.Status == GameStatus.Backlog)
            .OrderBy(item => item.BacklogOrder).Take(5)
            .Select(item => new UpNextItem(item.Id, item.Game.IgdbId, item.Game.Name,
                item.Game.CoverUrl, item.Priority == null ? null : item.Priority.ToString(), item.BacklogOrder!.Value))
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new DashboardResponse(statistics, playing, upNext);
    }
}
