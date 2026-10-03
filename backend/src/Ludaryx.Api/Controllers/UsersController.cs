using Ludaryx.Application.Auth.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(GetCurrentUserHandler currentUser) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            return Unauthorized();
        }

        var account = await currentUser.HandleAsync(userId, cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        return Ok(new CurrentUserResponse(
            account.Id,
            account.Username,
            account.Email,
            account.DisplayName,
            account.CreatedAt));
    }
}

public sealed record CurrentUserResponse(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt);
