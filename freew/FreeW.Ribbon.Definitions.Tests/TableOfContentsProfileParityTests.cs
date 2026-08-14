using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class TableOfContentsProfileParityTests
{
    [Fact]
    public void BothProfilesExposeTheSameAddTextDropdownCommands()
    {
        var wpf = AddTextDropdown(FreeWRibbonCapabilities.Wpf, "table-of-contents");
        var avalonia = AddTextDropdown(FreeWRibbonCapabilities.Avalonia, "toc");

        CommandItems(avalonia).Should().Equal(CommandItems(wpf));
        CommandItems(avalonia).Should().Equal(
            ("freew.toc-addtext-none", "Do Not Show in Table of Contents"),
            ("freew.toc-addtext-level1", "Level 1"),
            ("freew.toc-addtext-level2", "Level 2"),
            ("freew.toc-addtext-level3", "Level 3"));
    }

    private static RibbonDropdown AddTextDropdown(
        FreeWRibbonCapabilities capabilities,
        string groupId)
    {
        var definition = FreeWRibbon.Build(capabilities);
        return definition.FindTab("references")!
            .FindGroup(groupId)!
            .Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.toc-add-text");
    }

    private static IReadOnlyList<(string CommandId, string Label)> CommandItems(RibbonDropdown dropdown) =>
        dropdown.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value.Value, item.Header))
            .ToArray();
}
