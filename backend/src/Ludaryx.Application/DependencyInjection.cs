using FluentValidation;
using Ludaryx.Application.Auth.GetCurrentUser;
using Ludaryx.Application.Auth.LoginUser;
using Ludaryx.Application.Auth.LogoutUser;
using Ludaryx.Application.Auth.RefreshUser;
using Ludaryx.Application.Auth.RegisterUser;
using Ludaryx.Application.Games.SearchGames;
using Microsoft.Extensions.DependencyInjection;

namespace Ludaryx.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<RegisterUserCommand>, RegisterUserValidator>();
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<IValidator<LoginUserCommand>, LoginUserValidator>();
        services.AddScoped<LoginUserHandler>();
        services.AddScoped<RefreshUserHandler>();
        services.AddScoped<LogoutUserHandler>();
        services.AddScoped<GetCurrentUserHandler>();
        services.AddScoped<IValidator<SearchGamesQuery>, SearchGamesValidator>();
        services.AddScoped<SearchGamesHandler>();

        return services;
    }
}
