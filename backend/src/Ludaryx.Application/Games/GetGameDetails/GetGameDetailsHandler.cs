using FluentValidation;

namespace Ludaryx.Application.Games.GetGameDetails;

public sealed class GetGameDetailsHandler(IValidator<GetGameDetailsQuery> validator, IIgdbClient games)
{
    public async Task<GetGameDetailsResult> HandleAsync(
        GetGameDetailsQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new GetGameDetailsResult.Invalid(new Dictionary<string, string[]>
            {
                ["igdbId"] = validation.Errors.Select(error => error.ErrorMessage).ToArray()
            });
        }

        var game = await games.GetDetailsAsync(query.IgdbId, cancellationToken);
        if (game is null) return new GetGameDetailsResult.NotFound();

        // M2 has no library records; M3 will resolve the current user's persisted membership.
        return new GetGameDetailsResult.Found(new GameDetailsResponse(
            game.IgdbId, game.Name, game.Slug, game.Summary, game.CoverUrl,
            game.ReleaseDate, game.Genres, game.Platforms,
            new GameLibraryMembership(false, null, null)));
    }
}
