using Ludaryx.Application.Games;
using Ludaryx.Application.Games.GetGameDetails;

namespace Ludaryx.UnitTests;

public class GetGameDetailsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task Invalid_ID_does_not_call_the_provider(long id)
    {
        var provider = new StubClient();
        var handler = new GetGameDetailsHandler(new GetGameDetailsValidator(), provider, new StubMembership());
        var result = Assert.IsType<GetGameDetailsResult.Invalid>(await handler.HandleAsync(
            Guid.NewGuid(), new GetGameDetailsQuery(id), CancellationToken.None));
        Assert.NotEmpty(result.Errors["igdbId"]);
        Assert.Null(provider.Id);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(long.MaxValue)]
    public async Task Valid_ID_and_cancellation_are_forwarded_and_missing_game_is_not_found(long id)
    {
        var provider = new StubClient();
        var handler = new GetGameDetailsHandler(new GetGameDetailsValidator(), provider, new StubMembership());
        using var cancellation = new CancellationTokenSource();
        Assert.IsType<GetGameDetailsResult.NotFound>(await handler.HandleAsync(
            Guid.NewGuid(), new GetGameDetailsQuery(id), cancellation.Token));
        Assert.Equal(id, provider.Id);
        Assert.Equal(cancellation.Token, provider.Cancellation);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        var provider = new StubClient();
        var handler = new GetGameDetailsHandler(new GetGameDetailsValidator(), provider, new StubMembership());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(
            Guid.NewGuid(), new GetGameDetailsQuery(119133), cancellation.Token));
    }

    private sealed class StubMembership : IGameLibraryMembership
    {
        public Task<GameLibraryMembership> GetAsync(Guid userId, long igdbId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameLibraryMembership(false, null, null));
    }

    private sealed class StubClient : IIgdbClient
    {
        public long? Id { get; private set; }
        public CancellationToken Cancellation { get; private set; }

        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Id = igdbId;
            Cancellation = cancellationToken;
            return Task.FromResult<GameDetails?>(null);
        }

        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
