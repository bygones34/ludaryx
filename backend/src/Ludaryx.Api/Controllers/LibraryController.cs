using Ludaryx.Application.Games;
using Ludaryx.Application.Library.AddGame;
using Ludaryx.Application.Library.ListLibrary;
using Ludaryx.Application.Library.GetLibraryItem;
using Ludaryx.Application.Library.UpdateLibraryItem;
using Ludaryx.Application.Library.DeleteLibraryItem;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/library")]
public sealed class LibraryController(AddGameHandler addGame, ListLibraryHandler listLibrary,
    GetLibraryItemHandler getLibraryItem, UpdateLibraryHandler updateLibrary, DeleteLibraryItemHandler deleteLibraryItem) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(AddGameCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty) return Unauthorized();
        try
        {
            var result = await addGame.HandleAsync(userId, request, cancellationToken);
            if (result is AddGameResult.Invalid invalid)
            {
                foreach (var (field, messages) in invalid.Errors)
                    foreach (var message in messages) ModelState.AddModelError(field, message);
                return ValidationProblem(ModelState);
            }
            return result switch
            {
                AddGameResult.Added added => Created($"/api/v1/library/{added.Item.Id}", added.Item),
                AddGameResult.Duplicate => Problem(statusCode: 409, title: "Game is already in your library."),
                AddGameResult.NotFound => Problem(statusCode: 404, title: "Game was not found."),
                AddGameResult.UserMissing => Unauthorized(),
                _ => throw new InvalidOperationException("Unexpected library addition result.")
            };
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

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListLibraryQuery request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await listLibrary.HandleAsync(userId, request, cancellationToken);
        return result is ListLibraryResult.Invalid invalid ? InvalidRequest(invalid.Errors)
            : Ok(((ListLibraryResult.Listed)result).Library);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var item = await getLibraryItem.HandleAsync(userId, id, cancellationToken);
        return item is null ? MissingItem() : Ok(item);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] JsonElement request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseUpdate(request, out var command))
            return InvalidRequest(new Dictionary<string, string[]> { ["request"] = ["Use an object with status/priority strings, an integer rating, or a personalNote string. Nullable fields may be cleared with null. Duplicate or unsupported fields are not allowed."] });
        var result = await updateLibrary.HandleAsync(userId, id, command!, cancellationToken);
        return result switch
        {
            UpdateLibraryResult.Updated updated => Ok(updated.Item),
            UpdateLibraryResult.Invalid invalid => InvalidRequest(invalid.Errors),
            _ => MissingItem()
        };
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return await deleteLibraryItem.HandleAsync(userId, id, cancellationToken) ? NoContent() : MissingItem();
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirst("sub")?.Value, out userId) && userId != Guid.Empty;

    private IActionResult MissingItem() => Problem(statusCode: 404, title: "Library item was not found.");

    private IActionResult InvalidRequest(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages) ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    private static bool TryParseUpdate(JsonElement request, out UpdateLibraryCommand? command)
    {
        command = null;
        if (request.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? status = null, priority = null, note = null;
        int? rating = null;
        foreach (var property in request.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            if (property.Name == "rating")
            {
                if (property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var number)) return false;
                rating = number;
            }
            else if (property.Name is "status" or "priority" or "personalNote")
            {
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return false;
                var text = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
                switch (property.Name)
                {
                    case "status": status = text; break;
                    case "priority": priority = text; break;
                    case "personalNote": note = text; break;
                }
            }
            else return false;
        }
        command = new UpdateLibraryCommand(seen.Contains("status"), status, seen.Contains("priority"), priority,
            seen.Contains("rating"), rating, seen.Contains("personalNote"), note);
        return true;
    }
}
