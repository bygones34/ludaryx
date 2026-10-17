using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Ludaryx.Application.Dashboard.GetDashboard;
using Ludaryx.Application.Games;
using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class DashboardTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    private static long nextIgdbId = 20000;
    private static readonly DateTimeOffset Added = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Endpoint_requires_authentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_owner_returns_zero_statistics_null_average_and_empty_lists()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        using var response = await client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardResponse>())!;
        Assert.Equal(new DashboardStatistics(0, 0, 0, 0, 0, 0, null), dashboard.Statistics);
        Assert.Empty(dashboard.CurrentlyPlaying);
        Assert.Empty(dashboard.UpNext);
    }

    [Fact]
    public async Task Endpoint_reads_owned_persisted_summaries_and_rounds_the_average()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var owner = Guid.Parse(token.Subject);
        await using var context = database.CreateContext();
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var items = new List<UserGame>();
        var statuses = new[] { GameStatus.Playing, GameStatus.Completed, GameStatus.OnHold, GameStatus.Dropped, GameStatus.Backlog };
        for (var index = 0; index < statuses.Length; index++)
        {
            var game = new Game(10001 + index, $"Dashboard game {index}", $"dashboard-game-{index}", null, null, null);
            var status = statuses[index];
            var order = status == GameStatus.Backlog ? (int?)7 : null;
            var item = new UserGame(owner, game.Id, status, null, order, now);
            item.Update(status, null, order, index == 4 ? null : index == 3 ? 9 : 8, "Private note", now);
            context.AddRange(game, item);
            items.Add(item);
        }
        await context.SaveChangesAsync();

        using var response = await client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardResponse>())!;
        Assert.Equal(new DashboardStatistics(5, 1, 1, 1, 1, 1, 8.3m), dashboard.Statistics);
        Assert.Equal(new CurrentlyPlayingItem(items[0].Id, 10001, "Dashboard game 0", null, 8), Assert.Single(dashboard.CurrentlyPlaying));
        Assert.Equal(new UpNextItem(items[4].Id, 10005, "Dashboard game 4", null, null, 7), Assert.Single(dashboard.UpNext));
        Assert.DoesNotContain("Private note", await response.Content.ReadAsStringAsync());

        using var otherClient = await ClientForNewUser(factory);
        var other = (await otherClient.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;
        Assert.Equal(0, other.Statistics.TotalGames);
        Assert.Null(other.Statistics.AverageRating);
        Assert.Empty(other.CurrentlyPlaying);
        Assert.Empty(other.UpNext);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Signed_token_without_valid_owner_is_rejected(string? subject)
    {
        using var factory = CreateFactory();
        using var client = ClientForClaim(factory, subject);
        using var response = await client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static TheoryData<int?[], decimal?> RatingCases => new()
    {
        { [null], null },
        { [null, null, null], null },
        { [1], 1m },
        { [10], 10m },
        { [8, null], 8m },
        { [8, 8, 8, 9, null], 8.3m },
        { [8, 8, 8, 7], 7.8m },
        { [1, 1, 2], 1.3m },
        { [9, 9, 10], 9.3m }
    };

    [Theory]
    [MemberData(nameof(RatingCases))]
    public async Task Average_handles_single_unrated_and_rounding_boundaries(int?[] ratings, decimal? expected)
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        var owner = Owner(client);
        await Seed(owner, ratings.Select((rating, index) => new ItemSeed((GameStatus)(index % 5), rating,
            Order: index + 1)).ToArray());
        var result = await Get(client);
        Assert.Equal(ratings.Length, result.Statistics.TotalGames);
        Assert.Equal(expected, result.Statistics.AverageRating);
        Assert.Equal(ratings.Length, result.Statistics.Playing + result.Statistics.Completed + result.Statistics.Backlog
            + result.Statistics.OnHold + result.Statistics.Dropped);
    }

    [Fact]
    public async Task Previews_are_limited_but_statistics_include_every_record_and_shared_metadata_does_not_duplicate()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        var items = await Seed(Owner(client), Enumerable.Range(0, 7).Select(index =>
            new ItemSeed(GameStatus.Playing, 10, Started: Added.AddDays(index))).Concat(
            Enumerable.Range(0, 7).Select(index => new ItemSeed(GameStatus.Backlog, 2, Order: index * 3 + 1,
                Priority: index == 0 ? BacklogPriority.Low : BacklogPriority.High))).ToArray());
        await using (var context = database.CreateContext())
        {
            var game = await context.Games.SingleAsync(game => game.Id == items[0].GameId);
            foreach (var id in new long[] { 99001, 99002 })
            {
                var genre = new Genre(id, $"Dashboard genre {id}");
                var platform = new Platform(id, $"Dashboard platform {id}", null);
                context.AddRange(genre, platform);
                game.Genres.Add(new Ludaryx.Domain.Games.GameGenre(game.Id, genre.Id));
                game.Platforms.Add(new Ludaryx.Domain.Games.GamePlatform(game.Id, platform.Id));
            }
            await context.SaveChangesAsync();
        }
        var result = await Get(client);
        Assert.Equal(new DashboardStatistics(14, 7, 0, 7, 0, 0, 6m), result.Statistics);
        Assert.Equal(items.Take(7).Reverse().Take(5).Select(item => item.Id), result.CurrentlyPlaying.Select(item => item.UserGameId));
        Assert.Equal(items.Skip(7).Take(5).Select(item => item.Id), result.UpNext.Select(item => item.UserGameId));
        Assert.Equal(new[] { 1, 4, 7, 10, 13 }, result.UpNext.Select(item => item.Order));
        Assert.Equal("Low", result.UpNext[0].Priority);
        Assert.All(result.CurrentlyPlaying, item => Assert.NotNull(item.CoverUrl));
    }

    [Fact]
    public async Task Playing_order_uses_first_start_then_added_date_then_id_with_null_starts_last()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        var lowId = Guid.Parse("00000001-0000-0000-0000-000000000001");
        var highId = Guid.Parse("00000002-0000-0000-0000-000000000001");
        var items = await Seed(Owner(client),
            new(GameStatus.Playing, Started: Added.AddDays(2), AddedAt: Added, Id: highId),
            new(GameStatus.Playing, Started: Added.AddDays(2), AddedAt: Added, Id: lowId),
            new(GameStatus.Playing, Started: Added.AddDays(2), AddedAt: Added.AddDays(1)),
            new(GameStatus.Playing, Started: Added.AddDays(3)),
            new(GameStatus.Playing, NullStart: true, AddedAt: Added.AddDays(9)));
        var expected = new[] { items[3].Id, items[2].Id, lowId, highId, items[4].Id };
        Assert.Equal(expected, (await Get(client)).CurrentlyPlaying.Select(item => item.UserGameId));
        Assert.Equal(expected, (await Get(client)).CurrentlyPlaying.Select(item => item.UserGameId));

        using var note = await client.PatchAsJsonAsync($"/api/v1/library/{lowId}", new { rating = 9, personalNote = "New note" });
        note.EnsureSuccessStatusCode();
        using var leave = await client.PatchAsJsonAsync($"/api/v1/library/{lowId}", new { status = "OnHold" });
        leave.EnsureSuccessStatusCode();
        using var resume = await client.PatchAsJsonAsync($"/api/v1/library/{lowId}", new { status = "Playing" });
        resume.EnsureSuccessStatusCode();
        Assert.Equal(expected, (await Get(client)).CurrentlyPlaying.Select(item => item.UserGameId));
        await using var context = database.CreateContext();
        Assert.Equal(Added.AddDays(2), (await context.UserGames.SingleAsync(item => item.Id == lowId)).StartedAt);
    }

    [Theory]
    [InlineData(GameStatus.Backlog)]
    [InlineData(GameStatus.Playing)]
    [InlineData(GameStatus.Completed)]
    public async Task Nonempty_library_can_have_independently_empty_previews(GameStatus status)
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        await Seed(Owner(client), new ItemSeed(status));
        var result = await Get(client);
        Assert.Equal(1, result.Statistics.TotalGames);
        Assert.Null(result.Statistics.AverageRating);
        Assert.Equal(status == GameStatus.Playing ? 1 : 0, result.CurrentlyPlaying.Count);
        Assert.Equal(status == GameStatus.Backlog ? 1 : 0, result.UpNext.Count);
    }

    [Fact]
    public async Task Up_next_reflects_saved_reorders_status_changes_returns_and_deletion()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        var items = await Seed(Owner(client), new(GameStatus.Backlog, Order: 2),
            new(GameStatus.Backlog, Order: 5, Priority: BacklogPriority.High), new(GameStatus.Backlog, Order: 9));
        using var reorder = await client.PutAsJsonAsync("/api/v1/backlog/order", new
        {
            items = new[] { new { userGameId = items[2].Id, order = 1 }, new { userGameId = items[0].Id, order = 2 },
                new { userGameId = items[1].Id, order = 3 } }
        });
        reorder.EnsureSuccessStatusCode();
        Assert.Equal(new[] { items[2].Id, items[0].Id, items[1].Id }, (await Get(client)).UpNext.Select(item => item.UserGameId));
        using var leave = await client.PatchAsJsonAsync($"/api/v1/library/{items[2].Id}", new { status = "Playing" });
        leave.EnsureSuccessStatusCode();
        Assert.Equal(2, (await Get(client)).Statistics.Backlog);
        using var back = await client.PatchAsJsonAsync($"/api/v1/library/{items[2].Id}", new { status = "Backlog" });
        back.EnsureSuccessStatusCode();
        var returned = await Get(client);
        Assert.Equal(items[2].Id, returned.UpNext[^1].UserGameId);
        Assert.Equal(4, returned.UpNext[^1].Order);
        Assert.Null(returned.UpNext[^1].Priority);
        using var delete = await client.DeleteAsync($"/api/v1/library/{items[0].Id}");
        delete.EnsureSuccessStatusCode();
        var after = await Get(client);
        Assert.Equal(2, after.Statistics.TotalGames);
        Assert.Equal(new[] { items[1].Id, items[2].Id }, after.UpNext.Select(item => item.UserGameId));
    }

    [Fact]
    public async Task Shared_game_and_client_owner_parameters_cannot_leak_another_owners_data()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        using var other = await ClientForNewUser(factory);
        var mine = Assert.Single(await Seed(Owner(client), new ItemSeed(GameStatus.Playing, 2)));
        await using (var context = database.CreateContext())
        {
            var foreign = new UserGame(Owner(other), mine.GameId, GameStatus.Backlog, BacklogPriority.High, 1, Added);
            foreign.Update(GameStatus.Backlog, BacklogPriority.High, 1, 10, "Foreign secret", Added);
            context.Add(foreign);
            await context.SaveChangesAsync();
        }
        var response = (await client.GetFromJsonAsync<DashboardResponse>($"/api/v1/dashboard?userId={Owner(other)}"))!;
        Assert.Equal(new DashboardStatistics(1, 1, 0, 0, 0, 0, 2m), response.Statistics);
        Assert.Equal(mine.Id, Assert.Single(response.CurrentlyPlaying).UserGameId);
        Assert.Empty(response.UpNext);
        var theirs = await Get(other);
        Assert.Equal(10m, theirs.Statistics.AverageRating);
        Assert.Empty(theirs.CurrentlyPlaying);
        Assert.Equal("High", Assert.Single(theirs.UpNext).Priority);
    }

    [Fact]
    public async Task Addition_rating_clear_and_priority_edits_are_visible_on_the_next_dashboard_read()
    {
        using var factory = CreateFactory();
        using var client = await ClientForNewUser(factory);
        using var other = await ClientForNewUser(factory);
        var source = Assert.Single(await Seed(Owner(other), new ItemSeed(GameStatus.Completed, 10)));
        await using var context = database.CreateContext();
        var game = await context.Games.SingleAsync(game => game.Id == source.GameId);
        using var added = await client.PostAsJsonAsync("/api/v1/library", new { igdbId = game.IgdbId, status = "Backlog" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var first = await Get(client);
        Assert.Equal(new DashboardStatistics(1, 0, 0, 1, 0, 0, null), first.Statistics);
        var item = Assert.Single(first.UpNext);
        Assert.Equal(1, item.Order);
        Assert.Null(item.Priority);
        using var update = await client.PatchAsJsonAsync($"/api/v1/library/{item.UserGameId}", new { rating = 9, priority = "High" });
        update.EnsureSuccessStatusCode();
        var rated = await Get(client);
        Assert.Equal(9m, rated.Statistics.AverageRating);
        Assert.Equal("High", Assert.Single(rated.UpNext).Priority);
        Assert.Equal(1, rated.UpNext[0].Order);
        using var clear = await client.PatchAsJsonAsync($"/api/v1/library/{item.UserGameId}", new { rating = (int?)null });
        clear.EnsureSuccessStatusCode();
        Assert.Null((await Get(client)).Statistics.AverageRating);
        Assert.Equal(10m, (await Get(other)).Statistics.AverageRating);
    }

    [Fact]
    public async Task Dashboard_is_read_only_and_uses_no_igdb_or_private_note_projection()
    {
        var observer = new ReadObserver();
        using var factory = CreateFactory(observer);
        using var client = await ClientForNewUser(factory);
        var item = Assert.Single(await Seed(Owner(client), new ItemSeed(GameStatus.Playing, 8)));
        await using var context = database.CreateContext();
        var before = await context.UserGames.AsNoTracking().SingleAsync(stored => stored.Id == item.Id);
        await Get(client);
        var after = await context.UserGames.AsNoTracking().SingleAsync(stored => stored.Id == item.Id);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.PersonalNote, after.PersonalNote);
        Assert.Equal(before.Rating, after.Rating);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.StartedAt, after.StartedAt);
        Assert.Equal(before.CompletedAt, after.CompletedAt);
        Assert.Equal(3, observer.Reads.Count);
        Assert.All(observer.Reads, sql =>
        {
            Assert.StartsWith("SELECT", sql.TrimStart());
            Assert.DoesNotContain("PersonalNote", sql);
            Assert.DoesNotContain("GameGenres", sql);
            Assert.DoesNotContain("GamePlatforms", sql);
        });
    }

    [Fact]
    public async Task Concurrent_committed_change_does_not_split_statistics_and_previews_across_snapshots()
    {
        var gate = new PreviewGate();
        using var factory = CreateFactory(gate);
        using var client = await ClientForNewUser(factory);
        var item = Assert.Single(await Seed(Owner(client), new ItemSeed(GameStatus.Backlog, 2, Order: 7)));
        var request = Get(client);
        try
        {
            await gate.PreviewReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var writer = database.CreateContext();
            var stored = await writer.UserGames.SingleAsync(value => value.Id == item.Id);
            stored.Update(GameStatus.Playing, null, null, 10, stored.PersonalNote, Added.AddDays(3));
            await writer.SaveChangesAsync();
        }
        finally { gate.Release.TrySetResult(); }
        var snapshot = await request;
        Assert.Equal(new DashboardStatistics(1, 0, 0, 1, 0, 0, 2m), snapshot.Statistics);
        Assert.Empty(snapshot.CurrentlyPlaying);
        Assert.Equal(item.Id, Assert.Single(snapshot.UpNext).UserGameId);
        var next = await Get(client);
        Assert.Equal(new DashboardStatistics(1, 1, 0, 0, 0, 0, 10m), next.Statistics);
        Assert.Equal(item.Id, Assert.Single(next.CurrentlyPlaying).UserGameId);
        Assert.Empty(next.UpNext);
    }

    private static Guid Owner(HttpClient client) => Guid.Parse(new JwtSecurityTokenHandler()
        .ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject);

    private static async Task<DashboardResponse> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;

    private sealed record ItemSeed(GameStatus Status, int? Rating = null, int Order = 1,
        BacklogPriority? Priority = null, DateTimeOffset? Started = null, DateTimeOffset? AddedAt = null,
        Guid? Id = null, bool NullStart = false);

    private async Task<UserGame[]> Seed(Guid owner, params ItemSeed[] seeds)
    {
        await using var context = database.CreateContext();
        var items = seeds.Select(seed =>
        {
            var igdbId = Interlocked.Increment(ref nextIgdbId);
            var game = new Game(igdbId, $"Game {igdbId}", $"game-{igdbId}", null, "https://images.igdb.com/cover.jpg", null);
            var order = seed.Status == GameStatus.Backlog ? (int?)seed.Order : null;
            var added = seed.AddedAt ?? Added;
            var item = new UserGame(owner, game.Id, seed.Status, seed.Priority, order, added);
            item.Update(seed.Status, seed.Priority, order, seed.Rating, "Private seeded note", added);
            context.AddRange(game, item);
            if (seed.Id is { } id) context.Entry(item).Property(value => value.Id).CurrentValue = id;
            if (seed.Status == GameStatus.Playing)
                context.Entry(item).Property(value => value.StartedAt).CurrentValue = seed.NullStart ? null : seed.Started ?? added;
            return item;
        }).ToArray();
        await context.SaveChangesAsync();
        return items;
    }

    private WebApplicationFactory<Program> CreateFactory(DbCommandInterceptor? interceptor = null)
    {
        using var context = database.CreateContext();
        var connection = context.Database.GetConnectionString();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<LudaryxDbContext>();
                services.AddScoped(_ =>
                {
                    var options = new DbContextOptionsBuilder<LudaryxDbContext>().UseNpgsql(connection);
                    if (interceptor is not null) options.AddInterceptors(interceptor);
                    return new LudaryxDbContext(options.Options);
                });
                services.RemoveAll<IIgdbClient>();
                services.AddSingleton<IIgdbClient>(new NoProvider());
            });
        });
    }

    private async Task<HttpClient> ClientForNewUser(WebApplicationFactory<Program> factory)
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
        return ClientForClaim(factory, id.ToString());
    }

    private static HttpClient ClientForClaim(WebApplicationFactory<Program> factory, string? subject)
    {
        var settings = factory.Services.GetRequiredService<JwtSettings>();
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, subject is null ? [] : [new Claim("sub", subject)],
            now, now.AddMinutes(15), new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private sealed class NoProvider : IIgdbClient
    {
        public Task<GameDetails?> GetDetailsAsync(long id, CancellationToken token) => throw new InvalidOperationException("Dashboard must not call IGDB.");
        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken token) => throw new InvalidOperationException("Dashboard must not search IGDB.");
    }

    private sealed class ReadObserver : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            Reads.Add(command.CommandText);
            Assert.StartsWith("SELECT", command.CommandText.TrimStart());
            return new(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken token = default) =>
            throw new InvalidOperationException("Dashboard must not execute writes.");
    }

    private sealed class PreviewGate : DbCommandInterceptor
    {
        private int reads;
        public TaskCompletionSource PreviewReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            // Pause before the preview, after the first query established its PostgreSQL snapshot.
            if (Interlocked.Increment(ref reads) == 2)
            {
                PreviewReached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            }
            return result;
        }
    }
}
