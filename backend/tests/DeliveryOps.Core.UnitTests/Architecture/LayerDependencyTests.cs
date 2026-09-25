using System.Reflection;

namespace DeliveryOps.Core.UnitTests.Architecture;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_does_not_depend_on_outer_core_layers() => AssertNoReferences(
        typeof(DeliveryOps.Core.Domain.Entities.Order).Assembly,
        "DeliveryOps.Core.Queries", "DeliveryOps.Core.Handlers", "DeliveryOps.Core.Infrastructure", "DeliveryOps.Core.Api");

    [Fact]
    public void Queries_does_not_depend_on_handlers_or_infrastructure() => AssertNoReferences(
        Assembly.Load("DeliveryOps.Core.Queries"),
        "DeliveryOps.Core.Handlers", "DeliveryOps.Core.Infrastructure", "DeliveryOps.Core.Api");

    [Fact]
    public void Handlers_does_not_depend_on_infrastructure_or_api() => AssertNoReferences(
        Assembly.Load("DeliveryOps.Core.Handlers"),
        "DeliveryOps.Core.Infrastructure", "DeliveryOps.Core.Api");

    [Fact]
    public void Infrastructure_does_not_depend_on_handlers_or_api() => AssertNoReferences(
        Assembly.Load("DeliveryOps.Core.Infrastructure"),
        "DeliveryOps.Core.Handlers", "DeliveryOps.Core.Api");

    private static void AssertNoReferences(Assembly assembly, params string[] forbiddenAssemblies)
    {
        string[] references = assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
        foreach (string forbidden in forbiddenAssemblies)
            Assert.DoesNotContain(forbidden, references);
    }
}
