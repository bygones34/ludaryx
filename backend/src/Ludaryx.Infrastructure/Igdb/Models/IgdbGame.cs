using System.Text.Json.Serialization;

namespace Ludaryx.Infrastructure.Igdb.Models;

internal sealed class IgdbGame
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public string? Summary { get; init; }
    public IgdbCover? Cover { get; init; }
    [JsonPropertyName("first_release_date")]
    public long? FirstReleaseDate { get; init; }
    public IgdbNamedItem?[]? Genres { get; init; }
    public IgdbPlatform?[]? Platforms { get; init; }
}

internal sealed class IgdbCover
{
    [JsonPropertyName("image_id")]
    public string? ImageId { get; init; }
}

internal sealed class IgdbNamedItem
{
    public long Id { get; init; }
    public string? Name { get; init; }
}

internal sealed class IgdbPlatform
{
    public long Id { get; init; }
    public string? Name { get; init; }
    public string? Abbreviation { get; init; }
}
