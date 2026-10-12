namespace Ludaryx.Domain.Games;

public sealed class Genre
{
    private Genre() { }

    public Genre(long igdbId, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(igdbId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        IgdbId = igdbId;
        Name = name;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public long IgdbId { get; private set; }
    public string Name { get; private set; } = null!;
}
