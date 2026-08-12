namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonProductionOwnershipTests
{
    [Fact]
    public void Production_assembly_excludes_unused_layout_plan_and_test_validator()
    {
        var productionAssembly = typeof(RibbonDefinition).Assembly;

        productionAssembly.GetType("Free.Shared.Ribbon.RibbonLayoutPlan").Should().BeNull();
        productionAssembly.GetType("Free.Shared.Ribbon.RibbonDefinitionValidator").Should().BeNull();
        typeof(RibbonDefinitionValidator).Assembly.FullName.Should().NotBe(productionAssembly.FullName);
    }
}
