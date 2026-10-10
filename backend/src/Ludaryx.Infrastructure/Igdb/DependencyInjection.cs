using Ludaryx.Application.Games;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ludaryx.Infrastructure.Igdb;

public static class DependencyInjection
{
    public static IServiceCollection AddIgdb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => IgdbSettings.FromConfiguration(configuration));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IgdbTokenProvider>();
        services.AddSingleton<IgdbRequestLimiter>();
        services.AddHttpClient(IgdbTokenProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://id.twitch.tv/");
            client.Timeout = IgdbSettings.RequestTimeout;
        }).RemoveAllLoggers();
        services.AddHttpClient<IIgdbClient, IgdbClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.igdb.com/v4/");
            client.Timeout = IgdbSettings.RequestTimeout;
        }).RemoveAllLoggers();
        return services;
    }
}
