using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ludaryx.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ludaryx.IntegrationTests;

public class RegistrationTests
{
    private const string ValidPassword =
        "a long passphrase with spaces that remains valid beyond sixty-four characters";

    [Fact]
    public async Task Register_creates_an_Identity_user_and_returns_public_fields()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var username = UniqueUsername();
        var email = $"{username}@example.com";
        Guid? createdId = null;

        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
                new { username, email, password = ValidPassword });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var userId = body.RootElement.GetProperty("id").GetGuid();
            createdId = userId;
            Assert.Equal(3, body.RootElement.EnumerateObject().Count());
            Assert.Equal(username, body.RootElement.GetProperty("username").GetString());
            Assert.Equal(email, body.RootElement.GetProperty("email").GetString());

            await using var scope = factory.Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByIdAsync(userId.ToString());
            Assert.NotNull(user);
            Assert.Equal(username, user.DisplayName);
            Assert.Equal(TimeSpan.Zero, user.CreatedAt.Offset);
            Assert.NotNull(user.PasswordHash);
            Assert.NotEqual(ValidPassword, user.PasswordHash);
        }
        finally
        {
            if (createdId is Guid userId)
            {
                await DeleteUserAsync(factory, userId);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Register_rejects_duplicate_username_or_email(bool duplicateUsername)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var username = UniqueUsername();
        var secondUsername = UniqueUsername();
        var email = $"{username}@example.com";
        Guid? firstId = null;
        Guid? secondId = null;

        try
        {
            using var first = await client.PostAsJsonAsync("/api/v1/auth/register",
                new { username, email, password = ValidPassword });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            firstId = await ReadUserIdAsync(first);

            using var second = await client.PostAsJsonAsync("/api/v1/auth/register",
                new
                {
                    username = duplicateUsername ? username.ToUpperInvariant() : secondUsername,
                    email = duplicateUsername ? $"{secondUsername}@example.com" : email.ToUpperInvariant(),
                    password = ValidPassword
                });

            if (second.StatusCode == HttpStatusCode.Created)
            {
                secondId = await ReadUserIdAsync(second);
            }
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
        finally
        {
            if (secondId is Guid createdSecondId)
            {
                await DeleteUserAsync(factory, createdSecondId);
            }
            if (firstId is Guid createdFirstId)
            {
                await DeleteUserAsync(factory, createdFirstId);
            }
        }
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("password")]
    public async Task Register_rejects_invalid_account_fields(string invalidField)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var username = invalidField == "username" ? "ab" : UniqueUsername();
        var email = invalidField == "email" ? "not-an-email" : $"{username}@example.com";
        var password = invalidField == "password" ? "short password" : ValidPassword;
        Guid? createdId = null;

        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/register",
                new { username, email, password });

            if (response.StatusCode == HttpStatusCode.Created)
            {
                createdId = await ReadUserIdAsync(response);
            }
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            if (createdId is Guid userId)
            {
                await DeleteUserAsync(factory, userId);
            }
        }
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

    private static async Task<Guid> ReadUserIdAsync(HttpResponseMessage response)
    {
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
