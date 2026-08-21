namespace Free.Shared.Ribbon.Tests;

public class RibbonDefinitionBuilderTests
{
    [Fact]
    public void Builds_TabGroupControl_Hierarchy()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab
                .Group("clipboard", "Clipboard", "C", priority: 100, g => g
                    .Button("paste", "Paste", b => b with { KeyTip = "V" })))
            .Build();

        var control = definition.FindTab("home")!.FindGroup("clipboard")!.Controls.Single();
        control.Should().BeOfType<RibbonButton>();
        control.KeyTip.Should().Be("V");
    }

    [Fact]
    public void ContextualTab_CarriesContext()
    {
        var definition = new RibbonDefinitionBuilder()
            .ContextualTab("chart", "Chart",
                new RibbonTabContext("chart.selected", "Chart Tools", RibbonContextColor.Green),
                _ => { })
            .Build();

        var tab = definition.FindTab("chart")!;
        tab.IsContextual.Should().BeTrue();
        tab.Context!.ActivationKey.Should().Be("chart.selected");
    }

    [Fact]
    public void Group_DialogLauncher_CarriesCommandAndTooltipMetadata()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab => tab
                .Group("font", "Font", "F", priority: 100, g => g
                    .DialogLauncher("Format Cells Font", "Font dialog", "Open Format Cells on the Font tab.")
                    .Button("bold", "Bold")))
            .Build();

        definition.FindTab("home")!.FindGroup("font")!.Launcher.Should().Be(
            new RibbonGroupLauncher("Format Cells Font", "Font dialog", "Open Format Cells on the Font tab."));
    }
}
