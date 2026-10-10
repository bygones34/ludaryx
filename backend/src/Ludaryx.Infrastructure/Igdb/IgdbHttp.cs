using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ludaryx.Application.Games;

namespace Ludaryx.Infrastructure.Igdb;

internal static class IgdbHttp
{
    public static async Task<T> SendAsync<T>(
        HttpClient client, HttpRequestMessage request, bool allowTokenRenewal,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && allowTokenRenewal)
            {
                throw new IgdbUnauthorizedException();
            }

            if (!response.IsSuccessStatusCode)
            {
                var failure = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => GameProviderFailure.Authentication,
                    HttpStatusCode.TooManyRequests => GameProviderFailure.Unavailable,
                    _ when (int)response.StatusCode >= 500 => GameProviderFailure.Unavailable,
                    _ => GameProviderFailure.InvalidResponse
                };
                throw new GameProviderException(failure);
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GameProviderException(GameProviderFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new GameProviderException(GameProviderFailure.Unavailable);
        }
        catch (JsonException)
        {
            throw new GameProviderException(GameProviderFailure.InvalidResponse);
        }
    }
}

internal sealed class IgdbUnauthorizedException : Exception;
