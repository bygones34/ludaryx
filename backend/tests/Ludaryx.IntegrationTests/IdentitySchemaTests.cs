using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ludaryx.IntegrationTests;

public class IdentitySchemaTests
{
    [Fact]
    public async Task Authentication_migrations_apply_to_a_clean_database()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var scope = factory.Services.CreateAsyncScope();
        var configuredContext = scope.ServiceProvider.GetRequiredService<LudaryxDbContext>();
        var connection = new NpgsqlConnectionStringBuilder(configuredContext.Database.GetConnectionString());
        var databaseName = $"ludaryx_m1_test_{Guid.NewGuid():N}";
        await using var administration = new NpgsqlConnection(connection.ConnectionString);
        await administration.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            connection.Database = databaseName;
            connection.Pooling = false;
            var options = new DbContextOptionsBuilder<LudaryxDbContext>()
                .UseNpgsql(connection.ConnectionString).Options;
            await using var context = new LudaryxDbContext(options);

            await context.Database.MigrateAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(context.Database.GetMigrations().ToArray(), applied);
            Assert.Contains(applied, migration => migration.EndsWith("_InitialIdentity", StringComparison.Ordinal));
            Assert.Contains(applied, migration => migration.EndsWith("_AddRefreshSessions", StringComparison.Ordinal));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(await context.Users.AnyAsync());
            Assert.False(await context.RefreshTokens.AnyAsync());
        }
        finally
        {
            // The generated database belongs only to this test, never to local development.
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Identity_migration_is_applied_and_user_table_is_queryable()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LudaryxDbContext>();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations,
            migration => migration.EndsWith("_InitialIdentity", StringComparison.Ordinal));

        await context.Users.AnyAsync();
    }
}
