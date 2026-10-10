using FluentValidation;

namespace Ludaryx.Application.Games.SearchGames;

public sealed class SearchGamesHandler(IValidator<SearchGamesQuery> validator, IIgdbClient games)
{
    public async Task<SearchGamesResult> HandleAsync(SearchGamesQuery query, CancellationToken cancellationToken)
    {
        var normalized = query with { Q = query.Q?.Trim() };
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return new SearchGamesResult.Invalid(new Dictionary<string, string[]>
            {
                ["q"] = validation.Errors.Select(error => error.ErrorMessage).ToArray()
            });
        }

        var items = await games.SearchAsync(normalized.Q!, cancellationToken);
        return new SearchGamesResult.Found(items.Take(20).ToArray());
    }
}
