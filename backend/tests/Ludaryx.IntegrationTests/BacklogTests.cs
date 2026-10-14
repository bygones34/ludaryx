using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ludaryx.Application.Backlog;
using Ludaryx.Application.Games;
using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Backlog;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Ludaryx.IntegrationTests;

public class BacklogTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    private const string Constraint = "UQ_UserGames_UserId_BacklogOrder";

    [Fact]
    public async Task Get_is_owned_ordered_and_returns_stored_summary_and_nullable_priority()
    {
        await using var h = await Create();
        var response = await Get(h.Client);
        Assert.Equal(new[] { h.A, h.B, h.C }, response.Items.Select(item => item.Id));
        Assert.Equal(new[] { 1, 3, 7 }, response.Items.Select(item => item.Order));
        Assert.Equal("High", response.Items[0].Priority);
        Assert.Null(response.Items[1].Priority);
        Assert.Equal(101, response.Items[0].Game.IgdbId);
        Assert.Equal("Game 1", response.Items[0].Game.Name);
        Assert.Null(response.Items[0].Game.CoverUrl);
        using var other = h.ClientFor(h.Other);
        Assert.Equal(h.Foreign, Assert.Single((await Get(other)).Items).Id);
    }

    [Theory]
    [InlineData("GET", "/api/v1/backlog")]
    [InlineData("PUT", "/api/v1/backlog/order")]
    public async Task Endpoints_require_authentication(string method, string path)
    {
        await using var h = await Create();
        using var client = h.ClientFor(h.User);
        client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { items = Array.Empty<object>() }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reorder_swaps_and_compacts_positions_and_preserves_personal_fields()
    {
        await using var h = await Create();
        var before = await State(h.User);
        // Array order is deliberately different from explicit order values.
        using var response = await Put(h, [new(h.A, 3), new(h.B, 2), new(h.C, 1)]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<BacklogResponse>())!;
        Assert.Equal(new[] { h.C, h.B, h.A }, saved.Items.Select(item => item.Id));
        Assert.Equal(new[] { 1, 2, 3 }, saved.Items.Select(item => item.Order));
        Assert.Equal(saved.Items, (await Get(h.Client)).Items);
        var after = await State(h.User);
        foreach (var id in new[] { h.A, h.B, h.C })
        {
            Assert.Equal(h.Now, after[id].UpdatedAt);
            Assert.Equal(before[id] with { BacklogOrder = after[id].BacklogOrder, UpdatedAt = h.Now }, after[id]);
        }
        Assert.Equal(before[h.Completed], after[h.Completed]);
    }

    [Fact]
    public async Task Only_changed_orders_receive_new_timestamps_and_repeating_order_is_idempotent()
    {
        await using var h = await Create();
        var before = await State(h.User);
        using var response = await Put(h, [new(h.A, 1), new(h.B, 2), new(h.C, 3)]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = await State(h.User);
        Assert.Equal(before[h.A], first[h.A]);
        Assert.Equal(h.Now, first[h.B].UpdatedAt);
        using var repeat = await Put(h, [new(h.C, 3), new(h.A, 1), new(h.B, 2)]);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(first.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"items\":\"bad\"}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"userGameId\":\"bad\",\"order\":1}]}")]
    [InlineData("{\"items\":[{\"userGameId\":\"00000000-0000-0000-0000-000000000000\",\"order\":1}]}")]
    [InlineData("{\"items\":[{\"order\":1}]}")]
    [InlineData("{\"items\":[{\"userGameId\":\"11111111-1111-1111-1111-111111111111\"}]}")]
    [InlineData("{\"items\":[{\"userGameId\":\"11111111-1111-1111-1111-111111111111\",\"order\":1.5}]}")]
    [InlineData("{\"items\":[{\"userGameId\":\"11111111-1111-1111-1111-111111111111\",\"order\":2147483648}]}")]
    public async Task Invalid_body_is_400_and_does_not_change_data(string body)
    {
        await using var h = await Create();
        var before = await State(h.User);
        using var response = await h.Client.PutAsync("/api/v1/backlog/order", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-order")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("gap")]
    [InlineData("out-of-range")]
    [InlineData("own-non-backlog")]
    public async Task Invalid_reorder_is_400_and_rolls_back(string scenario)
    {
        await using var h = await Create();
        var before = await State(h.User);
        var items = new[] { new BacklogOrderItem(h.A, 1), new(h.B, 2), new(h.C, 3) };
        items[2] = scenario switch
        {
            "duplicate-id" => new(h.A, 3), "duplicate-order" => new(h.C, 2),
            "zero" => new(h.C, 0), "negative" => new(h.C, -1),
            "gap" => new(h.C, 4), "out-of-range" => new(h.C, int.MaxValue),
            _ => new(h.Completed, 3)
        };
        using var response = await Put(h, items);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(errors.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(before.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task Missing_and_foreign_ids_have_the_same_404_and_do_not_affect_either_owner()
    {
        await using var h = await Create();
        var own = await State(h.User);
        var other = await State(h.Other);
        string? first = null;
        foreach (var id in new[] { Guid.NewGuid(), h.Foreign })
        {
            using var response = await Put(h, [new(h.A, 1), new(h.B, 2), new(id, 3)]);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var title = body.RootElement.GetProperty("title").GetString();
            if (first is null) first = title; else Assert.Equal(first, title);
        }
        Assert.Equal(own.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
        Assert.Equal(other.OrderBy(pair => pair.Key), (await State(h.Other)).OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incomplete_or_empty_submission_to_nonempty_backlog_is_409(bool empty)
    {
        await using var h = await Create();
        using var response = await Put(h, empty ? [] : [new(h.A, 1), new(h.C, 2)]);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new[] { 1, 3, 7 }, (await Get(h.Client)).Items.Select(item => item.Order));
    }

    [Fact]
    public async Task Empty_backlog_get_and_empty_reorder_succeed()
    {
        await using var h = await Create();
        await using (var context = database.CreateContext())
            await context.UserGames.Where(item => item.UserId == h.User && item.Status == GameStatus.Backlog).ExecuteDeleteAsync();
        Assert.Empty((await Get(h.Client)).Items);
        using var response = await Put(h, []);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<BacklogResponse>())!.Items);
    }

    [Fact]
    public async Task Delete_leave_and_return_keep_gaps_and_append_at_the_current_maximum()
    {
        await using var h = await Create();
        using var deleted = await h.Client.DeleteAsync($"/api/v1/library/{h.B}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(new[] { 1, 7 }, (await Get(h.Client)).Items.Select(item => item.Order));
        using var leave = await h.Client.PatchAsJsonAsync($"/api/v1/library/{h.A}", new { status = "Playing" });
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        var state = (await State(h.User))[h.A];
        Assert.Null(state.BacklogOrder); Assert.Null(state.Priority);
        using var enter = await h.Client.PatchAsJsonAsync($"/api/v1/library/{h.A}", new { status = "Backlog" });
        Assert.Equal(HttpStatusCode.OK, enter.StatusCode);
        Assert.Equal(new[] { 7, 8 }, (await Get(h.Client)).Items.Select(item => item.Order));
        using var add = await h.Client.PostAsJsonAsync("/api/v1/library", new { igdbId = 106, status = "Backlog" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(9, (await Get(h.Client)).Items.Last().Order);
    }

    [Fact]
    public async Task Concurrent_reorders_return_their_own_saved_orders_and_final_order_is_complete()
    {
        await using var h = await Create();
        var requests = new BacklogOrderItem[][]
        {
            [new(h.A, 1), new(h.B, 2), new(h.C, 3)],
            [new(h.C, 1), new(h.B, 2), new(h.A, 3)]
        };
        var responses = await Task.WhenAll(requests.Select(items => Put(h, items)));
        try
        {
            for (var index = 0; index < responses.Length; index++)
            {
                Assert.Equal(HttpStatusCode.OK, responses[index].StatusCode);
                var saved = (await responses[index].Content.ReadFromJsonAsync<BacklogResponse>())!;
                Assert.Equal(requests[index].Select(item => item.UserGameId), saved.Items.Select(item => item.Id));
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var final = await Get(h.Client);
        Assert.Equal(new[] { 1, 2, 3 }, final.Items.Select(item => item.Order));
        Assert.True(final.Items.Select(item => item.Id).SequenceEqual(new[] { h.A, h.B, h.C })
            || final.Items.Select(item => item.Id).SequenceEqual(new[] { h.C, h.B, h.A }));
    }

    [Fact]
    public async Task Reorder_waits_for_user_lock_and_validates_membership_after_acquiring_it()
    {
        var observer = new LockObserver();
        await using var h = await Create(observer);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {h.User} FOR UPDATE");
        var pending = Put(h, [new(h.A, 1), new(h.B, 2), new(h.C, 3)]);
        await observer.Attempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        context.UserGames.Add(new UserGame(h.User, h.SpareGame, GameStatus.Backlog, null, 8, h.Now));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new[] { 1, 3, 7, 8 }, (await Get(h.Client)).Items.Select(item => item.Order));
    }

    [Fact]
    public async Task Failure_after_first_update_rolls_back_orders_and_timestamps()
    {
        await using var h = await Create();
        var before = await State(h.User);
        await using var reference = database.CreateContext();
        var interceptor = new FailSecondUpdate();
        await using var context = new LudaryxDbContext(new DbContextOptionsBuilder<LudaryxDbContext>()
            .UseNpgsql(reference.Database.GetConnectionString()).AddInterceptors(interceptor).Options);
        var store = new EfBacklogStore(context, new FixedTime(h.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReorderAsync(h.User,
            new ReorderBacklogCommand([new(h.C, 1), new(h.B, 2), new(h.A, 3)]), CancellationToken.None));
        Assert.Equal(2, interceptor.Updates);
        Assert.Equal(before.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
    }

    [Fact]
    public async Task Unique_constraint_is_deferrable_initially_immediate_and_rejects_duplicate_final_orders()
    {
        await using var h = await Create();
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"SELECT condeferrable, condeferred FROM pg_constraint WHERE conname = '{Constraint}'";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.True(reader.GetBoolean(0)); Assert.False(reader.GetBoolean(1));
        }
        var immediate = await Assert.ThrowsAsync<PostgresException>(() => context.UserGames.Where(item => item.Id == h.B)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.BacklogOrder, 1)));
        Assert.Equal("23505", immediate.SqlState); Assert.Equal(Constraint, immediate.ConstraintName);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlRawAsync($"SET CONSTRAINTS \"{Constraint}\" DEFERRED");
        await context.UserGames.Where(item => item.Id == h.B).ExecuteUpdateAsync(update => update.SetProperty(item => item.BacklogOrder, 1));
        var deferred = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.Equal("23505", deferred.SqlState);
        await using var verify = database.CreateContext();
        Assert.Equal(3, (await verify.UserGames.SingleAsync(item => item.Id == h.B)).BacklogOrder);
    }

    [Fact]
    public async Task Migration_down_and_up_preserve_data_and_match_the_model()
    {
        await using var h = await Create();
        var before = await State(h.User);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261012120955_AddLibrarySchema");
        await migrator.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(before.OrderBy(pair => pair.Key), (await State(h.User)).OrderBy(pair => pair.Key));
    }

    private static Task<HttpResponseMessage> Put(Harness h, BacklogOrderItem[] items) =>
        h.Client.PutAsJsonAsync("/api/v1/backlog/order", new { items });
    private static async Task<BacklogResponse> Get(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/backlog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BacklogResponse>())!;
    }
    private async Task<Dictionary<Guid, PersonalState>> State(Guid user)
    {
        await using var context = database.CreateContext();
        return await context.UserGames.Where(item => item.UserId == user).ToDictionaryAsync(item => item.Id,
            item => new PersonalState(item.Status, item.Priority, item.BacklogOrder, item.Rating, item.PersonalNote,
                item.AddedAt, item.UpdatedAt, item.StartedAt, item.CompletedAt));
    }
    private sealed record PersonalState(GameStatus Status, BacklogPriority? Priority, int? BacklogOrder, int? Rating,
        string? Note, DateTimeOffset AddedAt, DateTimeOffset UpdatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

    private async Task<Harness> Create(DbCommandInterceptor? interceptor = null)
    {
        await using var context = database.CreateContext();
        var users = Enumerable.Range(0, 2).Select(_ =>
        {
            var name = $"user{Guid.NewGuid():N}"[..20];
            return new ApplicationUser { Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
                Email = $"{name}@example.com", NormalizedEmail = $"{name}@example.com".ToUpperInvariant(), DisplayName = name };
        }).ToArray();
        var games = Enumerable.Range(1, 6).Select(index => new Game(100 + index, $"Game {index}", $"game-{index}", null, null, null)).ToArray();
        var added = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var items = new[] {
            new UserGame(users[0].Id, games[0].Id, GameStatus.Playing, null, null, added),
            new UserGame(users[0].Id, games[1].Id, GameStatus.Backlog, null, 3, added.AddDays(1)),
            new UserGame(users[0].Id, games[2].Id, GameStatus.Backlog, BacklogPriority.Medium, 7, added.AddDays(2)),
            new UserGame(users[0].Id, games[3].Id, GameStatus.Completed, null, null, added),
            new UserGame(users[1].Id, games[4].Id, GameStatus.Backlog, BacklogPriority.Low, 1, added)
        };
        items[0].Update(GameStatus.Completed, null, null, 9, "keep", added.AddDays(1));
        items[0].Update(GameStatus.Backlog, BacklogPriority.High, 1, 9, "keep", added.AddDays(2));
        context.AddRange(users); context.AddRange(games); context.AddRange(items);
        await context.SaveChangesAsync();
        var connection = context.Database.GetConnectionString();
        var now = added.AddDays(10);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => {
                services.RemoveAll<LudaryxDbContext>();
                services.AddScoped(_ => {
                    var options = new DbContextOptionsBuilder<LudaryxDbContext>().UseNpgsql(connection);
                    if (interceptor is not null) options.AddInterceptors(interceptor);
                    return new LudaryxDbContext(options.Options);
                });
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedTime(now));
                services.RemoveAll<IIgdbClient>(); services.AddSingleton<IIgdbClient>(new NoProvider());
            });
        });
        return new Harness(factory, database, users[0].Id, users[1].Id, games[5].Id, now, items.Select(item => item.Id).ToArray());
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        private readonly LibraryDatabaseFixture database;
        public Guid User { get; } public Guid Other { get; } public Guid SpareGame { get; }
        public Guid A { get; } public Guid B { get; } public Guid C { get; } public Guid Completed { get; } public Guid Foreign { get; }
        public DateTimeOffset Now { get; } public HttpClient Client { get; }
        public Harness(WebApplicationFactory<Program> factory, LibraryDatabaseFixture database, Guid user, Guid other, Guid spare, DateTimeOffset now, Guid[] ids)
        {
            this.factory = factory; this.database = database; User = user; Other = other; SpareGame = spare; Now = now;
            A = ids[0]; B = ids[1]; C = ids[2]; Completed = ids[3]; Foreign = ids[4]; Client = ClientFor(user);
        }
        public HttpClient ClientFor(Guid user)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var settings = factory.Services.GetRequiredService<JwtSettings>();
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(settings.Issuer, settings.Audience, [new Claim("sub", user.ToString())], now, now.AddMinutes(15),
                new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); factory.Dispose();
            await using var context = database.CreateContext();
            await context.UserGames.ExecuteDeleteAsync(); await context.Games.ExecuteDeleteAsync(); await context.Users.ExecuteDeleteAsync();
        }
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class NoProvider : IIgdbClient
    {
        public Task<GameDetails?> GetDetailsAsync(long id, CancellationToken token) => throw new InvalidOperationException("Backlog must use stored snapshots.");
        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken token) => throw new InvalidOperationException("Backlog must not query IGDB.");
    }
    private sealed class LockObserver : DbCommandInterceptor
    {
        public TaskCompletionSource Attempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Attempt.TrySetResult();
            return new(result);
        }
    }
    private sealed class FailSecondUpdate : DbCommandInterceptor
    {
        public int Updates { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"UserGames\"", StringComparison.Ordinal) && ++Updates == 2)
                throw new InvalidOperationException("Injected mid-reorder failure.");
            return new(result);
        }
    }
}
