using FluentValidation;

namespace Ludaryx.Application.Backlog;

public sealed class ReorderBacklogValidator : AbstractValidator<ReorderBacklogCommand>
{
    public ReorderBacklogValidator()
    {
        RuleFor(command => command).Custom((command, validation) =>
        {
            if (command.Items is null)
            {
                validation.AddFailure("items", "The complete backlog items array is required.");
                return;
            }
            var ids = new HashSet<Guid>();
            var orders = new HashSet<int>();
            for (var index = 0; index < command.Items.Count; index++)
            {
                var item = command.Items[index];
                if (item is null)
                {
                    validation.AddFailure($"items[{index}]", "An order item must not be null.");
                    continue;
                }
                if (item.UserGameId == Guid.Empty || !ids.Add(item.UserGameId))
                    validation.AddFailure($"items[{index}].userGameId", "Use a non-empty, unique library item ID.");
                if (item.Order < 1 || item.Order > command.Items.Count || !orders.Add(item.Order))
                    validation.AddFailure($"items[{index}].order", "Orders must be unique integers from 1 through the item count.");
            }
        });
    }
}
