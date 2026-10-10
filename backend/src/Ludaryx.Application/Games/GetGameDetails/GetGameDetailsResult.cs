namespace Ludaryx.Application.Games.GetGameDetails;

public abstract record GetGameDetailsResult
{
    public sealed record Found(GameDetailsResponse Game) : GetGameDetailsResult;
    public sealed record NotFound : GetGameDetailsResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : GetGameDetailsResult;
}

public sealed record GameDetailsResponse(
    long IgdbId, string Name, string Slug, string? Summary, string? CoverUrl,
    DateOnly? ReleaseDate, IReadOnlyList<GameGenre> Genres,
    IReadOnlyList<GamePlatform> Platforms, GameLibraryMembership Library);

public sealed record GameLibraryMembership(bool IsAdded, Guid? UserGameId, string? Status);
