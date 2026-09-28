using Microsoft.AspNetCore.Mvc.Testing;

namespace Ludaryx.IntegrationTests;

public class ApiHostTests
{
    [Fact]
    public void Api_host_can_be_created_successfully()
    {
        using var factory = new WebApplicationFactory<Program>();

        Assert.NotNull(factory.Services);
    }
}
