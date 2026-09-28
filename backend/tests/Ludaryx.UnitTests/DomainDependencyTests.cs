using System.Reflection;

namespace Ludaryx.UnitTests;

public class DomainDependencyTests
{
    [Fact]
    public void Domain_has_no_application_or_framework_dependencies()
    {
        var references = Assembly.Load("Ludaryx.Domain").GetReferencedAssemblies();

        Assert.All(references, reference =>
            Assert.True(reference.Name == "System.Runtime",
                $"Unexpected Domain dependency: {reference.Name}"));
    }
}
