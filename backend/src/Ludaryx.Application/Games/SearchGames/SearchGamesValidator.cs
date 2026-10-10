using FluentValidation;

namespace Ludaryx.Application.Games.SearchGames;

public sealed class SearchGamesValidator : AbstractValidator<SearchGamesQuery>
{
    public SearchGamesValidator()
    {
        RuleFor(query => query.Q)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Search query is required.")
            .Length(2, 100).WithMessage("Search query must contain 2–100 characters.")
            .Must(value => !value!.Any(char.IsControl))
            .WithMessage("Search query must not contain control characters.");
    }
}
