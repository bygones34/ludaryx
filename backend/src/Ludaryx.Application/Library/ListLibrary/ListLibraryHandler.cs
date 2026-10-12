using FluentValidation;

namespace Ludaryx.Application.Library.ListLibrary;

public sealed class ListLibraryHandler(IValidator<ListLibraryQuery> validator, ILibraryQueryStore library)
{
    public async Task<ListLibraryResult> HandleAsync(Guid userId, ListLibraryQuery query, CancellationToken cancellationToken)
    {
        query = query with { Search = query.Search?.Trim() };
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new ListLibraryResult.Invalid(validation.Errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
        }
        return new ListLibraryResult.Listed(await library.ListAsync(userId, query, cancellationToken));
    }
}
