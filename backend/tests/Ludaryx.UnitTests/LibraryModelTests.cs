using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;

namespace Ludaryx.UnitTests;

public class LibraryModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Metadata_rejects_nonpositive_external_IDs(long id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Game(id, "Game", "game", null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Genre(id, "Genre"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Platform(id, "Platform", null));
    }

    [Fact]
    public void Metadata_requires_names_and_slug_without_fabricating_optional_values()
    {
        Assert.Throws<ArgumentException>(() => new Game(1, " ", "game", null, null, null));
        Assert.Throws<ArgumentException>(() => new Game(1, "Game", " ", null, null, null));
        Assert.Throws<ArgumentException>(() => new Genre(1, " "));
        Assert.Throws<ArgumentException>(() => new Platform(1, " ", null));
        var game = new Game(1, "Game", "game", null, null, null);
        Assert.Null(game.Summary);
        Assert.Null(game.CoverUrl);
        Assert.Null(game.ReleaseDate);
        Assert.Empty(game.Genres);
        Assert.Empty(game.Platforms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Backlog_requires_a_positive_order(int? order)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(GameStatus.Backlog, null, order));
    }

    [Theory]
    [InlineData(GameStatus.Playing)]
    [InlineData(GameStatus.Completed)]
    [InlineData(GameStatus.OnHold)]
    [InlineData(GameStatus.Dropped)]
    public void Non_backlog_records_reject_priority_and_order(GameStatus status)
    {
        Assert.Throws<ArgumentException>(() => Create(status, BacklogPriority.High, null));
        Assert.Throws<ArgumentException>(() => Create(status, null, 1));
    }

    [Fact]
    public void Invalid_enum_values_and_empty_owners_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((GameStatus)100, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(GameStatus.Backlog, (BacklogPriority)100, 1));
        Assert.Throws<ArgumentException>(() => new UserGame(Guid.Empty, Guid.NewGuid(),
            GameStatus.Playing, null, null, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(GameStatus.Backlog)]
    [InlineData(GameStatus.Playing)]
    [InlineData(GameStatus.Completed)]
    public void Initial_timestamps_are_UTC_and_reflect_the_initial_status(GameStatus status)
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(3));
        var item = new UserGame(Guid.NewGuid(), Guid.NewGuid(), status, null,
            status == GameStatus.Backlog ? 1 : null, now);
        Assert.Equal(TimeSpan.Zero, item.AddedAt.Offset);
        Assert.Equal(now.ToUniversalTime(), item.AddedAt);
        Assert.Equal(item.AddedAt, item.UpdatedAt);
        Assert.Equal(status == GameStatus.Playing ? item.AddedAt : (DateTimeOffset?)null, item.StartedAt);
        Assert.Equal(status == GameStatus.Completed ? item.AddedAt : (DateTimeOffset?)null, item.CompletedAt);
        Assert.Null(item.Rating);
        Assert.Null(item.PersonalNote);
    }

    private static UserGame Create(GameStatus status, BacklogPriority? priority, int? order) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, priority, order, DateTimeOffset.UtcNow);
}
