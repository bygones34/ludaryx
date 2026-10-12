namespace Ludaryx.Application.Games.GetGameDetails;

public interface IGameLibraryMembership
{
    Task<GameLibraryMembership> GetAsync(Guid userId, long igdbId, CancellationToken cancellationToken);
}
