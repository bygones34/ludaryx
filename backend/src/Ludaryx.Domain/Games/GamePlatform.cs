namespace Ludaryx.Domain.Games;

public sealed class GamePlatform
{
    private GamePlatform() { }

    public GamePlatform(Guid gameId, Guid platformId)
    {
        if (gameId == Guid.Empty || platformId == Guid.Empty)
            throw new ArgumentException("Game and platform IDs must not be empty.");
        GameId = gameId;
        PlatformId = platformId;
    }

    public Guid GameId { get; private set; }
    public Guid PlatformId { get; private set; }
    public Game Game { get; private set; } = null!;
    public Platform Platform { get; private set; } = null!;
}
