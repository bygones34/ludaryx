using FluentValidation;

namespace Ludaryx.Application.Games.GetGameDetails;

public sealed class GetGameDetailsValidator : AbstractValidator<GetGameDetailsQuery>
{
    public GetGameDetailsValidator()
    {
        RuleFor(query => query.IgdbId).GreaterThan(0)
            .WithMessage("IGDB ID must be a positive integer.");
    }
}
