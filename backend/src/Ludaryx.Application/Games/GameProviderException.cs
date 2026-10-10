namespace Ludaryx.Application.Games;

public enum GameProviderFailure
{
    Unavailable,
    Timeout,
    InvalidResponse,
    Authentication
}

public sealed class GameProviderException(GameProviderFailure failure)
    : Exception("The game provider could not complete the request.")
{
    public GameProviderFailure Failure { get; } = failure;
}
