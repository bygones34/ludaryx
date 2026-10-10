namespace Ludaryx.Application.Games;

public interface IIgdbClient
{
    Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken);
}

public sealed record GameSearchItem(
    long IgdbId, string Name, string? CoverUrl, DateOnly? ReleaseDate,
    IReadOnlyList<string> Platforms);

public sealed record GameDetails(
    long IgdbId, string Name, string Slug, string? Summary, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<GameGenre> Genres, IReadOnlyList<GamePlatform> Platforms);

public sealed record GameGenre(long IgdbId, string Name);

public sealed record GamePlatform(long IgdbId, string Name, string? Abbreviation);
