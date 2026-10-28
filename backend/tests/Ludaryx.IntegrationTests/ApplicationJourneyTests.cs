using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ludaryx.Application.Dashboard.GetDashboard;
using Ludaryx.Application.Games;
using Ludaryx.Application.Library;
using Ludaryx.Application.Library.AddGame;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ludaryx.IntegrationTests;

public class ApplicationJourneyTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    [Fact]
    public async Task Authentication_discovery_library_backlog_and_dashboard_persist_across_new_sessions()
    {
        using var factory = new JourneyApiFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        const string password = "synthetic milestone seven test passphrase";
        var username = $"journey{Guid.NewGuid():N}"[..24];
        var email = $"{username}@example.com";
        using var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { username, email, password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        async Task<string> Login()
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
            return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        }
        async Task<HttpResponseMessage> SessionRequest(string action, string cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{action}");
            request.Headers.Add("Origin", "https://localhost:5173");
            request.Headers.Add("Cookie", cookie);
            return await client.SendAsync(request);
        }
        var cookie = await Login();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/users/me");
        Assert.Equal(username, me.GetProperty("username").GetString());
        var empty = (await client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;
        Assert.Equal(0, empty.Statistics.TotalGames);
        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/games/search?q=journey");
        Assert.Equal(2, search.GetProperty("items").GetArrayLength());
        var details = await client.GetFromJsonAsync<JsonElement>("/api/v1/games/igdb/701101");
        Assert.False(details.GetProperty("library").GetProperty("isAdded").GetBoolean());

        async Task<AddedLibraryItem> Add(long igdbId)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/library", new { igdbId, status = "Backlog" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AddedLibraryItem>())!;
        }
        var alpha = await Add(701101);
        var beta = await Add(701102);
        using var reorder = await client.PutAsJsonAsync("/api/v1/backlog/order", new
        {
            items = new[] { new { userGameId = beta.Id, order = 1 }, new { userGameId = alpha.Id, order = 2 } }
        });
        reorder.EnsureSuccessStatusCode();
        var ordered = (await client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;
        Assert.Equal(new[] { beta.Id, alpha.Id }, ordered.UpNext.Select(item => item.UserGameId));
        using var edit = await client.PatchAsJsonAsync($"/api/v1/library/{alpha.Id}", new
        {
            status = "Playing", rating = 8, personalNote = "Synthetic journey note\nSecond line"
        });
        edit.EnsureSuccessStatusCode();
        var saved = (await edit.Content.ReadFromJsonAsync<LibraryItemDetails>())!;
        Assert.NotNull(saved.StartedAt);
        Assert.Null(saved.BacklogOrder);
        Assert.Equal(8, saved.Rating);
        var filtered = (await client.GetFromJsonAsync<LibraryPage>("/api/v1/library?status=Playing&rating=8"))!;
        Assert.Equal(alpha.Id, Assert.Single(filtered.Items).Id);

        // A fresh bearer obtained through the refresh cookie models page reload.
        client.DefaultRequestHeaders.Authorization = null;
        using var refresh = await SessionRequest("refresh", cookie);
        refresh.EnsureSuccessStatusCode();
        var refreshed = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.GetProperty("accessToken").GetString());
        cookie = refresh.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        Assert.Equivalent(saved, await client.GetFromJsonAsync<LibraryItemDetails>($"/api/v1/library/{alpha.Id}"), strict: true);
        using var logout = await SessionRequest("logout", cookie);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var revoked = await SessionRequest("refresh", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var unauthorized = await client.GetAsync("/api/v1/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        cookie = await Login();
        Assert.Equivalent(saved, await client.GetFromJsonAsync<LibraryItemDetails>($"/api/v1/library/{alpha.Id}"), strict: true);
        var dashboard = (await client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;
        Assert.Equal(new DashboardStatistics(2, 1, 0, 1, 0, 0, 8m), dashboard.Statistics);
        Assert.Equal(alpha.Id, Assert.Single(dashboard.CurrentlyPlaying).UserGameId);
        Assert.Equal(beta.Id, Assert.Single(dashboard.UpNext).UserGameId);
        using var complete = await client.PatchAsJsonAsync($"/api/v1/library/{alpha.Id}", new { status = "Completed" });
        complete.EnsureSuccessStatusCode();
        var completed = (await complete.Content.ReadFromJsonAsync<LibraryItemDetails>())!;
        Assert.Equal(saved.StartedAt, completed.StartedAt);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(saved.PersonalNote, completed.PersonalNote);
        using var remove = await client.DeleteAsync($"/api/v1/library/{beta.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var final = (await client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard"))!;
        Assert.Equal(new DashboardStatistics(1, 0, 1, 0, 0, 0, 8m), final.Statistics);
        Assert.Empty(final.CurrentlyPlaying);
        Assert.Empty(final.UpNext);
        using var finalLogout = await SessionRequest("logout", cookie);
        Assert.Equal(HttpStatusCode.NoContent, finalLogout.StatusCode);
    }
}

// Real authentication, application services, EF and PostgreSQL; only IGDB is controlled.
public sealed class JourneyApiFactory(LibraryDatabaseFixture database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        using var context = database.CreateContext();
        var connection = context.Database.GetConnectionString();
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LudaryxDbContext>();
            services.AddScoped(_ => new LudaryxDbContext(new DbContextOptionsBuilder<LudaryxDbContext>().UseNpgsql(connection).Options));
            services.RemoveAll<IIgdbClient>();
            services.AddSingleton<IIgdbClient>(new JourneyGameProvider());
        });
    }

    private sealed class JourneyGameProvider : IIgdbClient
    {
        private int failures;
        public async Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (query == "unavailable" && Interlocked.Increment(ref failures) == 1)
                throw new GameProviderException(GameProviderFailure.Unavailable);
            if (query == "slow") await Task.Delay(1500, cancellationToken);
            if (query == "missing") return [];
            return [new(701101, "Journey Alpha", null, null, ["PC"]), new(701102, "Journey Beta", null, null, ["PC"])];
        }

        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken) =>
            Task.FromResult<GameDetails?>(igdbId is 701101 or 701102
                ? new(igdbId, igdbId == 701101 ? "Journey Alpha" : "Journey Beta", $"journey-{igdbId}",
                    "Synthetic game metadata for integrated regression.", null, null, [new(12, "RPG")], [new(6, "PC", null)]) : null);
    }
}
