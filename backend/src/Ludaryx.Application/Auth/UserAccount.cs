namespace Ludaryx.Application.Auth;

public sealed record UserAccount(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt);
