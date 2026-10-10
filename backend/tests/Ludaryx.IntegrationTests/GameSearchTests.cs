using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Ludaryx.Application.Games;
using Ludaryx.Infrastructure.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class GameSearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_rejects_missing_or_invalid_tokens_before_calling_IGDB(bool invalidToken)
    {
        var provider = new StubClient();
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory, authenticated: false);
        if (invalidToken)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        }

        using var response = await client.GetAsync("/api/v1/games/search?q=elden");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("el\n den")]
    public async Task Invalid_search_returns_validation_problem_without_calling_IGDB(string? query)
    {
        var provider = new StubClient();
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        var url = query is null ? "/api/v1/games/search" : $"/api/v1/games/search?q={Uri.EscapeDataString(query)}";

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").GetProperty("q").EnumerateArray());
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Search_returns_the_contract_and_trims_the_query()
    {
        var provider = new StubClient
        {
            Items =
            [
                new GameSearchItem(119133, "Elden Ring", "https://images.igdb.com/example.jpg",
                    new DateOnly(2022, 2, 25), ["PC (Microsoft Windows)"]),
                new GameSearchItem(2, "Missing metadata", null, null, [])
            ]
        };
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/games/search?q=%20Elden%20Ring%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Elden Ring", provider.Query);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(119133, items[0].GetProperty("igdbId").GetInt64());
        Assert.Equal("Elden Ring", items[0].GetProperty("name").GetString());
        Assert.Equal("https://images.igdb.com/example.jpg", items[0].GetProperty("coverUrl").GetString());
        Assert.Equal("2022-02-25", items[0].GetProperty("releaseDate").GetString());
        Assert.Equal("PC (Microsoft Windows)", items[0].GetProperty("platforms")[0].GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("coverUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("releaseDate").ValueKind);
        Assert.Equal(0, items[1].GetProperty("platforms").GetArrayLength());
    }

    [Fact]
    public async Task No_matches_returns_an_empty_items_array()
    {
        using var factory = CreateFactory(new StubClient());
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/games/search?q=unknown");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(GameProviderFailure.Unavailable, 503)]
    [InlineData(GameProviderFailure.Timeout, 504)]
    [InlineData(GameProviderFailure.InvalidResponse, 502)]
    [InlineData(GameProviderFailure.Authentication, 502)]
    public async Task Provider_failure_returns_a_safe_problem_and_the_next_request_can_succeed(
        GameProviderFailure failure, int status)
    {
        var provider = new StubClient { Failure = failure };
        using var factory = CreateFactory(provider);
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/v1/games/search?q=elden");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain("ClientSecret", text);
        Assert.DoesNotContain("access_token", text);
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);

        provider.Failure = null;
        using var recovered = await client.GetAsync("/api/v1/games/search?q=elden");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
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

    private sealed class StubClient : IIgdbClient
    {
        public int CallCount { get; private set; }
        public string? Query { get; private set; }
        public GameProviderFailure? Failure { get; set; }
        public IReadOnlyList<GameSearchItem> Items { get; init; } = [];

        public Task<IReadOnlyList<GameSearchItem>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Query = query;
            if (Failure is { } failure) throw new GameProviderException(failure);
            return Task.FromResult(Items);
        }

        public Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
