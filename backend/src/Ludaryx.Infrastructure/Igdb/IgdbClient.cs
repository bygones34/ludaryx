using System.Net.Http.Headers;
using System.Text;
using Ludaryx.Application.Games;
using Ludaryx.Infrastructure.Igdb.Models;
using Microsoft.Extensions.Logging;

namespace Ludaryx.Infrastructure.Igdb;

public sealed class IgdbClient(
    HttpClient http, IgdbSettings settings, IgdbTokenProvider tokens,
    IgdbRequestLimiter limiter, ILogger<IgdbClient> logger) : IIgdbClient
{
    public async Task<IReadOnlyList<GameSearchItem>> SearchAsync(
        string query, CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();
        if (trimmed.Length is < 2 or > 100 || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Search query must contain 2–100 printable characters.", nameof(query));
        }

        var escaped = trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var games = await QueryAsync(
            $"search \"{escaped}\"; fields name,cover.image_id,first_release_date,platforms.name; limit 20;",
            cancellationToken);
        return games.Take(20).Select(game =>
        {
            var value = RequireGame(game);
            return new GameSearchItem(value.Id, RequiredText(value.Name), CoverUrl(value.Cover),
                ReleaseDate(value.FirstReleaseDate), (value.Platforms ?? [])
                    .Select(platform => RequiredText(platform?.Name)).ToArray());
        }).ToArray();
    }

    public async Task<GameDetails?> GetDetailsAsync(long igdbId, CancellationToken cancellationToken)
    {
        if (igdbId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(igdbId));
        }

        var games = await QueryAsync(
            $"fields name,slug,summary,cover.image_id,first_release_date,genres.name,platforms.name,platforms.abbreviation; where id = {igdbId}; limit 1;",
            cancellationToken);
        if (games.Length == 0)
        {
            return null;
        }
        if (games.Length != 1 || games[0]?.Id != igdbId)
        {
            throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }

        var game = RequireGame(games[0]);
        return new GameDetails(game.Id, RequiredText(game.Name), RequiredText(game.Slug),
            OptionalText(game.Summary), CoverUrl(game.Cover), ReleaseDate(game.FirstReleaseDate),
            (game.Genres ?? []).Select(genre => new GameGenre(RequiredId(genre?.Id), RequiredText(genre?.Name))).ToArray(),
            (game.Platforms ?? []).Select(platform => new GamePlatform(
                RequiredId(platform?.Id), RequiredText(platform?.Name), OptionalText(platform?.Abbreviation))).ToArray());
    }

    private async Task<IgdbGame?[]> QueryAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await tokens.GetTokenAsync(cancellationToken);
                using var lease = await limiter.AcquireAsync(cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Post, "games")
                {
                    Content = new StringContent(query, Encoding.UTF8, "text/plain")
                };
                request.Headers.Add("Client-ID", settings.ClientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                try
                {
                    return await IgdbHttp.SendAsync<IgdbGame?[]>(
                        http, request, allowTokenRenewal: attempt == 0, cancellationToken);
                }
                catch (IgdbUnauthorizedException)
                {
                    await tokens.InvalidateAsync(token, cancellationToken);
                }
            }
            throw new GameProviderException(GameProviderFailure.Authentication);
        }
        catch (GameProviderException exception)
        {
            logger.LogWarning("IGDB metadata request failed: {Failure}.", exception.Failure);
            throw;
        }
    }

    private static IgdbGame RequireGame(IgdbGame? game) =>
        game is { Id: > 0 } ? game : throw new GameProviderException(GameProviderFailure.InvalidResponse);

    private static long RequiredId(long? id) =>
        id is > 0 ? id.Value : throw new GameProviderException(GameProviderFailure.InvalidResponse);

    private static string RequiredText(string? text) =>
        OptionalText(text) ?? throw new GameProviderException(GameProviderFailure.InvalidResponse);

    private static string? OptionalText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static DateOnly? ReleaseDate(long? timestamp)
    {
        if (timestamp is null) return null;
        try
        {
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }
    }

    private static string? CoverUrl(IgdbCover? cover)
    {
        if (OptionalText(cover?.ImageId) is not { } imageId) return null;
        if (!imageId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }
        return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
    }
}
