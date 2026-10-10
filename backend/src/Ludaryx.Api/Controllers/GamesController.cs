using Ludaryx.Application.Games;
using Ludaryx.Application.Games.SearchGames;
using Ludaryx.Application.Games.GetGameDetails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/games")]
public sealed class GamesController(SearchGamesHandler searchGames, GetGameDetailsHandler getGameDetails) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        try
        {
            var result = await searchGames.HandleAsync(new SearchGamesQuery(q), cancellationToken);
            if (result is SearchGamesResult.Invalid invalid)
            {
                return InvalidRequest(invalid.Errors);
            }

            var found = (SearchGamesResult.Found)result;
            return Ok(new SearchGamesResponse(found.Items));
        }
        catch (GameProviderException exception)
        {
            return ProviderProblem(exception);
        }
    }

    [HttpGet("igdb/{igdbId}")]
    public async Task<IActionResult> Details([FromRoute] long igdbId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await getGameDetails.HandleAsync(new GetGameDetailsQuery(igdbId), cancellationToken);
            return result switch
            {
                GetGameDetailsResult.Invalid invalid => InvalidRequest(invalid.Errors),
                GetGameDetailsResult.NotFound => Problem(statusCode: 404, title: "Game was not found."),
                GetGameDetailsResult.Found found => Ok(found.Game),
                _ => throw new InvalidOperationException("Unexpected game details result.")
            };
        }
        catch (GameProviderException exception)
        {
            return ProviderProblem(exception);
        }
    }

    private IActionResult InvalidRequest(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages) ModelState.AddModelError(field, message);
        }
        return ValidationProblem(ModelState);
    }

    private IActionResult ProviderProblem(GameProviderException exception)
    {
        var (status, title) = exception.Failure switch
        {
            GameProviderFailure.Unavailable => (503, "Game provider is temporarily unavailable."),
            GameProviderFailure.Timeout => (504, "Game provider request timed out."),
            _ => (502, "Game provider could not complete the request.")
        };
        return Problem(statusCode: status, title: title);
    }
}

public sealed record SearchGamesResponse(IReadOnlyList<GameSearchItem> Items);
