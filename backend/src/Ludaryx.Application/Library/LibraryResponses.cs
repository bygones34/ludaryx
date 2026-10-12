using Ludaryx.Application.Games;
using Ludaryx.Application.Library.AddGame;

namespace Ludaryx.Application.Library;

public sealed record LibraryListItem(Guid Id, LibraryGameSummary Game, string Status, int? Rating,
    string? Priority, DateTimeOffset AddedAt, DateTimeOffset? CompletedAt);

public sealed record LibraryPlatformOption(long IgdbId, string Name);

public sealed record LibraryPage(IReadOnlyList<LibraryListItem> Items, int Page, int PageSize, int TotalCount,
    int TotalPages, IReadOnlyList<LibraryPlatformOption> AvailablePlatforms);

public sealed record StoredGameDetails(Guid Id, long IgdbId, string Name, string Slug, string? Summary,
    string? CoverUrl, DateOnly? ReleaseDate, IReadOnlyList<GameGenre> Genres, IReadOnlyList<GamePlatform> Platforms);

public sealed record LibraryItemDetails(Guid Id, StoredGameDetails Game, string Status, string? Priority,
    int? BacklogOrder, int? Rating, string? PersonalNote, DateTimeOffset AddedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
