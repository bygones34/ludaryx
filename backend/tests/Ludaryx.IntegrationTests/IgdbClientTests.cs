using System.Net;
using System.Text;
using Ludaryx.Application.Games;
using Ludaryx.Infrastructure.Igdb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ludaryx.IntegrationTests;

public class IgdbClientTests
{
    [Fact]
    public async Task Search_maps_metadata_and_sends_a_bounded_escaped_query()
    {
        using var harness = new Harness(async (request, cancellationToken) =>
        {
            Assert.Equal("https://api.igdb.com/v4/games", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-client-id", request.Headers.GetValues("Client-ID").Single());
            Assert.Equal("test-access-token", request.Headers.Authorization!.Parameter);
            var query = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.StartsWith("search \"Halo\\\"; limit 999;\";", query);
            Assert.EndsWith("limit 20;", query);
            return Json("""
                [{"id":1,"name":"Halo","cover":{"image_id":"co123"},
                  "first_release_date":0,"platforms":[{"id":6,"name":"PC"}]}]
                """);
        });

        var items = await harness.Client.SearchAsync("  Halo\"; limit 999;  ", CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(1, item.IgdbId);
        Assert.Equal("Halo", item.Name);
        Assert.Equal(new DateOnly(1970, 1, 1), item.ReleaseDate);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co123.jpg", item.CoverUrl);
        Assert.Equal(["PC"], item.Platforms);
    }

    [Fact]
    public async Task Details_map_nested_metadata_and_optional_fields()
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("""
            [{"id":42,"name":"Example","slug":"example","summary":"Summary",
              "genres":[{"id":12,"name":"Role-playing"}],
              "platforms":[{"id":6,"name":"PC","abbreviation":"PC"}]}]
            """)));

        var game = await harness.Client.GetDetailsAsync(42, CancellationToken.None);

        Assert.NotNull(game);
        Assert.Equal("example", game.Slug);
        Assert.Equal("Summary", game.Summary);
        Assert.Null(game.CoverUrl);
        Assert.Null(game.ReleaseDate);
        Assert.Equal(new GameGenre(12, "Role-playing"), Assert.Single(game.Genres));
        Assert.Equal(new GamePlatform(6, "PC", "PC"), Assert.Single(game.Platforms));
    }

    [Fact]
    public async Task Missing_metadata_is_nullable_and_unknown_game_returns_null()
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("""
            [{"id":42,"name":"Example","slug":"example"}]
            """)));
        var game = await harness.Client.GetDetailsAsync(42, CancellationToken.None);
        Assert.NotNull(game);
        Assert.Null(game.Summary);
        Assert.Empty(game.Genres);
        Assert.Empty(game.Platforms);

        using var missing = new Harness((_, _) => Task.FromResult(Json("[]")));
        Assert.Null(await missing.Client.GetDetailsAsync(42, CancellationToken.None));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"id\":42}]")]
    [InlineData("[{\"id\":42,\"name\":\"Example\",\"slug\":\"example\",\"first_release_date\":9223372036854775807}]")]
    public async Task Malformed_metadata_becomes_a_safe_provider_failure(string response)
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json(response)));
        var exception = await Assert.ThrowsAsync<GameProviderException>(() =>
            harness.Client.GetDetailsAsync(42, CancellationToken.None));
        Assert.Equal(GameProviderFailure.InvalidResponse, exception.Failure);
        Assert.DoesNotContain(response, exception.Message);
    }

    [Theory]
    [InlineData(429, GameProviderFailure.Unavailable)]
    [InlineData(503, GameProviderFailure.Unavailable)]
    [InlineData(403, GameProviderFailure.Authentication)]
    [InlineData(400, GameProviderFailure.InvalidResponse)]
    public async Task Upstream_errors_are_classified_without_retry_or_raw_error_leakage(
        int status, GameProviderFailure expected)
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("private provider error", status)));
        var exception = await Assert.ThrowsAsync<GameProviderException>(() =>
            harness.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(expected, exception.Failure);
        Assert.DoesNotContain("private provider error", exception.Message);
        Assert.Equal(1, harness.GameHandler.Calls);
    }

    [Fact]
    public async Task Unauthorized_request_renews_token_and_retries_once()
    {
        var receivedTokens = new List<string?>();
        var tokenNumber = 0;
        using var harness = new Harness((request, _) =>
        {
            receivedTokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(receivedTokens.Count == 1 ? Json("{}", 401) : Json("[]"));
        }, (_, _) => Task.FromResult(Token($"test-token-{Interlocked.Increment(ref tokenNumber)}")));

        Assert.Empty(await harness.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(["test-token-1", "test-token-2"], receivedTokens);
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Fact]
    public async Task Repeated_unauthorized_response_stops_after_one_retry()
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("{}", 401)));
        var exception = await Assert.ThrowsAsync<GameProviderException>(() =>
            harness.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(GameProviderFailure.Authentication, exception.Failure);
        Assert.Equal(2, harness.GameHandler.Calls);
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Fact]
    public async Task Concurrent_token_requests_share_acquisition_and_renew_before_expiry()
    {
        var clock = new AdjustableClock();
        using var harness = new Harness((_, _) => Task.FromResult(Json("[]")), async (_, cancellationToken) =>
        {
            await Task.Delay(50, cancellationToken);
            return Token("test-access-token");
        }, time: clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => harness.Tokens.GetTokenAsync(CancellationToken.None)));
        Assert.All(results, token => Assert.Equal("test-access-token", token));
        Assert.Equal(1, harness.TokenHandler.Calls);

        clock.Advance(TimeSpan.FromSeconds(3541));
        await harness.Tokens.GetTokenAsync(CancellationToken.None);
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Fact]
    public async Task Late_rejection_of_an_old_token_does_not_invalidate_its_replacement()
    {
        var number = 0;
        using var harness = new Harness((_, _) => Task.FromResult(Json("[]")),
            (_, _) => Task.FromResult(Token($"test-token-{Interlocked.Increment(ref number)}")));
        var oldToken = await harness.Tokens.GetTokenAsync(CancellationToken.None);
        await harness.Tokens.InvalidateAsync(oldToken, CancellationToken.None);
        var replacement = await harness.Tokens.GetTokenAsync(CancellationToken.None);
        await harness.Tokens.InvalidateAsync(oldToken, CancellationToken.None);
        Assert.Equal(replacement, await harness.Tokens.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Fact]
    public async Task Token_credentials_are_sent_in_form_body_and_never_in_uri()
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("[]")), async (request, cancellationToken) =>
        {
            Assert.Equal("https://id.twitch.tv/oauth2/token", request.RequestUri!.AbsoluteUri);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            var form = await request.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("client_id=test-client-id", form);
            Assert.Contains("client_secret=test-client-secret", form);
            Assert.Contains("grant_type=client_credentials", form);
            return Token("test-access-token");
        });
        await harness.Client.SearchAsync("Halo", CancellationToken.None);
    }

    [Theory]
    [InlineData("{}", 200, GameProviderFailure.InvalidResponse)]
    [InlineData("{}", 401, GameProviderFailure.Authentication)]
    public async Task Invalid_token_response_does_not_send_a_metadata_request(
        string response, int status, GameProviderFailure failure)
    {
        using var harness = new Harness((_, _) => Task.FromResult(Json("[]")),
            (_, _) => Task.FromResult(Json(response, status)));
        var exception = await Assert.ThrowsAsync<GameProviderException>(() =>
            harness.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(failure, exception.Failure);
        Assert.Equal(0, harness.GameHandler.Calls);
    }

    [Fact]
    public async Task Network_failure_and_timeout_are_classified_but_caller_cancellation_is_preserved()
    {
        using var unavailable = new Harness((_, _) => throw new HttpRequestException());
        var network = await Assert.ThrowsAsync<GameProviderException>(() =>
            unavailable.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(GameProviderFailure.Unavailable, network.Failure);

        using var timeout = new Harness((_, _) => throw new TaskCanceledException());
        var timedOut = await Assert.ThrowsAsync<GameProviderException>(() =>
            timeout.Client.SearchAsync("Halo", CancellationToken.None));
        Assert.Equal(GameProviderFailure.Timeout, timedOut.Failure);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            timeout.Client.SearchAsync("Halo", cancellation.Token));
    }

    [Fact]
    public void Registered_client_has_safe_fixed_endpoints_timeout_and_missing_configuration_error()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection().AddLogging().AddIgdb(configuration);
        using var provider = services.BuildServiceProvider();
        using var tokenClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(IgdbTokenProvider.HttpClientName);
        Assert.Equal(new Uri("https://id.twitch.tv/"), tokenClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(10), tokenClient.Timeout);
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IIgdbClient>());
        Assert.Contains("Igdb:ClientId and Igdb:ClientSecret", exception.Message);
    }

    [Fact]
    public async Task Outbound_limit_serializes_start_times_and_caps_concurrent_leases()
    {
        using var limiter = new IgdbRequestLimiter(TimeProvider.System);
        var start = TimeProvider.System.GetTimestamp();
        var leases = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 8; i++) leases.Add(await limiter.AcquireAsync(CancellationToken.None));
            Assert.True(TimeProvider.System.GetElapsedTime(start) >= TimeSpan.FromMilliseconds(1700));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.AcquireAsync(cancellation.Token));
            leases[0].Dispose();
            using var next = await limiter.AcquireAsync(CancellationToken.None);
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }
    }

    private static HttpResponseMessage Json(string body, int status = 200) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Token(string token) => Json(
        $$"""{"access_token":"{{token}}","expires_in":3600,"token_type":"bearer"}""");

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return send(request, cancellationToken);
        }
    }

    private sealed class StubFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://id.twitch.tv/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class Harness : IDisposable
    {
        public StubHandler GameHandler { get; }
        public StubHandler TokenHandler { get; }
        public IgdbTokenProvider Tokens { get; }
        public IgdbClient Client { get; }
        private readonly HttpClient http;
        private readonly IgdbRequestLimiter limiter;

        public Harness(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> games,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? token = null,
            TimeProvider? time = null)
        {
            GameHandler = new StubHandler(games);
            TokenHandler = new StubHandler(token ?? ((_, _) => Task.FromResult(Token("test-access-token"))));
            var settings = new IgdbSettings("test-client-id", "test-client-secret");
            Tokens = new IgdbTokenProvider(new StubFactory(TokenHandler), settings,
                time ?? TimeProvider.System, NullLogger<IgdbTokenProvider>.Instance);
            limiter = new IgdbRequestLimiter(TimeProvider.System);
            http = new HttpClient(GameHandler) { BaseAddress = new Uri("https://api.igdb.com/v4/") };
            Client = new IgdbClient(http, settings, Tokens, limiter, NullLogger<IgdbClient>.Instance);
        }

        public void Dispose()
        {
            Tokens.Dispose();
            limiter.Dispose();
            http.Dispose();
            TokenHandler.Dispose();
        }
    }
}
