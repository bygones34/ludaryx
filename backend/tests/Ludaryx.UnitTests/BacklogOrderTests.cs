using Ludaryx.Domain.Library;

namespace Ludaryx.UnitTests;

public class BacklogOrderTests
{
    [Fact]
    public void Reordering_changes_only_order_and_updated_timestamp()
    {
        var added = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = new UserGame(Guid.NewGuid(), Guid.NewGuid(), GameStatus.Playing, null, null, added);
        item.Update(GameStatus.Completed, null, null, 9, "keep", added.AddDays(1));
        item.Update(GameStatus.Backlog, BacklogPriority.High, 3, 9, "keep", added.AddDays(2));
        item.Reorder(1, added.AddDays(3).ToOffset(TimeSpan.FromHours(3)));
        Assert.Equal(1, item.BacklogOrder);
        Assert.Equal(added.AddDays(3), item.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, item.UpdatedAt.Offset);
        Assert.Equal(added, item.AddedAt);
        Assert.Equal(added, item.StartedAt);
        Assert.Equal(added.AddDays(1), item.CompletedAt);
        Assert.Equal(BacklogPriority.High, item.Priority);
        Assert.Equal(9, item.Rating);
        Assert.Equal("keep", item.PersonalNote);
        Assert.Equal(GameStatus.Backlog, item.Status);
    }

    [Fact]
    public void Unchanged_order_preserves_updated_timestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new UserGame(Guid.NewGuid(), Guid.NewGuid(), GameStatus.Backlog, null, 1, now);
        item.Reorder(1, now.AddDays(1));
        Assert.Equal(now, item.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_order_does_not_change_the_item(int order)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new UserGame(Guid.NewGuid(), Guid.NewGuid(), GameStatus.Backlog, null, 2, now);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.Reorder(order, now.AddDays(1)));
        Assert.Equal(2, item.BacklogOrder);
        Assert.Equal(now, item.UpdatedAt);
    }

    [Theory]
    [InlineData(GameStatus.Playing)]
    [InlineData(GameStatus.Completed)]
    [InlineData(GameStatus.OnHold)]
    [InlineData(GameStatus.Dropped)]
    public void Non_backlog_item_cannot_be_reordered(GameStatus status)
    {
        var item = new UserGame(Guid.NewGuid(), Guid.NewGuid(), status, null, null, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => item.Reorder(1, DateTimeOffset.UtcNow));
        Assert.Null(item.BacklogOrder);
    }
}
