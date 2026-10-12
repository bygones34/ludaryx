using Ludaryx.Application.Library;
using Ludaryx.Application.Library.UpdateLibraryItem;
using Ludaryx.Domain.Library;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludaryx.Infrastructure.Library;

public sealed class EfLibraryMutationStore(LudaryxDbContext context, TimeProvider time) : ILibraryMutationStore
{
    public async Task<UpdateLibraryResult> UpdateAsync(Guid userId, Guid id, UpdateLibraryCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockUser(userId, cancellationToken)) return new UpdateLibraryResult.NotFound();
        var item = await LibraryMapping.WithDetails(context.UserGames.Where(item => item.UserId == userId && item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
        if (item is null) return new UpdateLibraryResult.NotFound();
        var status = command.HasStatus ? Enum.Parse<GameStatus>(command.Status!) : item.Status;
        BacklogPriority? priority;
        int? order;
        if (status == GameStatus.Backlog)
        {
            priority = command.HasPriority
                ? command.Priority is null ? null : Enum.Parse<BacklogPriority>(command.Priority)
                : item.Status == GameStatus.Backlog ? item.Priority : null;
            order = item.BacklogOrder;
            if (item.Status != GameStatus.Backlog)
            {
                var maximum = await context.UserGames.Where(other => other.UserId == userId && other.Status == GameStatus.Backlog)
                    .MaxAsync(other => other.BacklogOrder, cancellationToken);
                order = checked((maximum ?? 0) + 1);
            }
        }
        else
        {
            if (command.HasPriority && command.Priority is not null)
                return new UpdateLibraryResult.Invalid(new Dictionary<string, string[]> { ["priority"] = ["Priority applies only to Backlog."] });
            priority = null;
            order = null;
        }
        item.Update(status, priority, order, command.HasRating ? command.Rating : item.Rating,
            command.HasPersonalNote ? command.PersonalNote : item.PersonalNote, time.GetUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        // Return persisted scalar values, including PostgreSQL's microsecond timestamp precision.
        await context.Entry(item).ReloadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new UpdateLibraryResult.Updated(LibraryMapping.Details(item));
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockUser(userId, cancellationToken)) return false;
        var removed = await context.UserGames.Where(item => item.UserId == userId && item.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return removed == 1;
    }

    private async Task<bool> LockUser(Guid userId, CancellationToken cancellationToken) =>
        (await context.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken)).Count == 1;
}
