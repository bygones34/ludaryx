using Microsoft.Extensions.Configuration;

namespace Ludaryx.Infrastructure.Igdb;

public sealed class IgdbSettings(string clientId, string clientSecret)
{
    public string ClientId { get; } = clientId;
    public string ClientSecret { get; } = clientSecret;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static IgdbSettings FromConfiguration(IConfiguration configuration)
    {
        var clientId = configuration["Igdb:ClientId"];
        var clientSecret = configuration["Igdb:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Igdb:ClientId and Igdb:ClientSecret must be configured through user-secrets or environment variables.");
        }

        return new IgdbSettings(clientId, clientSecret);
    }
}
