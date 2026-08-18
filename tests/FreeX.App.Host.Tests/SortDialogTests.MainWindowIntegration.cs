using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void MainWindowCustomSort_UsesHeaderAwareChoicesAndExcludesHeaderRowWhenChecked()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: true, SortDialog.PlannerText)");
        source.Should().Contain("SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: false, SortDialog.PlannerText)");
        source.Should().Contain("SortDialogPlanner.BuildRowChoices(range, SortDialog.PlannerText)");
        source.Should().Contain("SortDialogPlanner.BuildColorChoices(_workbook, sheet, range)");
        source.Should().Contain("SortDialogPlanner.CreateCommandPlan(");
        // R142-services-sort-customdialog-1: the dialog's column choices (asserted above) are now
        // built from the range ResolveSortRangeAfterAdjacentDataPrompt resolved -- see that
        // resolution feeding execution via the two-arg overload, not a bare re-read of SelectedRange.
        source.Should().Contain("_session.SortSelectedRange(sortPlan, range)");
        source.Should().Contain("_session.ResolveSortRangeAfterAdjacentDataPrompt(");
    }

    [Fact]
    public void MainWindowCustomSort_ThreadsFirstKeySortOrderIntoFirstSortKey()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        // The custom-list "First key sort order" chosen in Sort Options must reach the
        // command. It is applied to the first (primary) sort key, matching Excel.
        source.Should().Contain("SortDialogPlanner.CreateCommandPlan(");
        source.Should().NotContain("CustomSortOrder.TryParse(");
        source.Should().NotContain("ApplyCustomOrderToFirstKey(");
    }

    // R127-commands-sort-multiarea-1: SortCustomButton_Click must refuse a Ctrl+click multi-area
    // selection BEFORE ever constructing/showing the modal SortDialog -- not merely before building
    // the SortCommand -- otherwise a real multi-area selection would still pop the dialog and let
    // the user "successfully" custom-sort just the active area while every other area silently sat
    // untouched. Verified via source order (rather than driving the modal dialog directly, which
    // would block indefinitely with no user present to dismiss it) that the shared selection-policy
    // adapter is called strictly before `new SortDialog(`.
    [Fact]
    public void MainWindowCustomSort_RejectsMultiAreaSelectionBeforeOpeningTheDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("if (TryRejectInvalidSortSelection()) return;");

        var rejectIndex = source.IndexOf("if (TryRejectInvalidSortSelection()) return;", StringComparison.Ordinal);
        var dialogConstructIndex = source.IndexOf("new SortDialog(", StringComparison.Ordinal);
        rejectIndex.Should().BeGreaterThan(-1);
        dialogConstructIndex.Should().BeGreaterThan(-1);
        rejectIndex.Should().BeLessThan(dialogConstructIndex,
            "the multi-area refusal must happen before the modal Sort dialog is ever constructed/shown");
    }
}
