namespace Ludaryx.Application.Library.UpdateLibraryItem;

public sealed record UpdateLibraryCommand(
    bool HasStatus, string? Status, bool HasPriority, string? Priority,
    bool HasRating, int? Rating, bool HasPersonalNote, string? PersonalNote);

public abstract record UpdateLibraryResult
{
    public sealed record Updated(LibraryItemDetails Item) : UpdateLibraryResult;
    public sealed record NotFound : UpdateLibraryResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateLibraryResult;
}
