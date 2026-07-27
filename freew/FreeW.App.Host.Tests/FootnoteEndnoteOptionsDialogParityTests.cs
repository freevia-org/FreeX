using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

public sealed class FootnoteEndnoteOptionsDialogParityTests
{
    [StaFact]
    public void Wpf_dialog_uses_shared_grid_metrics_and_action_contract()
    {
        var dialog = new FootnoteEndnoteOptionsDialog(null, new(), new());
        try
        {
            dialog.Width.Should().Be(FootnoteEndnoteOptionsDialogPlanner.DialogWidth);

            var root = (StackPanel)dialog.Content!;
            var grids = root.Children.OfType<Grid>().ToArray();
            grids.Should().HaveCount(2);
            grids.Should().OnlyContain(grid =>
                grid.ColumnDefinitions.Count == 2
                && grid.RowDefinitions.Count == 3);

            var actionRow = root.Children.OfType<StackPanel>().Single();
            var buttons = actionRow.Children.OfType<Button>().ToArray();
            buttons.Select(button => button.Content?.ToString())
                .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void Harness_populates_and_validates_this_route_in_both_hosts()
    {
        var wpf = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs");
        var avalonia = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("scenario.RouteId == \"footnote-endnote-options\"");
            source.Should().Contain("textBoxes[0].Text = \"not-a-number\"");
            source.Should().Contain("ValidateForTest");
            source.Should().Contain("pair.index switch");
        }
    }

    private static string ReadWorkspaceSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
