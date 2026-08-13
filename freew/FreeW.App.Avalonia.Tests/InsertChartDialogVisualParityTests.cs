using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class InsertChartDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_uses_the_Wpf_insert_chart_geometry_and_visible_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            dialog.Width.Should().Be(500);
            dialog.MinHeight.Should().Be(380);

            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
            buttons.Single(button => button.IsDefault).Content.Should().Be("OK");
            buttons.Single(button => button.IsCancel).Content.Should().Be("Cancel");

            dialog.GetLogicalDescendants().OfType<ComboBox>().Should().ContainSingle();
            Field<TextBox>(dialog, "_title").Text.Should().Be("Quarterly Sales");
            ((System.Collections.ICollection)Field<object>(dialog, "_rows")).Count.Should().BeGreaterThan(0);
            Field<StackPanel>(dialog, "_rowsPanel").Children
                .OfType<Grid>()
                .Skip(1)
                .Should()
                .AllSatisfy(row => row.ContextMenu.Should().NotBeNull());
        }, CancellationToken.None);
    }

    [Fact]
    public void Visual_harness_keeps_insert_chart_in_the_shared_target_only_route()
    {
        var workspace = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            workspace, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs"));

        source.Should().Contain("Pair(\"insert-chart\", \"InsertChartDialog\")");
    }

    private static InsertChartDialog CreateDialog()
    {
        var constructor = typeof(InsertChartDialog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(FreeW.Core.Model.Chart)],
            modifiers: null);
        return (InsertChartDialog)constructor!.Invoke([null]);
    }

    private static T Field<T>(InsertChartDialog dialog, string name) where T : class =>
        (T)(typeof(InsertChartDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing InsertChartDialog field {name}."));
}
