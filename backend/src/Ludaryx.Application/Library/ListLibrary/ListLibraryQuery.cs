namespace Ludaryx.Application.Library.ListLibrary;

public sealed record ListLibraryQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Status { get; init; }
    public long? Platform { get; init; }
    public int? Rating { get; init; }
    public string? Priority { get; init; }
    public string? Sort { get; init; } = "addedAtDesc";
    public string? Search { get; init; }
}

public abstract record ListLibraryResult
{
    public sealed record Listed(LibraryPage Library) : ListLibraryResult;
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ListLibraryResult;
}
