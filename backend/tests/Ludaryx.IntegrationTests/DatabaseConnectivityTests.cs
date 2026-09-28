using Ludaryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ludaryx.IntegrationTests;

public class DatabaseConnectivityTests
{
    [Fact]
    public async Task DbContext_can_connect_to_local_PostgreSql()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LudaryxDbContext>();

        Assert.True(await context.Database.CanConnectAsync(),
            "PostgreSQL must be running and ConnectionStrings:LudaryxDatabase configured.");
    }
}
