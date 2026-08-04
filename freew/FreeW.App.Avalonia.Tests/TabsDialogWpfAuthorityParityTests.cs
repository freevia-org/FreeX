using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class TabsDialogWpfAuthorityParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Dialog_matches_WPF_geometry_labels_metrics_and_action_order()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new TabsDialog(
                [
                    new TabStop(144, TabStopAlignment.Right, TabLeader.Dots),
                    new TabStop(72, TabStopAlignment.Left),
                ],
                defaultTabStopPt: 36);

            dialog.Title.Should().Be("Tabs");
            dialog.Width.Should().Be(340);
            dialog.SizeToContent.Should().Be(SizeToContent.Height);

            var grid = dialog.Content.Should().BeOfType<Grid>().Subject;
            grid.Margin.Should().Be(new Thickness(14));
            grid.ColumnDefinitions.Select(column => column.Width).Should().Equal(
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star));
            grid.RowDefinitions.Should().HaveCount(7);
            grid.RowDefinitions.Should().OnlyContain(row => row.Height == GridLength.Auto);

            grid.Children.OfType<TextBlock>().Select(label => label.Text).Should().Equal(
                "Tab stop position (pt):",
                "Stops:",
                "Alignment:",
                "Leader:",
                "Default tab stops (pt):");
            dialog.StopsForTest.Height.Should().Be(120);
            dialog.StopsForTest.MinWidth.Should().Be(150);
            dialog.PositionBoxForTest.MinWidth.Should().Be(120);
            dialog.DefaultTabStopBoxForTest.MinWidth.Should().Be(120);
            dialog.AlignmentBoxForTest.MinWidth.Should().Be(120);
            dialog.LeaderBoxForTest.MinWidth.Should().Be(120);

            dialog.StopsForTest.Items.Cast<string>().Should().Equal(
                "72 pt  Left",
                "144 pt  Right  Dots");
            dialog.DefaultTabStopBoxForTest.Text.Should().Be("36");

            var actionButtons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            actionButtons
                .Select(button => AvaloniaActionLabelInspector.Inspect(button).DisplayText)
                .Should().Equal("Set", "Clear", "Clear All", "OK", "Cancel");
            actionButtons
                .Should().ContainSingle(button => button.IsDefault && AutomationProperties.GetName(button) == "OK")
                .And.ContainSingle(button => button.IsCancel && AutomationProperties.GetName(button) == "Cancel");
            actionButtons.Skip(3).Select(AutomationProperties.GetName).Should().Equal("OK", "Cancel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Dialog_selection_projects_WPF_stop_editor_values_and_open_focus_targets_position()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new TabsDialog(
                [new TabStop(108.25, TabStopAlignment.Decimal, TabLeader.Underline)],
                defaultTabStopPt: 42.5);

            dialog.StopsForTest.SelectedIndex = 0;
            dialog.PositionBoxForTest.Text.Should().Be("108.25");
            dialog.AlignmentBoxForTest.SelectedIndex.Should().Be(3);
            dialog.LeaderBoxForTest.SelectedIndex.Should().Be(3);

            try
            {
                dialog.Show();
                dialog.Measure(new Size(340, 600));
                dialog.Arrange(new Rect(0, 0, 340, 600));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                dialog.PositionBoxForTest.Height.Should().Be(26);
                dialog.DefaultTabStopBoxForTest.Height.Should().Be(26);
                dialog.PositionBoxForTest.IsFocused.Should().BeTrue();
                dialog.PositionBoxForTest.SelectionStart.Should().Be(0);
                dialog.PositionBoxForTest.SelectionEnd.Should().Be(dialog.PositionBoxForTest.Text?.Length ?? 0);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Source_keeps_tabs_dialog_on_shared_chrome_and_WPF_validation_surface()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "ParagraphCommandDialogs.cs"));
        var start = source.IndexOf("public sealed class TabsDialog", StringComparison.Ordinal);
        var end = source.IndexOf("public sealed class BordersAndShadingDialog", start, StringComparison.Ordinal);
        var dialogSource = source[start..end];

        dialogSource.Should().Contain("AvaloniaDialogButtonRowFactory.CreateOkCancel(");
        dialogSource.Should().Contain("AvaloniaDialogButtonRowFactory.CreateRow(");
        dialogSource.Should().Contain("AvaloniaCompactDialogChrome.FocusAndSelect(_position)");
        dialogSource.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        dialogSource.Should().Contain("Width = 340");
        dialogSource.Should().Contain("new Thickness(14)");
        dialogSource.Should().NotContain("_status");
        dialogSource.Should().NotContain("private static StackPanel ButtonRow");
    }
}
