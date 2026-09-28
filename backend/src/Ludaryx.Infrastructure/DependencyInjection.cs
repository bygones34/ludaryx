using Ludaryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ludaryx.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:LudaryxDatabase must be configured.");
        }

        services.AddDbContext<LudaryxDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
