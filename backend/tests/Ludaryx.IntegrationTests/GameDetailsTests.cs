using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Ludaryx.Application.Games;
using Ludaryx.Application.Games.GetGameDetails;
using Ludaryx.Infrastructure.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class GameDetailsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_requires_a_valid_token_before_calling_IGDB(bool invalidToken)
    {
        var provider = new StubClient();
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory, authenticated: false);
        if (invalidToken)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        }
        using var response = await client.GetAsync("/api/v1/games/igdb/119133");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("12345678-1234-1234-1234-123456789abc")]
    public async Task Invalid_ID_returns_400_without_calling_IGDB(string id)
    {
        var provider = new StubClient();
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/api/v1/games/igdb/{id}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").GetProperty("igdbId").EnumerateArray());
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Missing_game_returns_404_problem()
    {
        var provider = new StubClient();
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/games/igdb/119133");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(119133, provider.Id);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Game was not found.", body.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_returns_the_contract_with_not_added_membership(bool missingMetadata)
    {
        var provider = new StubClient
        {
            Game = new GameDetails(119133, "Elden Ring", "elden-ring",
                missingMetadata ? null : "An action RPG.",
                missingMetadata ? null : "https://images.igdb.com/example.jpg",
                missingMetadata ? null : new DateOnly(2022, 2, 25),
                missingMetadata ? [] : [new GameGenre(12, "Role-playing (RPG)")],
                missingMetadata ? [] : [new GamePlatform(6, "PC (Microsoft Windows)", null)])
        };
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/games/igdb/119133");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var game = body.RootElement;
        Assert.Equal(119133, game.GetProperty("igdbId").GetInt64());
        Assert.Equal("Elden Ring", game.GetProperty("name").GetString());
        Assert.Equal("elden-ring", game.GetProperty("slug").GetString());
        var library = game.GetProperty("library");
        Assert.False(library.GetProperty("isAdded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, library.GetProperty("userGameId").ValueKind);
        Assert.Equal(JsonValueKind.Null, library.GetProperty("status").ValueKind);
        if (missingMetadata)
        {
            foreach (var field in new[] { "summary", "coverUrl", "releaseDate" })
                Assert.Equal(JsonValueKind.Null, game.GetProperty(field).ValueKind);
            Assert.Empty(game.GetProperty("genres").EnumerateArray());
            Assert.Empty(game.GetProperty("platforms").EnumerateArray());
        }
        else
        {
            Assert.Equal("An action RPG.", game.GetProperty("summary").GetString());
            Assert.Equal("https://images.igdb.com/example.jpg", game.GetProperty("coverUrl").GetString());
            Assert.Equal("2022-02-25", game.GetProperty("releaseDate").GetString());
            Assert.Equal(12, game.GetProperty("genres")[0].GetProperty("igdbId").GetInt64());
            Assert.Equal("Role-playing (RPG)", game.GetProperty("genres")[0].GetProperty("name").GetString());
            Assert.Equal(6, game.GetProperty("platforms")[0].GetProperty("igdbId").GetInt64());
            Assert.Equal("PC (Microsoft Windows)", game.GetProperty("platforms")[0].GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, game.GetProperty("platforms")[0].GetProperty("abbreviation").ValueKind);
        }
    }

    [Theory]
    [InlineData(GameProviderFailure.Unavailable, 503)]
    [InlineData(GameProviderFailure.Timeout, 504)]
    [InlineData(GameProviderFailure.InvalidResponse, 502)]
    [InlineData(GameProviderFailure.Authentication, 502)]
    public async Task Provider_failures_return_safe_problem_details(GameProviderFailure failure, int status)
    {
        using var factory = CreateFactory(new StubClient { Failure = failure });
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/games/igdb/119133");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("ClientSecret", text);
        Assert.DoesNotContain("access_token", text);
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(StubClient provider) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LudaryxDatabase"] = "Host=localhost;Database=unused;Username=unused",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIgdbClient>();
                services.AddSingleton<IIgdbClient>(provider);
                services.RemoveAll<IGameLibraryMembership>();
                services.AddSingleton<IGameLibraryMembership>(new StubMembership());
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, bool authenticated = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (authenticated)
        {
            var settings = factory.Services.GetRequiredService<JwtSettings>();
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
                [new Claim("sub", Guid.NewGuid().ToString())], now, now.AddMinutes(15),
                new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        return client;
    }

    private sealed class StubMembership : IGameLibraryMembership
    {
        public Task<GameLibraryMembership> GetAsync(Guid userId, long igdbId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameLibraryMembership(false, null, null));
    }

    private sealed class StubClient : IIgdbClient
    {
        public int CallCount { get; private set; }
        public long? Id { get; private set; }
        public GameDetails? Game { get; init; }
        public GameProviderFailure? Failure { get; init; }

        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Id = igdbId;
            if (Failure is { } failure) throw new GameProviderException(failure);
            return Task.FromResult(Game);
        }

        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
