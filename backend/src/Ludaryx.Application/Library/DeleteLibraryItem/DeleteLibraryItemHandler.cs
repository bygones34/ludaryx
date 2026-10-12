namespace Ludaryx.Application.Library.DeleteLibraryItem;

public sealed class DeleteLibraryItemHandler(ILibraryMutationStore library)
{
    public Task<bool> HandleAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        library.DeleteAsync(userId, id, cancellationToken);
}
