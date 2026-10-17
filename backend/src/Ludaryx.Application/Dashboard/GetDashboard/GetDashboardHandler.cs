namespace Ludaryx.Application.Dashboard.GetDashboard;

public sealed class GetDashboardHandler(IDashboardQueryStore dashboard)
{
    public async Task<DashboardResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await dashboard.GetAsync(userId, cancellationToken);
        return result with
        {
            Statistics = result.Statistics with
            {
                AverageRating = result.Statistics.AverageRating is { } average
                    ? Math.Round(average, 1, MidpointRounding.AwayFromZero) : null
            }
        };
    }
}
