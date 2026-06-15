namespace FreeX.Ribbon.Tests;

public class RibbonContextResolverTests
{
    private static RibbonDefinition Definition() => new RibbonDefinitionBuilder()
        .Tab("home", "Home", "H", _ => { })
        .ContextualTab("chart", "Chart",
            new RibbonTabContext("chart.selected", "Chart Tools", RibbonContextColor.Green),
            _ => { })
        .Build();

    [Fact]
    public void Hides_ContextualTab_WhenKeyInactive()
    {
        var visible = RibbonContextResolver.Resolve(Definition(), RibbonContextState.None);
        visible.Select(t => t.Id).Should().ContainSingle().Which.Should().Be("home");
    }

    [Fact]
    public void Shows_ContextualTab_WhenKeyActive()
    {
        var state = RibbonContextState.None.With("chart.selected");
        var visible = RibbonContextResolver.Resolve(Definition(), state);
        visible.Select(t => t.Id).Should().Equal("home", "chart");
    }
}
