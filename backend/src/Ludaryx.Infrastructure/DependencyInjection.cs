using Ludaryx.Application.Auth;
using Ludaryx.Application.Auth.RegisterUser;
using Ludaryx.Infrastructure.Authentication;
using Ludaryx.Infrastructure.Identity;
using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
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

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 15;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<LudaryxDbContext>()
            .AddSignInManager();

        services.AddScoped<IUserRegistrationStore, IdentityUserRegistrationStore>();
        services.AddScoped<IUserAccountStore, IdentityUserAccountStore>();
        services.AddScoped<IRefreshSessionStore, EfRefreshSessionStore>();

        return services;
    }
}
