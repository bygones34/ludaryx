using Ludaryx.Application.Library.UpdateLibraryItem;

namespace Ludaryx.Application.Library;

public interface ILibraryMutationStore
{
    Task<UpdateLibraryResult> UpdateAsync(Guid userId, Guid id, UpdateLibraryCommand command, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
