namespace Ludaryx.Application.Library.AddGame;

public sealed record AddGameCommand(long IgdbId, string? Status, string? Priority);
