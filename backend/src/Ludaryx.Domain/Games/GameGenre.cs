namespace Ludaryx.Domain.Games;

public sealed class GameGenre
{
    private GameGenre() { }

    public GameGenre(Guid gameId, Guid genreId)
    {
        if (gameId == Guid.Empty || genreId == Guid.Empty)
            throw new ArgumentException("Game and genre IDs must not be empty.");
        GameId = gameId;
        GenreId = genreId;
    }

    public Guid GameId { get; private set; }
    public Guid GenreId { get; private set; }
    public Game Game { get; private set; } = null!;
    public Genre Genre { get; private set; } = null!;
}
