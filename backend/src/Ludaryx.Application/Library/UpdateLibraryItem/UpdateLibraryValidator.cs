using FluentValidation;
using Ludaryx.Domain.Library;

namespace Ludaryx.Application.Library.UpdateLibraryItem;

public sealed class UpdateLibraryValidator : AbstractValidator<UpdateLibraryCommand>
{
    public UpdateLibraryValidator()
    {
        RuleFor(command => command).Must(command => command.HasStatus || command.HasPriority || command.HasRating || command.HasPersonalNote)
            .WithMessage("At least one editable field is required.").OverridePropertyName("request");
        RuleFor(command => command.Status).Must(value => Enum.GetNames<GameStatus>().Contains(value, StringComparer.Ordinal))
            .When(command => command.HasStatus);
        RuleFor(command => command.Priority).Must(value => value is null || Enum.GetNames<BacklogPriority>().Contains(value, StringComparer.Ordinal))
            .When(command => command.HasPriority);
        RuleFor(command => command.Rating).Must(value => value is null or >= 1 and <= 10).When(command => command.HasRating);
        RuleFor(command => command.PersonalNote).MaximumLength(1000).When(command => command.HasPersonalNote);
    }
}
