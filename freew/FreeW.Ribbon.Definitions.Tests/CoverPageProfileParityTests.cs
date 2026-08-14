using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class CoverPageProfileParityTests
{
    [Fact]
    public void BothProfilesExposeTheSameCanonicalPresetCommands()
    {
        var wpf = CoverPageDropdown(FreeWRibbonCapabilities.Wpf);
        var avalonia = CoverPageDropdown(FreeWRibbonCapabilities.Avalonia);

        CommandItems(avalonia).Should().Equal(CommandItems(wpf));
        CommandItems(avalonia).Should().Equal(
            ("freew.cover-page-default", "Default"),
            ("freew.cover-page-banded", "Banded"),
            ("freew.cover-page-motion", "Motion"));
    }

    private static RibbonDropdown CoverPageDropdown(FreeWRibbonCapabilities capabilities) =>
        FreeWRibbon.Build(capabilities)
            .FindTab("insert")!
            .FindGroup("pages")!
            .Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.cover-page");

    private static IReadOnlyList<(string CommandId, string Label)> CommandItems(RibbonDropdown dropdown) =>
        dropdown.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value.Value, item.Header))
            .ToArray();
}
