using Ludaryx.Application.Games;
using Ludaryx.Application.Games.SearchGames;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/games")]
public sealed class GamesController(SearchGamesHandler searchGames) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        try
        {
            var result = await searchGames.HandleAsync(new SearchGamesQuery(q), cancellationToken);
            if (result is SearchGamesResult.Invalid invalid)
            {
                foreach (var (field, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(field, message);
                    }
                }

                return ValidationProblem(ModelState);
            }

            var found = (SearchGamesResult.Found)result;
            return Ok(new SearchGamesResponse(found.Items));
        }
        catch (GameProviderException exception)
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
}

public sealed record SearchGamesResponse(IReadOnlyList<GameSearchItem> Items);
