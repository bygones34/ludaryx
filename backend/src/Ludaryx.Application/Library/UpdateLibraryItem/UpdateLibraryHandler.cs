using FluentValidation;

namespace Ludaryx.Application.Library.UpdateLibraryItem;

public sealed class UpdateLibraryHandler(IValidator<UpdateLibraryCommand> validator, ILibraryMutationStore library)
{
    public async Task<UpdateLibraryResult> HandleAsync(Guid userId, Guid id, UpdateLibraryCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new UpdateLibraryResult.Invalid(validation.Errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
        }
        return await library.UpdateAsync(userId, id, command, cancellationToken);
    }
}
