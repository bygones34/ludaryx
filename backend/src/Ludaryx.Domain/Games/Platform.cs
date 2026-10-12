namespace Ludaryx.Domain.Games;

public sealed class Platform
{
    private Platform() { }

    public Platform(long igdbId, string name, string? abbreviation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(igdbId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        IgdbId = igdbId;
        Name = name;
        Abbreviation = abbreviation;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public long IgdbId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Abbreviation { get; private set; }
}
