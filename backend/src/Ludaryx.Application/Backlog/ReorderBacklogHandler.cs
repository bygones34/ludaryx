using FluentValidation;

namespace Ludaryx.Application.Backlog;

public sealed class ReorderBacklogHandler(IValidator<ReorderBacklogCommand> validator, IBacklogStore backlog)
{
    public async Task<ReorderBacklogResult> HandleAsync(Guid userId, ReorderBacklogCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return new ReorderBacklogResult.Invalid(validation.Errors.GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
        return await backlog.ReorderAsync(userId, command, cancellationToken);
    }
}
