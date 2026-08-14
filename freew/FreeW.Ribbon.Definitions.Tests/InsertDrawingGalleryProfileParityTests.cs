using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class InsertDrawingGalleryProfileParityTests
{
    [Fact]
    public void BothProfilesExposeTheSameShapeAndTextBoxGalleryCommands()
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).FindTab("insert")!;
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).FindTab("insert")!;

        CommandItems(Dropdown(wpf, "illustrations", "freew.shapes"))
            .Should().Equal(CommandItems(Dropdown(avalonia, "illustrations", "freew.shapes")));
        CommandItems(Dropdown(avalonia, "illustrations", "freew.shapes"))
            .Should().Equal(
                ("freew.shape-rectangle", "Rectangle"),
                ("freew.shape-rounded", "Rounded Rectangle"),
                ("freew.shape-ellipse", "Ellipse"),
                ("freew.shape-textbox", "Text Box"));

        CommandItems(Dropdown(wpf, "text", "freew.shape-textbox"))
            .Should().Equal(CommandItems(Dropdown(avalonia, "text", "freew.shape-textbox")));
        CommandItems(Dropdown(avalonia, "text", "freew.shape-textbox"))
            .Should().Equal(
                ("freew.textbox-simple", "Simple Text Box"),
                ("freew.textbox-sidebar", "Sidebar (Banded)"),
                ("freew.textbox-quote", "Quote"));
    }

    private static RibbonDropdown Dropdown(
        RibbonTab tab,
        string groupId,
        string commandId) =>
        tab.FindGroup(groupId)!
            .Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == commandId);

    private static IReadOnlyList<(string CommandId, string Label)> CommandItems(RibbonDropdown dropdown) =>
        dropdown.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value.Value, item.Header))
            .ToArray();
}
