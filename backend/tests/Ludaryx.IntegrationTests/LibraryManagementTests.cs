using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Ludaryx.Application.Games;
using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class LibraryManagementTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    [Fact]
    public async Task List_defaults_are_paginated_and_contain_only_the_current_users_library()
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, "/api/v1/library");
        var page = body.RootElement;
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { h.C, h.B, h.A }, Ids(page));
        var first = page.GetProperty("items")[0];
        Assert.Equal(103, first.GetProperty("game").GetProperty("igdbId").GetInt64());
        Assert.True(first.TryGetProperty("rating", out _));
        Assert.True(first.TryGetProperty("priority", out _));
        Assert.True(first.TryGetProperty("completedAt", out _));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(99, 0)]
    [InlineData(int.MaxValue, 0)]
    public async Task Pagination_reports_counts_and_handles_pages_beyond_the_end(int page, int expectedCount)
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, $"/api/v1/library?page={page}&pageSize=2");
        Assert.Equal(3, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Equal(expectedCount, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Empty_library_returns_zero_pages()
    {
        await using var h = await Create();
        await using (var context = database.CreateContext()) await context.UserGames.Where(item => item.UserId == h.User).ExecuteDeleteAsync();
        using var body = await GetJson(h.Client, "/api/v1/library");
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Empty(Ids(body.RootElement));
    }

    [Theory]
    [InlineData("status=Backlog")]
    [InlineData("priority=High")]
    [InlineData("rating=9")]
    [InlineData("search=%20aLpHa%20")]
    [InlineData("status=Backlog&priority=High&rating=9&platform=6&search=Alpha")]
    public async Task Filters_and_case_insensitive_search_select_the_expected_owned_record(string query)
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, $"/api/v1/library?{query}");
        Assert.Equal(new[] { h.A }, Ids(body.RootElement));
    }

    [Fact]
    public async Task Platform_filter_uses_external_ID_and_filters_combine_with_AND()
    {
        await using var h = await Create();
        using var platform = await GetJson(h.Client, "/api/v1/library?platform=6");
        Assert.Equal(new[] { h.C, h.A }, Ids(platform.RootElement));
        using var mismatch = await GetJson(h.Client, "/api/v1/library?platform=48&status=Backlog");
        Assert.Empty(Ids(mismatch.RootElement));
        using var otherUsersPriority = await GetJson(h.Client, "/api/v1/library?priority=Low");
        Assert.Empty(Ids(otherUsersPriority.RootElement));
    }

    [Fact]
    public async Task Platform_options_cover_the_whole_owned_library_without_exposing_foreign_options()
    {
        await using var h = await Create();
        await using (var context = database.CreateContext())
        {
            var platform = new Platform(999, "Foreign-only platform", null);
            var foreignGameId = await context.UserGames.Where(item => item.Id == h.Foreign).Select(item => item.GameId).SingleAsync();
            context.Platforms.Add(platform);
            context.GamePlatforms.Add(new Ludaryx.Domain.Games.GamePlatform(foreignGameId, platform.Id));
            await context.SaveChangesAsync();
        }
        using var body = await GetJson(h.Client, "/api/v1/library?status=Backlog&pageSize=1");
        var options = body.RootElement.GetProperty("availablePlatforms").EnumerateArray().ToArray();
        Assert.Equal(new long[] { 6, 48 }, options.Select(platform => platform.GetProperty("igdbId").GetInt64()));
        Assert.DoesNotContain(options, platform => platform.GetProperty("name").GetString() == "Foreign-only platform");
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Search_treats_wildcard_characters_literally(string query)
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, $"/api/v1/library?search={Uri.EscapeDataString(query)}");
        Assert.Equal(new[] { h.C }, Ids(body.RootElement));
    }

    [Theory]
    [InlineData("addedAtAsc", "ABC")]
    [InlineData("addedAtDesc", "CBA")]
    [InlineData("nameAsc", "CAB")]
    [InlineData("nameDesc", "BAC")]
    [InlineData("ratingAsc", "CAB")]
    [InlineData("ratingDesc", "ACB")]
    public async Task Sort_vocabulary_orders_values_with_unrated_records_last(string sort, string expected)
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, $"/api/v1/library?sort={sort}");
        Assert.Equal(expected.Select(value => value == 'A' ? h.A : value == 'B' ? h.B : h.C), Ids(body.RootElement));
    }

    [Theory]
    [InlineData("completedAtAsc")]
    [InlineData("completedAtDesc")]
    public async Task Completion_sort_places_incomplete_games_last(string sort)
    {
        await using var h = await Create();
        using var body = await GetJson(h.Client, $"/api/v1/library?sort={sort}");
        Assert.Equal(h.B, Ids(body.RootElement)[0]);
        var items = body.RootElement.GetProperty("items");
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task Equal_sort_values_have_stable_UUID_ties_without_overlapping_pages()
    {
        await using var h = await Create();
        await using (var context = database.CreateContext())
        {
            var date = DateTimeOffset.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserGames\" SET \"AddedAt\" = {date} WHERE \"UserId\" = {h.User}");
        }
        var observed = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            using var first = await GetJson(h.Client, $"/api/v1/library?pageSize=1&page={page}");
            using var repeat = await GetJson(h.Client, $"/api/v1/library?pageSize=1&page={page}");
            Assert.Equal(Ids(first.RootElement), Ids(repeat.RootElement));
            observed.AddRange(Ids(first.RootElement));
        }
        Assert.Equal(3, observed.Distinct().Count());
        await using var verification = database.CreateContext();
        Assert.Equal(await verification.UserGames.Where(item => item.UserId == h.User).OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(), observed);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    [InlineData("platform=0")]
    [InlineData("rating=0")]
    [InlineData("rating=11")]
    [InlineData("status=1")]
    [InlineData("priority=Invalid")]
    [InlineData("sort=Invalid")]
    public async Task Invalid_list_parameters_return_400(string query)
    {
        await using var h = await Create();
        using var response = await h.Client.GetAsync($"/api/v1/library?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stored_details_do_not_depend_on_IGDB_availability()
    {
        await using var h = await Create();
        h.Provider.Unavailable = true;
        using var body = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        var item = body.RootElement;
        Assert.Equal("Alpha", item.GetProperty("game").GetProperty("name").GetString());
        Assert.Equal(12, item.GetProperty("game").GetProperty("genres")[0].GetProperty("igdbId").GetInt64());
        Assert.Equal(6, item.GetProperty("game").GetProperty("platforms")[0].GetProperty("igdbId").GetInt64());
        Assert.Equal("keep", item.GetProperty("personalNote").GetString());
        Assert.Equal(0, h.Provider.Calls);
        foreach (var field in new[] { "startedAt", "completedAt", "updatedAt", "addedAt", "backlogOrder" })
            Assert.True(item.TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Foreign_and_missing_records_return_the_same_404(string method)
    {
        await using var h = await Create();
        foreach (var id in new[] { h.Foreign, Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/library/{id}");
            if (method == "PATCH") request.Content = JsonContent.Create(new { rating = 10 });
            using var response = await h.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Library item was not found.", body.RootElement.GetProperty("title").GetString());
        }
        await using var context = database.CreateContext();
        Assert.Equal(10, (await context.UserGames.SingleAsync(item => item.Id == h.Foreign)).Rating);
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("GET", true)]
    [InlineData("PATCH", false)]
    [InlineData("DELETE", false)]
    public async Task Library_routes_require_authentication(string method, bool collection)
    {
        await using var h = await Create();
        h.Client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(new HttpMethod(method), collection ? "/api/v1/library" : $"/api/v1/library/{h.A}");
        if (method == "PATCH") request.Content = JsonContent.Create(new { rating = 10 });
        using var response = await h.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_preserves_omitted_fields_and_explicit_null_clears_nullable_fields()
    {
        await using var h = await Create();
        using var changed = await Patch(h, new { rating = 1 });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var body = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("rating").GetInt32());
        Assert.Equal("keep", body.RootElement.GetProperty("personalNote").GetString());
        Assert.Equal("High", body.RootElement.GetProperty("priority").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("backlogOrder").GetInt32());
        using var cleared = await Patch(h, new { rating = (int?)null, priority = (string?)null, personalNote = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var empty = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        foreach (var field in new[] { "rating", "priority", "personalNote" })
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty(field).ValueKind);
        Assert.Equal("Backlog", empty.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"status\":null}")]
    [InlineData("{\"status\":1}")]
    [InlineData("{\"status\":\"Invalid\"}")]
    [InlineData("{\"rating\":0}")]
    [InlineData("{\"rating\":11}")]
    [InlineData("{\"rating\":1.5}")]
    [InlineData("{\"priority\":\"Invalid\"}")]
    [InlineData("{\"status\":\"Playing\",\"priority\":\"High\"}")]
    [InlineData("{\"addedAt\":\"2020-01-01\"}")]
    [InlineData("{\"userId\":\"ignored\"}")]
    [InlineData("{\"rating\":1,\"rating\":10}")]
    public async Task Invalid_patch_does_not_partially_change_a_record(string json)
    {
        await using var h = await Create();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await h.Client.PatchAsync($"/api/v1/library/{h.A}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = database.CreateContext();
        var item = await context.UserGames.SingleAsync(item => item.Id == h.A);
        Assert.Equal(9, item.Rating);
        Assert.Equal(GameStatus.Backlog, item.Status);
        Assert.Equal(BacklogPriority.High, item.Priority);
        Assert.Equal("keep", item.PersonalNote);
    }

    [Fact]
    public async Task Note_length_and_priority_rules_are_applied_to_the_resulting_state()
    {
        await using var h = await Create();
        using var allowed = await Patch(h, new { personalNote = new string('x', 1000) });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var rejected = await Patch(h, new { personalNote = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var nonBacklogPriority = await h.Client.PatchAsJsonAsync($"/api/v1/library/{h.B}", new { priority = "High" });
        Assert.Equal(HttpStatusCode.BadRequest, nonBacklogPriority.StatusCode);
    }

    [Fact]
    public async Task Status_changes_clear_backlog_fields_preserve_history_and_append_on_return()
    {
        await using var h = await Create();
        using var playing = await Patch(h, new { status = "Playing" });
        Assert.Equal(HttpStatusCode.OK, playing.StatusCode);
        using var playingBody = JsonDocument.Parse(await playing.Content.ReadAsStringAsync());
        var started = playingBody.RootElement.GetProperty("startedAt").GetDateTimeOffset();
        Assert.Equal(JsonValueKind.Null, playingBody.RootElement.GetProperty("priority").ValueKind);
        Assert.Equal(JsonValueKind.Null, playingBody.RootElement.GetProperty("backlogOrder").ValueKind);
        using var completed = await Patch(h, new { status = "Completed" });
        using var completedBody = JsonDocument.Parse(await completed.Content.ReadAsStringAsync());
        var finished = completedBody.RootElement.GetProperty("completedAt").GetDateTimeOffset();
        using var next = await h.Client.PostAsJsonAsync("/api/v1/library", new { igdbId = 105, status = "Backlog" });
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        using var returned = await Patch(h, new { status = "Backlog" });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        using var returnedBody = JsonDocument.Parse(await returned.Content.ReadAsStringAsync());
        Assert.Equal(2, returnedBody.RootElement.GetProperty("backlogOrder").GetInt32());
        Assert.Equal(started, returnedBody.RootElement.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(finished, returnedBody.RootElement.GetProperty("completedAt").GetDateTimeOffset());
        using var replay = await Patch(h, new { status = "Playing" });
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(started, replayBody.RootElement.GetProperty("startedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Delete_preserves_metadata_and_removes_current_user_membership()
    {
        await using var h = await Create();
        using var before = await GetJson(h.Client, "/api/v1/games/igdb/101");
        Assert.True(before.RootElement.GetProperty("library").GetProperty("isAdded").GetBoolean());
        Assert.Equal(h.A, before.RootElement.GetProperty("library").GetProperty("userGameId").GetGuid());
        using var deleted = await h.Client.DeleteAsync($"/api/v1/library/{h.A}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var repeated = await h.Client.DeleteAsync($"/api/v1/library/{h.A}");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        using var after = await GetJson(h.Client, "/api/v1/games/igdb/101");
        var membership = after.RootElement.GetProperty("library");
        Assert.False(membership.GetProperty("isAdded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, membership.GetProperty("userGameId").ValueKind);
        await using var context = database.CreateContext();
        Assert.Equal(4, await context.Games.CountAsync());
        Assert.Equal(1, await context.Genres.CountAsync());
        Assert.Equal(2, await context.Platforms.CountAsync());
    }

    [Fact]
    public async Task Discovery_membership_is_scoped_to_the_user_and_tracks_status_updates()
    {
        await using var h = await Create();
        using var other = h.ClientFor(h.OtherUser);
        using var body = await GetJson(other, "/api/v1/games/igdb/101");
        Assert.False(body.RootElement.GetProperty("library").GetProperty("isAdded").GetBoolean());
        using var updated = await Patch(h, new { status = "Completed" });
        using var own = await GetJson(h.Client, "/api/v1/games/igdb/101");
        Assert.Equal("Completed", own.RootElement.GetProperty("library").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Concurrent_partial_updates_preserve_both_changes()
    {
        await using var h = await Create();
        var responses = await Task.WhenAll(Patch(h, new { rating = 10 }), Patch(h, new { personalNote = "parallel" }));
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
        using var body = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        Assert.Equal(10, body.RootElement.GetProperty("rating").GetInt32());
        Assert.Equal("parallel", body.RootElement.GetProperty("personalNote").GetString());
    }

    private static Guid[] Ids(JsonElement body) => body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
    private static Task<HttpResponseMessage> Patch(Harness h, object command) => h.Client.PatchAsJsonAsync($"/api/v1/library/{h.A}", command);
    private static async Task<JsonDocument> GetJson(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(null)]
    public async Task Rating_only_patch_round_trips_bounds_and_null_without_changing_other_fields(int? rating)
    {
        await using var h = await Create();
        using var before = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        using var response = await Patch(h, new { rating });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var fetched = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        Assert.Equal(saved.RootElement.GetRawText(), fetched.RootElement.GetRawText());
        if (rating is null) Assert.Equal(JsonValueKind.Null, fetched.RootElement.GetProperty("rating").ValueKind);
        else Assert.Equal(rating.Value, fetched.RootElement.GetProperty("rating").GetInt32());
        AssertUnchangedExcept(before.RootElement, fetched.RootElement, "rating", "updatedAt");
    }

    [Theory]
    [InlineData("startedAt")]
    [InlineData("completedAt")]
    public async Task Client_cannot_edit_lifecycle_dates_even_with_an_otherwise_valid_patch(string field)
    {
        await using var h = await Create();
        using var before = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        using var response = await Patch(h, new Dictionary<string, object?>
        {
            ["rating"] = 10, [field] = "2026-01-02T00:00:00Z"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var after = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        AssertUnchangedExcept(before.RootElement, after.RootElement);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Note_only_patch_preserves_text_and_other_personal_fields(bool maximumUnicodeLength)
    {
        await using var h = await Create();
        var note = maximumUnicodeLength ? new string('x', 998) + "🎮" : "  First line\r\nSecond line: café 🎮\n  ";
        using var before = await GetJson(h.Client, $"/api/v1/library/{h.C}");
        using var response = await h.Client.PatchAsJsonAsync($"/api/v1/library/{h.C}", new { personalNote = note });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var saved = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var fetched = await GetJson(h.Client, $"/api/v1/library/{h.C}");
        Assert.Equal(note, fetched.RootElement.GetProperty("personalNote").GetString());
        Assert.Equal(saved.RootElement.GetRawText(), fetched.RootElement.GetRawText());
        AssertUnchangedExcept(before.RootElement, fetched.RootElement, "personalNote", "updatedAt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_combined_edit_does_not_save_status_dates_rating_or_note(bool oversizedNote)
    {
        await using var h = await Create();
        using var before = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        using var response = await Patch(h, new
        {
            status = "Completed", rating = oversizedNote ? 10 : 0,
            personalNote = oversizedNote ? new string('x', 999) + "🎮" : "Changed"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var after = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        AssertUnchangedExcept(before.RootElement, after.RootElement);
    }

    [Fact]
    public async Task Http_transitions_record_first_dates_and_preserve_them_across_replays()
    {
        var clock = new LifecycleClock();
        await using var h = await Create(clock);
        var started = clock.Now;
        using var playing = await Patch(h, new { status = "Playing" });
        Assert.Equal(HttpStatusCode.OK, playing.StatusCode);
        clock.Now = clock.Now.AddDays(1);
        var completed = clock.Now;
        using var finished = await Patch(h, new { status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        foreach (var status in new[] { "Dropped", "Playing", "OnHold", "Completed" })
        {
            clock.Now = clock.Now.AddDays(1);
            using var response = await Patch(h, new { status });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var fetched = await GetJson(h.Client, $"/api/v1/library/{h.A}");
            Assert.Equal(started, fetched.RootElement.GetProperty("startedAt").GetDateTimeOffset());
            Assert.Equal(completed, fetched.RootElement.GetProperty("completedAt").GetDateTimeOffset());
            Assert.Equal(clock.Now, fetched.RootElement.GetProperty("updatedAt").GetDateTimeOffset());
            Assert.Equal(9, fetched.RootElement.GetProperty("rating").GetInt32());
            Assert.Equal("keep", fetched.RootElement.GetProperty("personalNote").GetString());
        }
    }

    [Fact]
    public async Task Direct_completion_does_not_invent_a_start_date_and_later_playing_preserves_completion()
    {
        var clock = new LifecycleClock();
        await using var h = await Create(clock);
        var completed = clock.Now;
        using var response = await Patch(h, new { status = "Completed" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var first = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("startedAt").ValueKind);
        Assert.Equal(completed, first.RootElement.GetProperty("completedAt").GetDateTimeOffset());
        clock.Now = clock.Now.AddDays(1);
        using var playing = await Patch(h, new { status = "Playing" });
        Assert.Equal(HttpStatusCode.OK, playing.StatusCode);
        using var after = await GetJson(h.Client, $"/api/v1/library/{h.A}");
        Assert.Equal(clock.Now, after.RootElement.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(completed, after.RootElement.GetProperty("completedAt").GetDateTimeOffset());
    }

    private static void AssertUnchangedExcept(JsonElement before, JsonElement after, params string[] changedFields)
    {
        foreach (var property in before.EnumerateObject())
            if (!changedFields.Contains(property.Name))
                Assert.Equal(property.Value.GetRawText(), after.GetProperty(property.Name).GetRawText());
    }

    private sealed class LifecycleClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private async Task<Harness> Create(TimeProvider? timeProvider = null)
    {
        await using var context = database.CreateContext();
        var users = new[] { NewUser(), NewUser() };
        var genre = new Genre(12, "RPG");
        var pc = new Platform(6, "PC", null);
        var console = new Platform(48, "PlayStation", "PS");
        var names = new[] { "Alpha", "Beta", "100%_Fun\\Game", "Other User Game" };
        var games = names.Select((name, index) => new Game(101 + index, name, $"game-{index}", null, null, null)).ToArray();
        for (var index = 0; index < games.Length; index++)
        {
            games[index].Genres.Add(new Ludaryx.Domain.Games.GameGenre(games[index].Id, genre.Id));
            games[index].Platforms.Add(new Ludaryx.Domain.Games.GamePlatform(games[index].Id, index == 1 ? console.Id : pc.Id));
        }
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var items = new[]
        {
            new UserGame(users[0].Id, games[0].Id, GameStatus.Backlog, BacklogPriority.High, 1, now),
            new UserGame(users[0].Id, games[1].Id, GameStatus.Completed, null, null, now.AddDays(1)),
            new UserGame(users[0].Id, games[2].Id, GameStatus.Playing, null, null, now.AddDays(2)),
            new UserGame(users[1].Id, games[3].Id, GameStatus.Backlog, BacklogPriority.Low, 1, now.AddDays(3))
        };
        items[0].Update(GameStatus.Backlog, BacklogPriority.High, 1, 9, "keep", now);
        items[2].Update(GameStatus.Playing, null, null, 5, null, now.AddDays(2));
        items[3].Update(GameStatus.Backlog, BacklogPriority.Low, 1, 10, null, now.AddDays(3));
        context.AddRange(users);
        context.AddRange(games);
        context.AddRange(genre, pc, console);
        context.AddRange(items);
        await context.SaveChangesAsync();
        var connection = context.Database.GetConnectionString();
        var provider = new StubProvider();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:LudaryxDatabase"] = connection }));
            builder.ConfigureServices(services =>
            {
                if (timeProvider is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(timeProvider);
                }
                services.RemoveAll<IIgdbClient>();
                services.AddSingleton<IIgdbClient>(provider);
                services.RemoveAll<LudaryxDbContext>();
                services.AddScoped(_ => new LudaryxDbContext(new DbContextOptionsBuilder<LudaryxDbContext>().UseNpgsql(connection).Options));
            });
        });
        return new Harness(factory, database, provider, users[0].Id, users[1].Id, items.Select(item => item.Id).ToArray());
    }

    private static ApplicationUser NewUser()
    {
        var id = Guid.NewGuid();
        var name = $"user{id:N}"[..20];
        return new ApplicationUser { Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@example.com", NormalizedEmail = $"{name}@EXAMPLE.COM", DisplayName = name };
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        private readonly LibraryDatabaseFixture database;
        public StubProvider Provider { get; }
        public HttpClient Client { get; }
        public Guid User { get; }
        public Guid OtherUser { get; }
        public Guid A { get; }
        public Guid B { get; }
        public Guid C { get; }
        public Guid Foreign { get; }

        public Harness(WebApplicationFactory<Program> factory, LibraryDatabaseFixture database, StubProvider provider, Guid user, Guid other, Guid[] ids)
        {
            this.factory = factory;
            this.database = database;
            Provider = provider; User = user; OtherUser = other;
            A = ids[0]; B = ids[1]; C = ids[2]; Foreign = ids[3];
            Client = ClientFor(user);
        }

        public HttpClient ClientFor(Guid id)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var settings = factory.Services.GetRequiredService<JwtSettings>();
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(settings.Issuer, settings.Audience, [new Claim("sub", id.ToString())], now, now.AddMinutes(15), new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); factory.Dispose();
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
        public bool Unavailable { get; set; }
        public int Calls { get; private set; }
        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken)
        {
            Calls++;
            if (Unavailable) throw new GameProviderException(GameProviderFailure.Unavailable);
            return Task.FromResult<GameDetails?>(new GameDetails(igdbId, "Provider game", "provider-game", null, null, null, [], []));
        }
        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
