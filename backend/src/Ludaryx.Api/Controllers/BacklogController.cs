using Ludaryx.Application.Backlog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/backlog")]
public sealed class BacklogController(GetBacklogHandler getBacklog, ReorderBacklogHandler reorderBacklog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await getBacklog.HandleAsync(userId, cancellationToken));
    }

    [HttpPut("order")]
    public async Task<IActionResult> Reorder(ReorderBacklogCommand request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await reorderBacklog.HandleAsync(userId, request, cancellationToken);
        if (result is ReorderBacklogResult.Invalid invalid)
        {
            foreach (var (field, messages) in invalid.Errors)
                foreach (var message in messages) ModelState.AddModelError(field, message);
            return ValidationProblem(ModelState);
        }
        return result switch
        {
            ReorderBacklogResult.Reordered saved => Ok(saved.Backlog),
            ReorderBacklogResult.NotFound => Problem(statusCode: 404, title: "Library item was not found."),
            ReorderBacklogResult.Conflict => Problem(statusCode: 409, title: "Backlog has changed. Reload it before reordering."),
            _ => throw new InvalidOperationException("Unexpected backlog reorder result.")
        };
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirst("sub")?.Value, out userId) && userId != Guid.Empty;
}
