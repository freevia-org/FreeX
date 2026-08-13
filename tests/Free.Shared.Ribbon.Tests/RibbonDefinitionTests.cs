namespace Free.Shared.Ribbon.Tests;

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

    [Fact]
    public void ComboBox_TypedChoicesKeepProtocolValuesSeparateFromLabels_AndLegacyItemsRemainAvailable()
    {
        var combo = new RibbonComboBox("theme", "Theme")
        {
            Choices =
            [
                new RibbonComboBoxChoice("theme.office", "Office"),
                new RibbonComboBoxChoice("theme.slate", "Slate"),
            ],
            Items = ["Legacy"],
        };

        combo.Choices.Should().Equal(
            new RibbonComboBoxChoice("theme.office", "Office"),
            new RibbonComboBoxChoice("theme.slate", "Slate"));
        combo.Items.Should().Equal("Legacy");
    }
}
