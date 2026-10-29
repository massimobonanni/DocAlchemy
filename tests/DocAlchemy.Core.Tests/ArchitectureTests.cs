using System.Reflection;

namespace DocAlchemy.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceAzureOrAspNet()
    {
        var references = Assembly.Load("DocAlchemy.Core").GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is not null
            && (reference.Name.StartsWith("Azure.", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)));
    }
}
