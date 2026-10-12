using Ludaryx.Domain.Games;

namespace Ludaryx.Domain.Library;

public sealed class UserGame
{
    private UserGame() { }

    public UserGame(Guid userId, Guid gameId, GameStatus status, BacklogPriority? priority,
        int? backlogOrder, DateTimeOffset addedAt)
    {
        if (userId == Guid.Empty || gameId == Guid.Empty)
            throw new ArgumentException("User and game IDs must not be empty.");
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (priority is { } value && !Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(priority));
        if (status == GameStatus.Backlog)
        {
            if (backlogOrder is null or <= 0) throw new ArgumentOutOfRangeException(nameof(backlogOrder));
        }
        else if (priority is not null || backlogOrder is not null)
        {
            throw new ArgumentException("Priority and order apply only to Backlog.");
        }

        UserId = userId;
        GameId = gameId;
        Status = status;
        Priority = priority;
        BacklogOrder = backlogOrder;
        AddedAt = addedAt.ToUniversalTime();
        UpdatedAt = AddedAt;
        StartedAt = status == GameStatus.Playing ? AddedAt : null;
        CompletedAt = status == GameStatus.Completed ? AddedAt : null;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid GameId { get; private set; }
    public Game Game { get; private set; } = null!;
    public GameStatus Status { get; private set; }
    public BacklogPriority? Priority { get; private set; }
    public int? BacklogOrder { get; private set; }
    public int? Rating { get; private set; }
    public string? PersonalNote { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Update(GameStatus status, BacklogPriority? priority, int? backlogOrder,
        int? rating, string? personalNote, DateTimeOffset updatedAt)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (priority is { } value && !Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (status == GameStatus.Backlog && backlogOrder is null or <= 0)
            throw new ArgumentOutOfRangeException(nameof(backlogOrder));
        if (status != GameStatus.Backlog && (priority is not null || backlogOrder is not null))
            throw new ArgumentException("Priority and order apply only to Backlog.");
        if (rating is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(rating));
        if (personalNote?.Length > 1000) throw new ArgumentException("Personal note must not exceed 1000 characters.", nameof(personalNote));

        Status = status;
        Priority = priority;
        BacklogOrder = backlogOrder;
        Rating = rating;
        PersonalNote = personalNote;
        UpdatedAt = updatedAt.ToUniversalTime();
        if (status == GameStatus.Playing) StartedAt ??= UpdatedAt;
        if (status == GameStatus.Completed) CompletedAt ??= UpdatedAt;
    }
}
