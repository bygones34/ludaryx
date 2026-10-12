using FluentValidation;
using Ludaryx.Application.Games;
using Ludaryx.Domain.Library;

namespace Ludaryx.Application.Library.AddGame;

public sealed class AddGameHandler(IValidator<AddGameCommand> validator, ILibraryAdditionStore library, IIgdbClient games)
{
    public async Task<AddGameResult> HandleAsync(Guid userId, AddGameCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
            return new AddGameResult.Invalid(errors);
        }

        var state = await library.GetStateAsync(userId, command.IgdbId, cancellationToken);
        if (state == LibraryAdditionState.UserMissing) return new AddGameResult.UserMissing();
        if (state == LibraryAdditionState.AlreadyAdded) return new AddGameResult.Duplicate();
        GameDetails? snapshot = null;
        if (state == LibraryAdditionState.NewGame)
        {
            snapshot = await games.GetDetailsAsync(command.IgdbId, cancellationToken);
            if (snapshot is null) return new AddGameResult.NotFound();
            if (snapshot.IgdbId != command.IgdbId) throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }

        return await library.AddAsync(new LibraryAddition(userId, command.IgdbId,
            Enum.Parse<GameStatus>(command.Status!),
            command.Priority is null ? null : Enum.Parse<BacklogPriority>(command.Priority), snapshot), cancellationToken);
    }
}
