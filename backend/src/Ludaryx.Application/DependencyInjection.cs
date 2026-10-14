using FluentValidation;
using Ludaryx.Application.Backlog;
using Ludaryx.Application.Auth.GetCurrentUser;
using Ludaryx.Application.Auth.LoginUser;
using Ludaryx.Application.Auth.LogoutUser;
using Ludaryx.Application.Auth.RefreshUser;
using Ludaryx.Application.Auth.RegisterUser;
using Ludaryx.Application.Games.SearchGames;
using Ludaryx.Application.Games.GetGameDetails;
using Ludaryx.Application.Library.AddGame;
using Ludaryx.Application.Library.ListLibrary;
using Ludaryx.Application.Library.GetLibraryItem;
using Ludaryx.Application.Library.UpdateLibraryItem;
using Ludaryx.Application.Library.DeleteLibraryItem;
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
        services.AddScoped<IValidator<GetGameDetailsQuery>, GetGameDetailsValidator>();
        services.AddScoped<GetGameDetailsHandler>();
        services.AddScoped<IValidator<AddGameCommand>, AddGameValidator>();
        services.AddScoped<AddGameHandler>();
        services.AddScoped<IValidator<ListLibraryQuery>, ListLibraryValidator>();
        services.AddScoped<ListLibraryHandler>();
        services.AddScoped<GetLibraryItemHandler>();
        services.AddScoped<IValidator<UpdateLibraryCommand>, UpdateLibraryValidator>();
        services.AddScoped<UpdateLibraryHandler>();
        services.AddScoped<DeleteLibraryItemHandler>();
        services.AddScoped<GetBacklogHandler>();
        services.AddScoped<IValidator<ReorderBacklogCommand>, ReorderBacklogValidator>();
        services.AddScoped<ReorderBacklogHandler>();

        return services;
    }
}
