using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

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
            source.Should().Contain("FreeWDialogPopulationKind.FootnoteEndnoteOptions");
            source.Should().Contain("textBoxes[0].Text = \"not-a-number\"");
            source.Should().Contain("ValidateForTest");
            source.Should().Contain("pair.index switch");
        }
    }

    [StaFact]
    public void Editor_boundary_applies_all_numbering_options()
    {
        var view = new DocumentView();
        var result = new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman,
            4,
            NoteNumberRestart.EachPage,
            NoteNumberFormat.LowerLetter,
            9,
            NoteNumberRestart.EachSection);

        view.ApplyFootnoteEndnoteOptions(result);

        view.Model.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperRoman);
        view.Model.FootnoteNumbering.StartAt.Should().Be(4);
        view.Model.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachPage);
        view.Model.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerLetter);
        view.Model.EndnoteNumbering.StartAt.Should().Be(9);
        view.Model.EndnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);

        view.Undo();
        view.Model.FootnoteNumbering.StartAt.Should().Be(1);
        view.Model.EndnoteNumbering.StartAt.Should().Be(1);
    }

    [Fact]
    public void Ribbon_dispatches_through_commit_plan_and_editor_boundary()
    {
        var source = ReadWorkspaceSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        source.Should().Contain("FootnoteEndnoteOptionsDialogPlanner.PlanCommit(");
        source.Should().Contain("editor.ApplyFootnoteEndnoteOptions(commit.Result!)");
        source.Should().NotContain("model.FootnoteNumbering.NumberFormat =");
        source.Should().NotContain("model.EndnoteNumbering.NumberFormat =");
    }

    private static string ReadWorkspaceSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
