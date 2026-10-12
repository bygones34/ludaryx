namespace Ludaryx.Domain.Games;

public sealed class Game
{
    private Game() { }

    public Game(long igdbId, string name, string slug, string? summary, string? coverUrl, DateOnly? releaseDate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(igdbId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        IgdbId = igdbId;
        Name = name;
        Slug = slug;
        Summary = summary;
        CoverUrl = coverUrl;
        ReleaseDate = releaseDate;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public long IgdbId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Summary { get; private set; }
    public string? CoverUrl { get; private set; }
    public DateOnly? ReleaseDate { get; private set; }
    public ICollection<GameGenre> Genres { get; } = new List<GameGenre>();
    public ICollection<GamePlatform> Platforms { get; } = new List<GamePlatform>();
}
