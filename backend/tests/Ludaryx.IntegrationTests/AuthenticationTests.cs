using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Ludaryx.IntegrationTests;

public class AuthenticationTests
{
    private const string Password = "a long passphrase with spaces that exceeds sixty-four characters";

    [Fact]
    public async Task Login_issues_an_access_token_that_can_read_the_current_user()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var username = UniqueUsername();
        var email = $"{username}@example.com";
        var userId = await RegisterAsync(client, username, email);

        try
        {
            using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email = email.ToUpperInvariant(), password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            Assert.Equal(900, loginBody.RootElement.GetProperty("expiresIn").GetInt32());
            Assert.Equal(userId, loginBody.RootElement.GetProperty("user").GetProperty("id").GetGuid());
            Assert.Equal(username,
                loginBody.RootElement.GetProperty("user").GetProperty("username").GetString());
            var token = loginBody.RootElement.GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var currentUser = await client.GetAsync("/api/v1/users/me");
            Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);

            using var currentBody = JsonDocument.Parse(await currentUser.Content.ReadAsStringAsync());
            Assert.Equal(userId, currentBody.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(username, currentBody.RootElement.GetProperty("username").GetString());
            Assert.Equal(email, currentBody.RootElement.GetProperty("email").GetString());
            Assert.Equal(username, currentBody.RootElement.GetProperty("displayName").GetString());
            Assert.True(currentBody.RootElement.GetProperty("createdAt").GetDateTimeOffset() <=
                        DateTimeOffset.UtcNow);
        }
        finally
        {
            await DeleteUserAsync(factory, userId);
        }
    }

    [Fact]
    public async Task Login_rejects_a_wrong_password()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var username = UniqueUsername();
        var email = $"{username}@example.com";
        var userId = await RegisterAsync(client, username, email);

        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { email, password = "wrong password" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DeleteUserAsync(factory, userId);
        }
    }

    [Fact]
    public async Task Current_user_requires_a_bearer_token()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("signature")]
    public async Task Current_user_rejects_an_invalid_access_token(string invalidPart)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var settings = factory.Services.GetRequiredService<JwtSettings>();
        var now = DateTime.UtcNow;
        var signingKey = invalidPart == "signature"
            ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))
            : settings.SigningKey;
        var token = new JwtSecurityToken(
            issuer: invalidPart == "issuer" ? "Other.Issuer" : settings.Issuer,
            audience: invalidPart == "audience" ? "Other.Audience" : settings.Audience,
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: invalidPart == "expired" ? now.AddMinutes(-30) : now,
            expires: invalidPart == "expired" ? now.AddMinutes(-15) : now.AddMinutes(15),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        using var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    private static string UniqueUsername() => $"user{Guid.NewGuid():N}"[..20];

    private static async Task<Guid> RegisterAsync(HttpClient client, string username, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { username, email, password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task DeleteUserAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            var result = await manager.DeleteAsync(user);
            Assert.True(result.Succeeded);
        }
    }
}
