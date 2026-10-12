using Ludaryx.Application.Games;
using Ludaryx.Domain.Library;

namespace Ludaryx.Application.Library.AddGame;

public enum LibraryAdditionState
{
    NewGame,
    ExistingGame,
    AlreadyAdded,
    UserMissing
}

public sealed record LibraryAddition(
    Guid UserId, long IgdbId, GameStatus Status, BacklogPriority? Priority, GameDetails? Snapshot);

public interface ILibraryAdditionStore
{
    Task<LibraryAdditionState> GetStateAsync(Guid userId, long igdbId, CancellationToken cancellationToken);
    Task<AddGameResult> AddAsync(LibraryAddition addition, CancellationToken cancellationToken);
}
