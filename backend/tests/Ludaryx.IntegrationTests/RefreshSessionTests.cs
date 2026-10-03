using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludaryx.IntegrationTests;

public class RefreshSessionTests
{
    private const string Origin = "https://localhost:5173";
    private const string Password = "a long passphrase with spaces";

    [Fact]
    public async Task Refresh_rotates_cookie_and_reuse_revokes_only_its_session_family()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var (id, email) = await RegisterAsync(client);

        try
        {
            using var firstLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
            var firstToken = ReadCookie(firstLogin);
            AssertCookieSecurity(firstLogin);
            Assert.DoesNotContain(firstToken, await firstLogin.Content.ReadAsStringAsync());

            using var secondLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
            var independentToken = ReadCookie(secondLogin);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LudaryxDbContext>();
                var hash = SHA256.HashData(Encoding.ASCII.GetBytes(firstToken));
                var stored = await db.RefreshTokens.SingleAsync(record => record.UserId == id && record.TokenHash == hash);
                Assert.Equal(32, stored.TokenHash.Length);
                Assert.NotEqual(Encoding.UTF8.GetBytes(firstToken), stored.TokenHash);
            }

            using var refreshed = await AuthPostAsync(client, "refresh", firstToken);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            var replacement = ReadCookie(refreshed);
            Assert.NotEqual(firstToken, replacement);
            using var body = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
            Assert.Equal(900, body.RootElement.GetProperty("expiresIn").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
            Assert.False(body.RootElement.TryGetProperty("refreshToken", out _));

            using var reuse = await AuthPostAsync(client, "refresh", firstToken);
            Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
            using var revokedReplacement = await AuthPostAsync(client, "refresh", replacement);
            Assert.Equal(HttpStatusCode.Unauthorized, revokedReplacement.StatusCode);
            using var independent = await AuthPostAsync(client, "refresh", independentToken);
            Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        }
        finally
        {
            await DeleteUserAsync(factory, id);
        }
    }

    [Fact]
    public async Task Logout_revokes_session_and_is_idempotent_without_cookie()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var (id, email) = await RegisterAsync(client);

        try
        {
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            var token = ReadCookie(login);
            using var logout = await AuthPostAsync(client, "logout", token);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Contains("expires=Thu, 01 Jan 1970", logout.Headers.GetValues("Set-Cookie").Single(),
                StringComparison.OrdinalIgnoreCase);
            using var repeatedLogout = await AuthPostAsync(client, "logout", null);
            Assert.Equal(HttpStatusCode.NoContent, repeatedLogout.StatusCode);
            using var refresh = await AuthPostAsync(client, "refresh", token);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }
        finally
        {
            await DeleteUserAsync(factory, id);
        }
    }

    [Fact]
    public async Task Concurrent_refresh_attempts_issue_at_most_one_replacement_and_revoke_the_family()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var (id, email) = await RegisterAsync(client);

        try
        {
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
            var token = ReadCookie(login);
            var responses = await Task.WhenAll(
                AuthPostAsync(client, "refresh", token),
                AuthPostAsync(client, "refresh", token));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
                var replacement = ReadCookie(responses.Single(response => response.StatusCode == HttpStatusCode.OK));
                using var revoked = await AuthPostAsync(client, "refresh", replacement);
                Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            await DeleteUserAsync(factory, id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://untrusted.example")]
    public async Task Refresh_and_logout_reject_disallowed_origin(string? origin)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        foreach (var action in new[] { "refresh", "logout" })
        {
            using var response = await AuthPostAsync(client, action, null, origin);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Cors_allows_credentials_only_for_configured_origin()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var allowedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/refresh");
        allowedRequest.Headers.Add("Origin", Origin);
        allowedRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var allowed = await client.SendAsync(allowedRequest);
        Assert.Equal(Origin, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var deniedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/refresh");
        deniedRequest.Headers.Add("Origin", "https://untrusted.example");
        deniedRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var denied = await client.SendAsync(deniedRequest);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

    private static async Task<(Guid Id, string Email)> RegisterAsync(HttpClient client)
    {
        var username = $"user{Guid.NewGuid():N}"[..20];
        var email = $"{username}@example.com";
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { username, email, password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("id").GetGuid(), email);
    }

    private static async Task<HttpResponseMessage> AuthPostAsync(
        HttpClient client, string action, string? token, string? origin = Origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{action}");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        if (token is not null)
        {
            request.Headers.Add("Cookie", $"ludaryx.refresh={token}");
        }
        return await client.SendAsync(request);
    }

    private static string ReadCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single();
        return header.Split(';', 2)[0].Split('=', 2)[1];
    }

    private static void AssertCookieSecurity(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DeleteUserAsync(WebApplicationFactory<Program> factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            Assert.True((await users.DeleteAsync(user)).Succeeded);
        }
    }
}
