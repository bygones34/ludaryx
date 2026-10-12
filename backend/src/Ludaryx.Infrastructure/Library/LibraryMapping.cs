using Ludaryx.Application.Library;
using Ludaryx.Application.Library.AddGame;
using Ludaryx.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Library;

internal static class LibraryMapping
{
    public static IQueryable<UserGame> WithDetails(IQueryable<UserGame> query) => query
        .Include(item => item.Game).ThenInclude(game => game.Genres).ThenInclude(link => link.Genre)
        .Include(item => item.Game).ThenInclude(game => game.Platforms).ThenInclude(link => link.Platform)
        .AsSplitQuery();

    public static LibraryListItem ListItem(UserGame item) => new(item.Id,
        new LibraryGameSummary(item.Game.Id, item.Game.IgdbId, item.Game.Name, item.Game.CoverUrl),
        item.Status.ToString(), item.Rating, item.Priority?.ToString(), item.AddedAt, item.CompletedAt);

    public static LibraryItemDetails Details(UserGame item) => new(item.Id,
        new StoredGameDetails(item.Game.Id, item.Game.IgdbId, item.Game.Name, item.Game.Slug,
            item.Game.Summary, item.Game.CoverUrl, item.Game.ReleaseDate,
            item.Game.Genres.OrderBy(link => link.Genre.IgdbId)
                .Select(link => new Ludaryx.Application.Games.GameGenre(link.Genre.IgdbId, link.Genre.Name)).ToArray(),
            item.Game.Platforms.OrderBy(link => link.Platform.IgdbId)
                .Select(link => new Ludaryx.Application.Games.GamePlatform(link.Platform.IgdbId, link.Platform.Name, link.Platform.Abbreviation)).ToArray()),
        item.Status.ToString(), item.Priority?.ToString(), item.BacklogOrder, item.Rating, item.PersonalNote,
        item.AddedAt, item.UpdatedAt, item.StartedAt, item.CompletedAt);
}
