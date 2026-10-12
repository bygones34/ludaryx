namespace Ludaryx.Application.Library.AddGame;

public abstract record AddGameResult
{
    public sealed record Added(AddedLibraryItem Item) : AddGameResult;
    public sealed record Duplicate : AddGameResult;
    public sealed record NotFound : AddGameResult;
    public sealed record UserMissing : AddGameResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : AddGameResult;
}

public sealed record AddedLibraryItem(
    Guid Id, LibraryGameSummary Game, string Status, string? Priority, int? BacklogOrder,
    int? Rating, string? PersonalNote, DateTimeOffset AddedAt);

public sealed record LibraryGameSummary(Guid Id, long IgdbId, string Name, string? CoverUrl);
