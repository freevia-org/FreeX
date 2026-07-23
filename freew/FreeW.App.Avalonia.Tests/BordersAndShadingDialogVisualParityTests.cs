using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class BordersAndShadingDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Uses_the_Wpf_three_tab_geometry_and_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BordersAndShadingDialog(ParagraphFormatting.Default, null);

            dialog.Width.Should().Be(420);
            var tabs = dialog.TabsForTest;
            tabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString())
                .Should().Equal("Borders", "Page Border", "Shading");
            tabs.Items.OfType<TabItem>().Select(item => item.Content).Should()
                .OnlyContain(content => content is Grid);

            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal(LocalizedUiText.Ok, LocalizedUiText.Cancel);
            buttons.Single(button => button.IsDefault).Content.Should().Be(LocalizedUiText.Ok);
            buttons.Single(button => button.IsCancel).Content.Should().Be(LocalizedUiText.Cancel);
        }, CancellationToken.None);
    }

    [Fact]
    public void Visual_harness_keeps_the_combined_route_on_both_hosts()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var avaloniaFactory = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "AvaloniaDialogRouteFactory.cs"));
        var wpfFactory = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Wpf",
            "WpfDialogRouteFactory.cs"));
        var inventoryBuilder = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness",
            "Program.cs"));

        avaloniaFactory.Should().Contain("[\"borders-and-shading\"] = \"BordersAndShadingDialog\"");
        wpfFactory.Should().Contain("[\"borders-and-shading\"] = \"BordersAndShadingDialog\"");
        inventoryBuilder.Should().Contain("var classText = text[match.Index..classEnd]");
    }
}
