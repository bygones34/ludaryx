using Ludaryx.Domain.Library;

namespace Ludaryx.UnitTests;

public class LibraryUpdateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Invalid_ratings_do_not_modify_the_record(int rating)
    {
        var item = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Update(GameStatus.Playing, null, null, rating, null, DateTimeOffset.UtcNow));
        Assert.Equal(GameStatus.Backlog, item.Status);
        Assert.Equal(BacklogPriority.High, item.Priority);
        Assert.Null(item.StartedAt);
    }

    [Fact]
    public void Oversized_notes_do_not_modify_the_record()
    {
        var item = Create();
        Assert.Throws<ArgumentException>(() => item.Update(GameStatus.Playing, null, null, null, new string('x', 1001), DateTimeOffset.UtcNow));
        Assert.Equal(GameStatus.Backlog, item.Status);
    }

    [Fact]
    public void Repeated_status_transitions_preserve_first_dates_and_added_date()
    {
        var item = Create();
        var added = item.AddedAt;
        var started = added.AddDays(1);
        var completed = added.AddDays(2);
        item.Update(GameStatus.Playing, null, null, 10, "Note", started);
        item.Update(GameStatus.Completed, null, null, 10, "Note", completed);
        item.Update(GameStatus.Backlog, null, 2, null, null, added.AddDays(3));
        item.Update(GameStatus.Playing, null, null, null, null, added.AddDays(4));
        item.Update(GameStatus.Completed, null, null, null, null, added.AddDays(5));
        Assert.Equal(started, item.StartedAt);
        Assert.Equal(completed, item.CompletedAt);
        Assert.Equal(added, item.AddedAt);
        Assert.Equal(added.AddDays(5), item.UpdatedAt);
        Assert.Null(item.Priority);
        Assert.Null(item.BacklogOrder);
        Assert.Null(item.Rating);
        Assert.Null(item.PersonalNote);
    }

    private static UserGame Create() => new(Guid.NewGuid(), Guid.NewGuid(), GameStatus.Backlog,
        BacklogPriority.High, 1, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
}
