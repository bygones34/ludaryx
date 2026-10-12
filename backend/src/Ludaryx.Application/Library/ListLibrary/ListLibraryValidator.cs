using FluentValidation;
using Ludaryx.Domain.Library;

namespace Ludaryx.Application.Library.ListLibrary;

public sealed class ListLibraryValidator : AbstractValidator<ListLibraryQuery>
{
    public ListLibraryValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Platform).Must(value => value is null or > 0);
        RuleFor(query => query.Rating).Must(value => value is null or >= 1 and <= 10);
        RuleFor(query => query.Status).Must(value => value is null || Enum.GetNames<GameStatus>().Contains(value, StringComparer.Ordinal));
        RuleFor(query => query.Priority).Must(value => value is null || Enum.GetNames<BacklogPriority>().Contains(value, StringComparer.Ordinal));
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Sort).Must(value => value is "addedAtDesc" or "addedAtAsc" or "nameAsc" or "nameDesc"
            or "ratingAsc" or "ratingDesc" or "completedAtAsc" or "completedAtDesc");
    }
}
