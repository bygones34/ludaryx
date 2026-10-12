using FluentValidation;
using Ludaryx.Domain.Library;

namespace Ludaryx.Application.Library.AddGame;

public sealed class AddGameValidator : AbstractValidator<AddGameCommand>
{
    public AddGameValidator()
    {
        RuleFor(command => command.IgdbId).GreaterThan(0);
        RuleFor(command => command.Status)
            .Must(value => Enum.GetNames<GameStatus>().Contains(value, StringComparer.Ordinal))
            .WithMessage("Status must be Backlog, Playing, Completed, OnHold, or Dropped.");
        RuleFor(command => command.Priority)
            .Must(value => value is null || Enum.GetNames<BacklogPriority>().Contains(value, StringComparer.Ordinal))
            .WithMessage("Priority must be null, Low, Medium, or High.");
        RuleFor(command => command.Priority)
            .Must((command, value) => value is null || command.Status == nameof(GameStatus.Backlog))
            .WithMessage("Priority applies only to Backlog.");
    }
}
