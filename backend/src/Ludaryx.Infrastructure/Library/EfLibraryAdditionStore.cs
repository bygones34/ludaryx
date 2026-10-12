using Ludaryx.Application.Library.AddGame;
using Ludaryx.Domain.Games;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Library;

public sealed class EfLibraryAdditionStore(LudaryxDbContext context, TimeProvider time) : ILibraryAdditionStore
{
    public async Task<LibraryAdditionState> GetStateAsync(Guid userId, long igdbId, CancellationToken cancellationToken)
    {
        if (!await context.Users.AnyAsync(user => user.Id == userId, cancellationToken)) return LibraryAdditionState.UserMissing;
        if (await context.UserGames.AnyAsync(item => item.UserId == userId && item.Game.IgdbId == igdbId, cancellationToken))
            return LibraryAdditionState.AlreadyAdded;
        return await context.Games.AnyAsync(game => game.IgdbId == igdbId, cancellationToken)
            ? LibraryAdditionState.ExistingGame : LibraryAdditionState.NewGame;
    }

    public async Task<AddGameResult> AddAsync(LibraryAddition addition, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Serialize this user's mutations across API processes, including backlog append calculations.
        var users = await context.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {addition.UserId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        if (users.Count == 0) return new AddGameResult.UserMissing();
        if (await context.UserGames.AnyAsync(item => item.UserId == addition.UserId && item.Game.IgdbId == addition.IgdbId, cancellationToken))
            return new AddGameResult.Duplicate();

        var game = await context.Games.AsNoTracking().SingleOrDefaultAsync(game => game.IgdbId == addition.IgdbId, cancellationToken);
        if (game is null)
        {
            var snapshot = addition.Snapshot;
            if (snapshot is null) return new AddGameResult.NotFound();
            var candidate = new Game(snapshot.IgdbId, snapshot.Name, snapshot.Slug, snapshot.Summary, snapshot.CoverUrl, snapshot.ReleaseDate);
            var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Games" ("Id", "IgdbId", "Name", "Slug", "Summary", "CoverUrl", "ReleaseDate")
                VALUES ({candidate.Id}, {candidate.IgdbId}, {candidate.Name}, {candidate.Slug}, {candidate.Summary}, {candidate.CoverUrl}, {candidate.ReleaseDate})
                ON CONFLICT ("IgdbId") DO NOTHING
                """, cancellationToken);
            // ON CONFLICT waits for a competing insert to commit; READ COMMITTED then sees its snapshot.
            game = await context.Games.AsNoTracking().SingleAsync(game => game.IgdbId == addition.IgdbId, cancellationToken);
            if (inserted == 1)
            {
                // Stable ordering avoids cycles when different new games share metadata concurrently.
                foreach (var metadata in snapshot.Genres.DistinctBy(genre => genre.IgdbId).OrderBy(genre => genre.IgdbId))
                {
                    var genre = new Genre(metadata.IgdbId, metadata.Name);
                    await context.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "Genres" ("Id", "IgdbId", "Name") VALUES ({genre.Id}, {genre.IgdbId}, {genre.Name})
                        ON CONFLICT ("IgdbId") DO NOTHING
                        """, cancellationToken);
                    var genreId = await context.Genres.Where(genre => genre.IgdbId == metadata.IgdbId)
                        .Select(genre => genre.Id).SingleAsync(cancellationToken);
                    context.GameGenres.Add(new GameGenre(game.Id, genreId));
                }
                foreach (var metadata in snapshot.Platforms.DistinctBy(platform => platform.IgdbId).OrderBy(platform => platform.IgdbId))
                {
                    var platform = new Platform(metadata.IgdbId, metadata.Name, metadata.Abbreviation);
                    await context.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "Platforms" ("Id", "IgdbId", "Name", "Abbreviation")
                        VALUES ({platform.Id}, {platform.IgdbId}, {platform.Name}, {platform.Abbreviation})
                        ON CONFLICT ("IgdbId") DO NOTHING
                        """, cancellationToken);
                    var platformId = await context.Platforms.Where(platform => platform.IgdbId == metadata.IgdbId)
                        .Select(platform => platform.Id).SingleAsync(cancellationToken);
                    context.GamePlatforms.Add(new GamePlatform(game.Id, platformId));
                }
            }
        }

        int? order = null;
        if (addition.Status == GameStatus.Backlog)
        {
            var maximum = await context.UserGames.Where(item => item.UserId == addition.UserId && item.Status == GameStatus.Backlog)
                .MaxAsync(item => item.BacklogOrder, cancellationToken);
            order = checked((maximum ?? 0) + 1);
        }
        var item = new UserGame(addition.UserId, game.Id, addition.Status, addition.Priority, order, time.GetUtcNow());
        context.UserGames.Add(item);
        await context.SaveChangesAsync(cancellationToken);
        await context.Entry(item).ReloadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AddGameResult.Added(new AddedLibraryItem(item.Id,
            new LibraryGameSummary(game.Id, game.IgdbId, game.Name, game.CoverUrl), item.Status.ToString(),
            item.Priority?.ToString(), item.BacklogOrder, item.Rating, item.PersonalNote, item.AddedAt));
    }
}
