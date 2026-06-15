namespace FreeX.Ribbon.Tests;

public class RibbonDefinitionTests
{
    [Fact]
    public void FindTab_ReturnsTabById()
    {
        var definition = new RibbonDefinition(new[]
        {
            new RibbonTab("home", "Home", "H", Context: null, new[]
            {
                new RibbonGroup("clipboard", "Clipboard", "C", Priority: 100,
                    new RibbonControl[] { new RibbonButton("paste", "Paste") },
                    RibbonGroupSizing.Default)
            })
        });

        definition.FindTab("home")!.Header.Should().Be("Home");
        definition.VisibleTabs.Should().ContainSingle();
    }

    [Fact]
    public void ContextualTab_IsExcludedFromVisibleTabs()
    {
        var definition = new RibbonDefinition(new[]
        {
            new RibbonTab("home", "Home", "H", Context: null, Array.Empty<RibbonGroup>()),
            new RibbonTab("chart", "Chart", null,
                new RibbonTabContext("chart.selected", "Chart Tools", RibbonContextColor.Green),
                Array.Empty<RibbonGroup>())
        });

        definition.VisibleTabs.Should().ContainSingle(t => t.Id == "home");
        definition.ContextualTabs.Should().ContainSingle(t => t.Id == "chart");
    }
}
