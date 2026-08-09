using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void MainWindowCustomSort_UsesHeaderAwareChoicesAndExcludesHeaderRowWhenChecked()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true)");
        source.Should().Contain("SortDialog.BuildColumnChoices(sheet, range, hasHeaders: false)");
        source.Should().Contain("SortDialog.BuildRowChoices(range)");
        source.Should().Contain("SortDialog.BuildColorChoices(_workbook, sheet, range)");
        source.Should().Contain("SortDialog.ExcludeHeaderRow(currentRange, dialog.ResultHasHeaders)");
        source.Should().Contain("new SortOptions(dialog.ResultOptions.CaseSensitive, dialog.ResultOptions.LeftToRight)");
    }

    [Fact]
    public void MainWindowCustomSort_ThreadsFirstKeySortOrderIntoFirstSortKey()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        // The custom-list "First key sort order" chosen in Sort Options must reach the
        // command. It is applied to the first (primary) sort key, matching Excel.
        source.Should().Contain("CustomSortOrder.TryParse(dialog.ResultOptions.FirstKeySortOrder, out var customOrder)");
        source.Should().Contain("SortDialog.ApplyCustomOrderToFirstKey(keys, customOrder)");
    }

    // R127-commands-sort-multiarea-1: SortCustomButton_Click must refuse a Ctrl+click multi-area
    // selection BEFORE ever constructing/showing the modal SortDialog -- not merely before building
    // the SortCommand -- otherwise a real multi-area selection would still pop the dialog and let
    // the user "successfully" custom-sort just the active area while every other area silently sat
    // untouched. Verified via source order (rather than driving the modal dialog directly, which
    // would block indefinitely with no user present to dismiss it) that TryRejectMultiAreaSort is
    // called, and called strictly before `new SortDialog(`.
    [Fact]
    public void MainWindowCustomSort_RejectsMultiAreaSelectionBeforeOpeningTheDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("if (TryRejectMultiAreaSort(range)) return;");

        var rejectIndex = source.IndexOf("if (TryRejectMultiAreaSort(range)) return;", StringComparison.Ordinal);
        var dialogConstructIndex = source.IndexOf("new SortDialog(", StringComparison.Ordinal);
        rejectIndex.Should().BeGreaterThan(-1);
        dialogConstructIndex.Should().BeGreaterThan(-1);
        rejectIndex.Should().BeLessThan(dialogConstructIndex,
            "the multi-area refusal must happen before the modal Sort dialog is ever constructed/shown");
    }
}
