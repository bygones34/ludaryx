namespace Ludaryx.Application.Dashboard.GetDashboard;

public sealed record DashboardStatistics(int TotalGames, int Playing, int Completed, int Backlog,
    int OnHold, int Dropped, decimal? AverageRating);

public sealed record CurrentlyPlayingItem(Guid UserGameId, long IgdbId, string Name, string? CoverUrl, int? Rating);

public sealed record UpNextItem(Guid UserGameId, long IgdbId, string Name, string? CoverUrl, string? Priority, int Order);

public sealed record DashboardResponse(DashboardStatistics Statistics,
    IReadOnlyList<CurrentlyPlayingItem> CurrentlyPlaying, IReadOnlyList<UpNextItem> UpNext);

public interface IDashboardQueryStore
{
    Task<DashboardResponse> GetAsync(Guid userId, CancellationToken cancellationToken);
}
