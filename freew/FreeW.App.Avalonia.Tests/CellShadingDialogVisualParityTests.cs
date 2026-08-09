using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class CellShadingDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Initial_surface_matches_the_Wpf_palette_geometry_and_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CellShadingDialog();
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            var layout = CellShadingDialogPlanner.Layout;

            buttons.Should().HaveCount(CellShadingDialogPlanner.Palette.Count + 1);
            buttons.Take(CellShadingDialogPlanner.Palette.Count).Should().OnlyContain(button =>
                button.Width == layout.SwatchSize
                && button.Height == layout.SwatchSize
                && button.Margin == new Thickness(layout.SwatchMargin)
                && button.Focusable);
            buttons.Take(CellShadingDialogPlanner.Palette.Count)
                .Select(button => AutomationProperties.GetAutomationId(button))
                .Should().Equal(Enumerable.Range(0, CellShadingDialogPlanner.Palette.Count).Select(index => $"CellShadingSwatch{index}"));

            var clear = buttons[^1];
            AutomationProperties.GetAutomationId(clear).Should().Be("CellShadingNoColorButton");
            clear.Content?.ToString().Should().Be(CellShadingDialogPlanner.NoColorLabel);
            buttons.Should().NotContain(button => button.IsDefault || button.IsCancel);
        }, CancellationToken.None);
    }

    [Fact]
    public void Harness_routes_are_app_owned_and_cell_shading_has_only_a_real_initial_state()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var catalog = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs"));

        catalog.Should().Contain("Pair(\"cell-shading\", \"CellShadingDialog\")");
        catalog.Should().Contain("\"symbol-picker\" or \"cell-shading\" => [\"initial\"]");
    }
}
