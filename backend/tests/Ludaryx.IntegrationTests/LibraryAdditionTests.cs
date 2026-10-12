using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Ludaryx.Application.Games;
using Ludaryx.Application.Library.AddGame;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class LibraryAdditionTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    private static GameDetails Snapshot(long id) => new(id, $"Game {id}", $"game-{id}", null, null,
        new DateOnly(2022, 2, 25), [new GameGenre(12, "RPG")], [new GamePlatform(6, "PC", null)]);

    [Fact]
    public async Task Addition_returns_201_and_persists_snapshot_and_authenticated_ownership()
    {
        await using var harness = await CreateHarness(new StubProvider());
        using var response = await harness.Client.PostAsJsonAsync("/api/v1/library",
            new { igdbId = 119133, status = "Backlog", priority = "High", userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = body.RootElement;
        Assert.Equal($"/api/v1/library/{item.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.Equal("Backlog", item.GetProperty("status").GetString());
        Assert.Equal("High", item.GetProperty("priority").GetString());
        Assert.Equal(1, item.GetProperty("backlogOrder").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("rating").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("personalNote").ValueKind);
        Assert.Equal(119133, item.GetProperty("game").GetProperty("igdbId").GetInt64());
        Assert.Equal("Game 119133", item.GetProperty("game").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("game").GetProperty("coverUrl").ValueKind);
        Assert.True(item.GetProperty("addedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        await using var context = database.CreateContext();
        var saved = await context.UserGames.Include(item => item.Game).SingleAsync();
        Assert.Equal(harness.UserId, saved.UserId);
        Assert.Equal(item.GetProperty("game").GetProperty("id").GetGuid(), saved.GameId);
        Assert.Equal(new DateOnly(2022, 2, 25), saved.Game.ReleaseDate);
        Assert.Equal(1, await context.Genres.CountAsync());
        Assert.Equal(1, await context.Platforms.CountAsync());
        Assert.Equal(1, await context.GameGenres.CountAsync());
        Assert.Equal(1, await context.GamePlatforms.CountAsync());
    }

    [Fact]
    public async Task Duplicate_returns_409_without_calling_provider_again()
    {
        var provider = new StubProvider();
        await using var harness = await CreateHarness(provider);
        using var first = await Add(harness.Client, 119133);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        provider.Failure = GameProviderFailure.Unavailable;
        using var duplicate = await Add(harness.Client, 119133);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, provider.Calls);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.UserGames.CountAsync());
    }

    [Fact]
    public async Task Another_user_reuses_snapshot_even_when_IGDB_is_unavailable()
    {
        var provider = new StubProvider();
        await using var harness = await CreateHarness(provider);
        using var first = await Add(harness.Client, 119133);
        provider.Failure = GameProviderFailure.Unavailable;
        using var secondClient = await harness.AddUserClient();
        using var second = await Add(secondClient, 119133);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, provider.Calls);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.Games.CountAsync());
        Assert.Equal(2, await context.UserGames.CountAsync());
    }

    [Fact]
    public async Task Different_games_reuse_genres_and_platforms()
    {
        await using var harness = await CreateHarness(new StubProvider());
        using var first = await Add(harness.Client, 119133);
        using var second = await Add(harness.Client, 1942);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.Games.CountAsync());
        Assert.Equal(1, await context.Genres.CountAsync());
        Assert.Equal(1, await context.Platforms.CountAsync());
        Assert.Equal(2, await context.GameGenres.CountAsync());
        Assert.Equal(2, await context.GamePlatforms.CountAsync());
        Assert.Equal(new int?[] { 1, 2 }, await context.UserGames.OrderBy(item => item.BacklogOrder).Select(item => item.BacklogOrder).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_invalid_token_cannot_add(bool invalidToken)
    {
        var provider = new StubProvider();
        await using var harness = await CreateHarness(provider);
        harness.Client.DefaultRequestHeaders.Authorization = invalidToken
            ? new AuthenticationHeaderValue("Bearer", "invalid-token") : null;
        using var response = await Add(harness.Client, 119133);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(0, "Backlog", null)]
    [InlineData(-1, "Backlog", null)]
    [InlineData(1, null, null)]
    [InlineData(1, "Invalid", null)]
    [InlineData(1, "1", null)]
    [InlineData(1, "Backlog", "Invalid")]
    [InlineData(1, "Playing", "High")]
    public async Task Invalid_input_returns_400_without_provider_or_database_writes(long id, string? status, string? priority)
    {
        var provider = new StubProvider();
        await using var harness = await CreateHarness(provider);
        using var response = await harness.Client.PostAsJsonAsync("/api/v1/library", new { igdbId = id, status, priority });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, provider.Calls);
        await using var context = database.CreateContext();
        Assert.False(await context.Games.AnyAsync());
    }

    [Theory]
    [InlineData("Playing")]
    [InlineData("Completed")]
    [InlineData("OnHold")]
    [InlineData("Dropped")]
    public async Task Non_backlog_additions_have_no_priority_or_order(string status)
    {
        await using var harness = await CreateHarness(new StubProvider());
        using var response = await harness.Client.PostAsJsonAsync("/api/v1/library", new { igdbId = 119133, status });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("priority").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("backlogOrder").ValueKind);
        await using var context = database.CreateContext();
        var saved = await context.UserGames.SingleAsync();
        Assert.Equal(status == "Playing", saved.StartedAt is not null);
        Assert.Equal(status == "Completed", saved.CompletedAt is not null);
    }

    [Theory]
    [InlineData(GameProviderFailure.Unavailable, 503)]
    [InlineData(GameProviderFailure.Timeout, 504)]
    [InlineData(GameProviderFailure.Authentication, 502)]
    [InlineData(GameProviderFailure.InvalidResponse, 502)]
    public async Task Provider_failures_return_safe_errors_and_leave_no_snapshot(GameProviderFailure failure, int expected)
    {
        await using var harness = await CreateHarness(new StubProvider { Failure = failure });
        using var response = await Add(harness.Client, 119133);
        Assert.Equal(expected, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ClientSecret", text);
        Assert.DoesNotContain("access_token", text);
        await using var context = database.CreateContext();
        Assert.False(await context.Games.AnyAsync());
        Assert.False(await context.UserGames.AnyAsync());
    }

    [Fact]
    public async Task Unknown_game_returns_404_without_writing_metadata()
    {
        await using var harness = await CreateHarness(new StubProvider { Missing = true });
        using var response = await Add(harness.Client, 119133);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var context = database.CreateContext();
        Assert.False(await context.Games.AnyAsync());
    }

    [Fact]
    public async Task Deleted_user_token_cannot_create_a_library_record()
    {
        var provider = new StubProvider();
        await using var harness = await CreateHarness(provider);
        await using (var context = database.CreateContext()) await context.Users.ExecuteDeleteAsync();
        using var response = await Add(harness.Client, 119133);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_additions_of_one_game_preserve_uniqueness(bool sameUser)
    {
        var provider = new StubProvider { SynchronizeTwoCalls = true, DistinctSnapshotNames = true };
        await using var harness = await CreateHarness(provider);
        using var secondClient = sameUser ? harness.ClientFor(harness.UserId) : await harness.AddUserClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await Task.WhenAll(Add(harness.Client, 119133, timeout.Token), Add(secondClient, 119133, timeout.Token));
        try
        {
            Assert.Equal(sameUser ? 1 : 2, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(sameUser ? 1 : 0, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            await using var context = database.CreateContext();
            Assert.Equal(1, await context.Games.CountAsync());
            Assert.Equal(1, await context.Genres.CountAsync());
            Assert.Equal(1, await context.Platforms.CountAsync());
            Assert.Equal(1, await context.GameGenres.CountAsync());
            Assert.Equal(1, await context.GamePlatforms.CountAsync());
            Assert.Equal(sameUser ? 1 : 2, await context.UserGames.CountAsync());
            var name = (await context.Games.SingleAsync()).Name;
            foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.Created))
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(name, body.RootElement.GetProperty("game").GetProperty("name").GetString());
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Concurrent_different_games_for_one_user_receive_distinct_backlog_orders()
    {
        await using var harness = await CreateHarness(new StubProvider { SynchronizeTwoCalls = true });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await Task.WhenAll(Add(harness.Client, 119133, timeout.Token), Add(harness.Client, 1942, timeout.Token));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            await using var context = database.CreateContext();
            Assert.Equal(new int?[] { 1, 2 }, await context.UserGames.OrderBy(item => item.BacklogOrder).Select(item => item.BacklogOrder).ToArrayAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Failed_snapshot_transaction_rolls_back_every_partial_insert()
    {
        await using var harness = await CreateHarness(new StubProvider());
        await using var context = database.CreateContext();
        var store = new EfLibraryAdditionStore(context, TimeProvider.System);
        var malformed = Snapshot(119133) with { Platforms = [new GamePlatform(0, "Invalid", null)] };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddAsync(
            new LibraryAddition(harness.UserId, 119133, GameStatus.Backlog, null, malformed), CancellationToken.None));
        await using var verification = database.CreateContext();
        Assert.False(await verification.Games.AnyAsync());
        Assert.False(await verification.Genres.AnyAsync());
        Assert.False(await verification.GameGenres.AnyAsync());
        Assert.False(await verification.UserGames.AnyAsync());
    }

    [Fact]
    public async Task Concurrent_different_games_for_different_users_share_metadata()
    {
        await using var harness = await CreateHarness(new StubProvider { SynchronizeTwoCalls = true });
        using var secondClient = await harness.AddUserClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await Task.WhenAll(Add(harness.Client, 119133, timeout.Token), Add(secondClient, 1942, timeout.Token));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            await using var context = database.CreateContext();
            Assert.Equal(2, await context.Games.CountAsync());
            Assert.Equal(1, await context.Genres.CountAsync());
            Assert.Equal(1, await context.Platforms.CountAsync());
            Assert.Equal(2, await context.GameGenres.CountAsync());
            Assert.Equal(2, await context.GamePlatforms.CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, long id, CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync("/api/v1/library", new { igdbId = id, status = "Backlog" }, cancellationToken);

    private async Task<Harness> CreateHarness(StubProvider provider)
    {
        await using var context = database.CreateContext();
        var connectionString = context.Database.GetConnectionString();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LudaryxDatabase"] = connectionString,
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIgdbClient>();
                services.AddSingleton<IIgdbClient>(provider);
                // Replace the scoped context explicitly; startup captures its connection string before test configuration is applied.
                services.RemoveAll<LudaryxDbContext>();
                services.AddScoped(_ => new LudaryxDbContext(new DbContextOptionsBuilder<LudaryxDbContext>()
                    .UseNpgsql(connectionString).Options));
            });
        });
        var harness = new Harness(factory, database);
        harness.Client = await harness.AddUserClient();
        return harness;
    }

    private sealed class Harness(WebApplicationFactory<Program> factory, LibraryDatabaseFixture database) : IAsyncDisposable
    {
        public HttpClient Client { get; set; } = null!;
        public Guid UserId { get; private set; }

        public async Task<HttpClient> AddUserClient()
        {
            var id = Guid.NewGuid();
            var name = $"user{id:N}"[..20];
            await using var context = database.CreateContext();
            context.Users.Add(new ApplicationUser
            {
                Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.com", NormalizedEmail = $"{name}@EXAMPLE.COM", DisplayName = name
            });
            await context.SaveChangesAsync();
            if (UserId == Guid.Empty) UserId = id;
            return ClientFor(id);
        }

        public HttpClient ClientFor(Guid id)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var settings = factory.Services.GetRequiredService<JwtSettings>();
            var now = DateTime.UtcNow;
            var jwt = new JwtSecurityToken(settings.Issuer, settings.Audience, [new Claim("sub", id.ToString())],
                now, now.AddMinutes(15), new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            factory.Dispose();
            // This fixture owns a uniquely named test database, never the development database.
            await using var context = database.CreateContext();
            await context.UserGames.ExecuteDeleteAsync();
            await context.GameGenres.ExecuteDeleteAsync();
            await context.GamePlatforms.ExecuteDeleteAsync();
            await context.Games.ExecuteDeleteAsync();
            await context.Genres.ExecuteDeleteAsync();
            await context.Platforms.ExecuteDeleteAsync();
            await context.Users.ExecuteDeleteAsync();
        }
    }

    private sealed class StubProvider : IIgdbClient
    {
        private int calls;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref calls);
        public bool SynchronizeTwoCalls { get; init; }
        public bool DistinctSnapshotNames { get; init; }
        public bool Missing { get; init; }
        public GameProviderFailure? Failure { get; set; }

        public async Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref calls);
            if (SynchronizeTwoCalls)
            {
                if (count == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            if (Failure is { } failure) throw new GameProviderException(failure);
            if (Missing) return null;
            var snapshot = Snapshot(igdbId);
            return DistinctSnapshotNames ? snapshot with { Name = $"Snapshot {count}" } : snapshot;
        }

        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
