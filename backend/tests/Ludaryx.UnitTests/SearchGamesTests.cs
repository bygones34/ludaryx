using Ludaryx.Application.Games;
using Ludaryx.Application.Games.SearchGames;

namespace Ludaryx.UnitTests;

public class SearchGamesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" a ")]
    [InlineData("el\n den")]
    public async Task Invalid_queries_do_not_call_the_provider(string? query)
    {
        var provider = new StubClient();
        var handler = new SearchGamesHandler(new SearchGamesValidator(), provider);

        var result = await handler.HandleAsync(new SearchGamesQuery(query), CancellationToken.None);

        var invalid = Assert.IsType<SearchGamesResult.Invalid>(result);
        Assert.NotEmpty(invalid.Errors["q"]);
        Assert.Null(provider.Query);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public async Task Boundary_queries_are_trimmed_and_cancellation_is_forwarded(int length)
    {
        var provider = new StubClient();
        var handler = new SearchGamesHandler(new SearchGamesValidator(), provider);
        using var cancellation = new CancellationTokenSource();
        var query = new string('a', length);

        Assert.IsType<SearchGamesResult.Found>(await handler.HandleAsync(
            new SearchGamesQuery($"  {query}  "), cancellation.Token));

        Assert.Equal(query, provider.Query);
        Assert.Equal(cancellation.Token, provider.Cancellation);
    }

    [Fact]
    public async Task Queries_longer_than_the_limit_are_rejected()
    {
        var provider = new StubClient();
        var handler = new SearchGamesHandler(new SearchGamesValidator(), provider);
        Assert.IsType<SearchGamesResult.Invalid>(await handler.HandleAsync(
            new SearchGamesQuery(new string('a', 101)), CancellationToken.None));
        Assert.Null(provider.Query);
    }

    [Fact]
    public async Task Results_are_limited_to_twenty_items()
    {
        var provider = new StubClient
        {
            Items = Enumerable.Range(1, 25).Select(id => new GameSearchItem(id, $"Game {id}", null, null, [])).ToArray()
        };
        var handler = new SearchGamesHandler(new SearchGamesValidator(), provider);
        var result = Assert.IsType<SearchGamesResult.Found>(await handler.HandleAsync(
            new SearchGamesQuery("elden ring"), CancellationToken.None));
        Assert.Equal(20, result.Items.Count);
    }

    private sealed class StubClient : IIgdbClient
    {
        public string? Query { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public IReadOnlyList<GameSearchItem> Items { get; init; } = [];

        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Query = query;
            Cancellation = cancellationToken;
            return Task.FromResult(Items);
        }

        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
