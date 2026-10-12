using Ludaryx.Application.Library.ListLibrary;

namespace Ludaryx.Application.Library;

public interface ILibraryQueryStore
{
    Task<LibraryPage> ListAsync(Guid userId, ListLibraryQuery query, CancellationToken cancellationToken);
    Task<LibraryItemDetails?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
