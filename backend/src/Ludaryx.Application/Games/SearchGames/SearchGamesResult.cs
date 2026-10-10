namespace Ludaryx.Application.Games.SearchGames;

public abstract record SearchGamesResult
{
    public sealed record Found(IReadOnlyList<GameSearchItem> Items) : SearchGamesResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SearchGamesResult;
}
