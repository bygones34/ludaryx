using Ludaryx.Application.Auth.LoginUser;
using Ludaryx.Application.Auth.LogoutUser;
using Ludaryx.Application.Auth.RefreshUser;
using Ludaryx.Application.Auth.RegisterUser;
using Ludaryx.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Ludaryx.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    RegisterUserHandler registerUser,
    LoginUserHandler loginUser,
    RefreshUserHandler refreshUser,
    LogoutUserHandler logoutUser,
    IConfiguration configuration) : ControllerBase
{
    private const string RefreshCookieName = "ludaryx.refresh";
    private const string RefreshCookiePath = "/api/v1/auth";

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await registerUser.HandleAsync(
            new RegisterUserCommand(request.Username, request.Email, request.Password),
            cancellationToken);

        if (result is RegisterUserResult.Invalid invalid)
        {
            return InvalidRequest(invalid.Errors);
        }

        if (result is RegisterUserResult.Conflict)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Username or email is already in use."
            });
        }

        var registered = (RegisterUserResult.Registered)result;
        return StatusCode(StatusCodes.Status201Created,
            new RegisterResponse(registered.Id, registered.Username, registered.Email));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginUser.HandleAsync(
            new LoginUserCommand(request.Email, request.Password), cancellationToken);

        if (result is LoginUserResult.Invalid invalid)
        {
            return InvalidRequest(invalid.Errors);
        }

        if (result is LoginUserResult.Rejected)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid email or password."
            });
        }

        var authenticated = (LoginUserResult.Authenticated)result;
        SetRefreshCookie(authenticated.RefreshSession);
        return Ok(new LoginResponse(
            authenticated.AccessToken,
            authenticated.ExpiresIn,
            new LoginUserResponse(authenticated.User.Id, authenticated.User.Username)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!HasAllowedOrigin())
        {
            return Forbid();
        }

        var result = await refreshUser.HandleAsync(
            Request.Cookies[RefreshCookieName], cancellationToken);
        if (result is null)
        {
            ClearRefreshCookie();
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Refresh session is invalid."
            });
        }

        SetRefreshCookie(result.Session);
        return Ok(new RefreshResponse(result.AccessToken, result.ExpiresIn));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!HasAllowedOrigin())
        {
            return Forbid();
        }

        await logoutUser.HandleAsync(Request.Cookies[RefreshCookieName], cancellationToken);
        ClearRefreshCookie();
        return NoContent();
    }

    private bool HasAllowedOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        return configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
            .Contains(origin, StringComparer.Ordinal) == true;
    }

    private void SetRefreshCookie(RefreshSession session) =>
        Response.Cookies.Append(RefreshCookieName, session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            MaxAge = session.ExpiresAt - DateTimeOffset.UtcNow,
            IsEssential = true
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath
        });

    private IActionResult InvalidRequest(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return ValidationProblem(ModelState);
    }
}

public sealed record RegisterRequest(string Username, string Email, string Password);

public sealed record RegisterResponse(Guid Id, string Username, string Email);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, int ExpiresIn, LoginUserResponse User);

public sealed record RefreshResponse(string AccessToken, int ExpiresIn);

public sealed record LoginUserResponse(Guid Id, string Username);
