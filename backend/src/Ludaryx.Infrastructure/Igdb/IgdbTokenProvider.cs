using System.Text.Json.Serialization;
using Ludaryx.Application.Games;
using Microsoft.Extensions.Logging;

namespace Ludaryx.Infrastructure.Igdb;

public sealed class IgdbTokenProvider(
    IHttpClientFactory clients, IgdbSettings settings, TimeProvider time,
    ILogger<IgdbTokenProvider> logger) : IDisposable
{
    public const string HttpClientName = "IgdbAuthentication";
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset renewAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (token is not null && time.GetUtcNow() < renewAt)
            {
                return token;
            }

            using var client = clients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                    ["grant_type"] = "client_credentials"
                })
            };
            var response = await IgdbHttp.SendAsync<TokenResponse>(
                client, request, allowTokenRenewal: false, cancellationToken);
            if (string.IsNullOrWhiteSpace(response.AccessToken) || response.ExpiresIn <= 0 ||
                !string.Equals(response.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new GameProviderException(GameProviderFailure.InvalidResponse);
            }

            try
            {
                renewAt = time.GetUtcNow().AddSeconds(response.ExpiresIn - 60);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new GameProviderException(GameProviderFailure.InvalidResponse);
            }
            token = response.AccessToken;
            return token;
        }
        catch (GameProviderException exception)
        {
            logger.LogWarning("IGDB token acquisition failed: {Failure}.", exception.Failure);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task InvalidateAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // An older 401 must not invalidate a token another caller has already renewed.
            if (token == rejectedToken)
            {
                token = null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; init; }
        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
