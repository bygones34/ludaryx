using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ludaryx.IntegrationTests;

public class LibrarySchemaTests(LibraryDatabaseFixture database) : IClassFixture<LibraryDatabaseFixture>
{
    [Fact]
    public async Task Library_migration_applies_to_clean_PostgreSql_and_matches_the_model()
    {
        await using var context = database.CreateContext();
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(),
            name => name.EndsWith("_AddLibrarySchema", StringComparison.Ordinal));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.False(await context.UserGames.AnyAsync());
    }

    [Fact]
    public Task Metadata_and_two_users_share_one_game_and_round_trip_without_losing_nulls() => WithSeed(async (context, seed) =>
    {
        var secondUser = NewUser();
        context.Users.Add(secondUser);
        context.UserGames.Add(new UserGame(secondUser.Id, seed.Game.Id, GameStatus.Completed, null, null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var game = await context.Games.Include(game => game.Genres).ThenInclude(link => link.Genre)
            .Include(game => game.Platforms).ThenInclude(link => link.Platform).SingleAsync();
        Assert.Equal(new DateOnly(2022, 2, 25), game.ReleaseDate);
        Assert.Null(game.Summary);
        Assert.Null(game.CoverUrl);
        Assert.Equal("RPG", Assert.Single(game.Genres).Genre.Name);
        Assert.Null(Assert.Single(game.Platforms).Platform.Abbreviation);
        Assert.Equal(2, await context.UserGames.CountAsync());
        Assert.Equal(1, await context.Games.CountAsync());
        var item = await context.UserGames.SingleAsync(item => item.Id == seed.Item.Id);
        Assert.Equal(GameStatus.Backlog, item.Status);
        Assert.Equal(BacklogPriority.High, item.Priority);
        Assert.Equal(1, item.BacklogOrder);
        Assert.Equal(TimeSpan.Zero, item.AddedAt.Offset);
        Assert.Null(item.Rating);
        Assert.Null(item.PersonalNote);
    });

    [Theory]
    [InlineData("game")]
    [InlineData("genre")]
    [InlineData("platform")]
    public Task Duplicate_external_IDs_are_rejected(string kind) => WithSeed(async (context, seed) =>
    {
        if (kind == "game") context.Games.Add(new Game(seed.Game.IgdbId, "Duplicate", "duplicate", null, null, null));
        if (kind == "genre") context.Genres.Add(new Genre(seed.Genre.IgdbId, "Duplicate"));
        if (kind == "platform") context.Platforms.Add(new Platform(seed.Platform.IgdbId, "Duplicate", null));
        await AssertDatabaseError(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation);
    });

    [Fact]
    public Task Same_user_cannot_have_two_records_for_one_game() => WithSeed(async (context, seed) =>
    {
        context.UserGames.Add(new UserGame(seed.User.Id, seed.Game.Id, GameStatus.Playing, null, null, DateTimeOffset.UtcNow));
        await AssertDatabaseError(() => context.SaveChangesAsync(), PostgresErrorCodes.UniqueViolation);
    });

    [Theory]
    [InlineData("GameGenres", "GenreId")]
    [InlineData("GamePlatforms", "PlatformId")]
    public Task Association_pairs_are_unique(string table, string relatedColumn) => WithSeed(async (context, seed) =>
    {
        Assert.Equal(table == "GameGenres" ? "GenreId" : "PlatformId", relatedColumn);
        FormattableString sql = table == "GameGenres"
            ? (FormattableString)$"INSERT INTO \"GameGenres\" (\"GameId\", \"GenreId\") VALUES ({seed.Game.Id}, {seed.Genre.Id})"
            : $"INSERT INTO \"GamePlatforms\" (\"GameId\", \"PlatformId\") VALUES ({seed.Game.Id}, {seed.Platform.Id})";
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(sql), PostgresErrorCodes.UniqueViolation);
    });

    [Theory]
    [InlineData("Games")]
    [InlineData("Genres")]
    [InlineData("Platforms")]
    public Task Nonpositive_external_IDs_are_rejected_by_the_database(string table) => WithSeed(async (context, _) =>
    {
        var sql = table switch
        {
            "Games" => "UPDATE \"Games\" SET \"IgdbId\" = 0",
            "Genres" => "UPDATE \"Genres\" SET \"IgdbId\" = 0",
            "Platforms" => "UPDATE \"Platforms\" SET \"IgdbId\" = 0",
            _ => throw new ArgumentException("Unexpected table.")
        };
        await AssertDatabaseError(() => context.Database.ExecuteSqlRawAsync(sql), PostgresErrorCodes.CheckViolation);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public Task Invalid_ratings_are_rejected_by_the_database(int rating) => WithSeed(async (context, seed) =>
    {
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"UserGames\" SET \"Rating\" = {rating} WHERE \"Id\" = {seed.Item.Id}"),
            PostgresErrorCodes.CheckViolation);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public Task Valid_ratings_and_maximum_note_length_are_accepted(int rating) => WithSeed(async (context, seed) =>
    {
        var note = new string('x', 1000);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"UserGames\" SET \"Rating\" = {rating}, \"PersonalNote\" = {note} WHERE \"Id\" = {seed.Item.Id}");
        context.ChangeTracker.Clear();
        var item = await context.UserGames.SingleAsync();
        Assert.Equal(rating, item.Rating);
        Assert.Equal(note, item.PersonalNote);
    });

    [Fact]
    public Task Oversized_notes_are_rejected_by_the_database() => WithSeed(async (context, seed) =>
    {
        var note = new string('x', 1001);
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"UserGames\" SET \"PersonalNote\" = {note} WHERE \"Id\" = {seed.Item.Id}"),
            PostgresErrorCodes.StringDataRightTruncation);
    });

    [Theory]
    [InlineData("Status", "Invalid", "CK_UserGames_Status")]
    [InlineData("Priority", "Invalid", "CK_UserGames_Priority")]
    public Task Invalid_enum_names_are_rejected(string column, string value, string constraint) => WithSeed(async (context, seed) =>
    {
        FormattableString sql = column == "Status"
            ? (FormattableString)$"UPDATE \"UserGames\" SET \"Status\" = {value}, \"Priority\" = NULL, \"BacklogOrder\" = NULL WHERE \"Id\" = {seed.Item.Id}"
            : $"UPDATE \"UserGames\" SET \"Priority\" = {value} WHERE \"Id\" = {seed.Item.Id}";
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(sql), PostgresErrorCodes.CheckViolation, constraint);
    });

    [Theory]
    [InlineData("\"BacklogOrder\" = NULL")]
    [InlineData("\"BacklogOrder\" = 0")]
    [InlineData("\"Status\" = 'Playing'")]
    [InlineData("\"Status\" = 'Playing', \"BacklogOrder\" = NULL")]
    public Task Invalid_backlog_field_combinations_are_rejected(string assignment) => WithSeed(async (context, seed) =>
    {
        var sql = assignment switch
        {
            "\"BacklogOrder\" = NULL" => "UPDATE \"UserGames\" SET \"BacklogOrder\" = NULL WHERE \"Id\" = {0}",
            "\"BacklogOrder\" = 0" => "UPDATE \"UserGames\" SET \"BacklogOrder\" = 0 WHERE \"Id\" = {0}",
            "\"Status\" = 'Playing'" => "UPDATE \"UserGames\" SET \"Status\" = 'Playing' WHERE \"Id\" = {0}",
            "\"Status\" = 'Playing', \"BacklogOrder\" = NULL" => "UPDATE \"UserGames\" SET \"Status\" = 'Playing', \"BacklogOrder\" = NULL WHERE \"Id\" = {0}",
            _ => throw new ArgumentException("Unexpected assignment.")
        };
        await AssertDatabaseError(() => context.Database.ExecuteSqlRawAsync(sql, seed.Item.Id),
            PostgresErrorCodes.CheckViolation, "CK_UserGames_BacklogFields");
    });

    [Theory]
    [InlineData("UserId")]
    [InlineData("GameId")]
    public Task Missing_ownership_and_game_references_are_rejected(string column) => WithSeed(async (context, seed) =>
    {
        var missingId = Guid.NewGuid();
        FormattableString sql = column == "UserId"
            ? (FormattableString)$"UPDATE \"UserGames\" SET \"UserId\" = {missingId} WHERE \"Id\" = {seed.Item.Id}"
            : $"UPDATE \"UserGames\" SET \"GameId\" = {missingId} WHERE \"Id\" = {seed.Item.Id}";
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(sql), PostgresErrorCodes.ForeignKeyViolation);
    });

    [Fact]
    public Task Deleting_a_library_record_preserves_all_shared_metadata() => WithSeed(async (context, seed) =>
    {
        context.UserGames.Remove(seed.Item);
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.Games.CountAsync());
        Assert.Equal(1, await context.Genres.CountAsync());
        Assert.Equal(1, await context.Platforms.CountAsync());
        Assert.Equal(1, await context.GameGenres.CountAsync());
        Assert.Equal(1, await context.GamePlatforms.CountAsync());
    });

    [Fact]
    public Task Deleting_an_account_cascades_to_library_records_without_deleting_games() => WithSeed(async (context, seed) =>
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = {seed.User.Id}");
        Assert.False(await context.UserGames.AnyAsync());
        Assert.True(await context.Games.AnyAsync());
    });

    [Fact]
    public Task Referenced_games_cannot_be_deleted() => WithSeed(async (context, seed) =>
    {
        await AssertDatabaseError(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Games\" WHERE \"Id\" = {seed.Game.Id}"), PostgresErrorCodes.RestrictViolation);
    });

    private async Task WithSeed(Func<LudaryxDbContext, Seed, Task> check)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = NewUser();
        var game = new Game(119133, "Elden Ring", "elden-ring", null, null, new DateOnly(2022, 2, 25));
        var genre = new Genre(12, "RPG");
        var platform = new Platform(6, "PC", null);
        game.Genres.Add(new GameGenre(game.Id, genre.Id));
        game.Platforms.Add(new GamePlatform(game.Id, platform.Id));
        var item = new UserGame(user.Id, game.Id, GameStatus.Backlog, BacklogPriority.High, 1, DateTimeOffset.UtcNow);
        context.AddRange(user, game, genre, platform, item);
        await context.SaveChangesAsync();
        await check(context, new Seed(user, game, genre, platform, item));
        // Every seeded record is rolled back; nothing is written to the development database.
    }

    private static ApplicationUser NewUser()
    {
        var id = Guid.NewGuid();
        var name = $"user{id:N}"[..20];
        return new ApplicationUser
        {
            Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.com", NormalizedEmail = $"{name}@EXAMPLE.COM", DisplayName = name
        };
    }

    private static async Task AssertDatabaseError(Func<Task> action, string sqlState, string? constraint = null)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);
        var postgres = Assert.IsType<PostgresException>(exception is DbUpdateException ? exception.InnerException : exception);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraint is not null) Assert.Equal(constraint, postgres.ConstraintName);
    }

    private sealed record Seed(ApplicationUser User, Game Game, Genre Genre, Platform Platform, UserGame Item);
}

public sealed class LibraryDatabaseFixture : IAsyncLifetime
{
    private readonly string databaseName = $"ludaryx_m3_test_{Guid.NewGuid():N}";
    private string administrationConnection = null!;
    private DbContextOptions<LudaryxDbContext> options = null!;
    private bool created;

    public LudaryxDbContext CreateContext() => new(options);

    public async Task InitializeAsync()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var scope = factory.Services.CreateAsyncScope();
        var configured = scope.ServiceProvider.GetRequiredService<LudaryxDbContext>();
        var connection = new NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString());
        administrationConnection = connection.ConnectionString;
        await using var administration = new NpgsqlConnection(administrationConnection);
        await administration.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", administration);
        await create.ExecuteNonQueryAsync();
        created = true;
        connection.Database = databaseName;
        // Reuse connections within this isolated fixture; DROP DATABASE WITH FORCE closes them on cleanup.
        connection.Pooling = true;
        options = new DbContextOptionsBuilder<LudaryxDbContext>().UseNpgsql(connection.ConnectionString).Options;
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        // Only this fixture's generated database is removed; the development database is preserved.
        await using var administration = new NpgsqlConnection(administrationConnection);
        await administration.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", administration);
        await drop.ExecuteNonQueryAsync();
        created = false;
    }
}
