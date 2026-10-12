namespace Ludaryx.Application.Library.GetLibraryItem;

public sealed class GetLibraryItemHandler(ILibraryQueryStore library)
{
    public Task<LibraryItemDetails?> HandleAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        library.GetAsync(userId, id, cancellationToken);
}
