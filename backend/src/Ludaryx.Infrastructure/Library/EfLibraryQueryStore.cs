using System.Data;
using Ludaryx.Application.Games.GetGameDetails;
using Ludaryx.Application.Library;
using Ludaryx.Application.Library.ListLibrary;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Library;

public sealed class EfLibraryQueryStore(LudaryxDbContext context) : ILibraryQueryStore, IGameLibraryMembership
{
    public async Task<LibraryPage> ListAsync(Guid userId, ListLibraryQuery request, CancellationToken cancellationToken)
    {
        // Count and page are read from one snapshot while other requests can mutate the library.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var query = context.UserGames.AsNoTracking().Where(item => item.UserId == userId);
        var availablePlatforms = await query.SelectMany(item => item.Game.Platforms)
            .Select(link => new { link.Platform.IgdbId, link.Platform.Name }).Distinct()
            .OrderBy(platform => platform.Name).ThenBy(platform => platform.IgdbId).ToListAsync(cancellationToken);
        if (request.Status is { } status)
        {
            var value = Enum.Parse<GameStatus>(status);
            query = query.Where(item => item.Status == value);
        }
        if (request.Priority is { } priority)
        {
            var value = Enum.Parse<BacklogPriority>(priority);
            query = query.Where(item => item.Priority == value);
        }
        if (request.Platform is { } platform) query = query.Where(item => item.Game.Platforms.Any(link => link.Platform.IgdbId == platform));
        if (request.Rating is { } rating) query = query.Where(item => item.Rating == rating);
        if (!string.IsNullOrEmpty(request.Search))
        {
            var pattern = "%" + request.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(item => EF.Functions.ILike(item.Game.Name, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        IOrderedQueryable<UserGame> sorted = request.Sort switch
        {
            "addedAtAsc" => query.OrderBy(item => item.AddedAt),
            "nameAsc" => query.OrderBy(item => item.Game.Name),
            "nameDesc" => query.OrderByDescending(item => item.Game.Name),
            "ratingAsc" => query.OrderBy(item => item.Rating == null).ThenBy(item => item.Rating),
            "ratingDesc" => query.OrderBy(item => item.Rating == null).ThenByDescending(item => item.Rating),
            "completedAtAsc" => query.OrderBy(item => item.CompletedAt == null).ThenBy(item => item.CompletedAt),
            "completedAtDesc" => query.OrderBy(item => item.CompletedAt == null).ThenByDescending(item => item.CompletedAt),
            _ => query.OrderByDescending(item => item.AddedAt)
        };
        var offset = ((long)request.Page - 1) * request.PageSize;
        var items = offset >= total ? [] : await sorted.ThenBy(item => item.Id).Include(item => item.Game)
            .Skip((int)offset).Take(request.PageSize).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LibraryPage(items.Select(LibraryMapping.ListItem).ToArray(), request.Page, request.PageSize,
            total, (int)(((long)total + request.PageSize - 1) / request.PageSize),
            availablePlatforms.Select(platform => new LibraryPlatformOption(platform.IgdbId, platform.Name)).ToArray());
    }

    public async Task<LibraryItemDetails?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var item = await LibraryMapping.WithDetails(context.UserGames.AsNoTracking()
            .Where(item => item.UserId == userId && item.Id == id)).SingleOrDefaultAsync(cancellationToken);
        return item is null ? null : LibraryMapping.Details(item);
    }

    public async Task<GameLibraryMembership> GetAsync(Guid userId, long igdbId, CancellationToken cancellationToken)
    {
        var item = await context.UserGames.AsNoTracking().Where(item => item.UserId == userId && item.Game.IgdbId == igdbId)
            .Select(item => new { item.Id, item.Status }).SingleOrDefaultAsync(cancellationToken);
        return item is null ? new GameLibraryMembership(false, null, null)
            : new GameLibraryMembership(true, item.Id, item.Status.ToString());
    }
}
