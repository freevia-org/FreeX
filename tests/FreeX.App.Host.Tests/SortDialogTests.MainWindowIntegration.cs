using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void MainWindowCustomSort_UsesHeaderAwareChoicesAndExcludesHeaderRowWhenChecked()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

        // The custom-list "First key sort order" chosen in Sort Options must reach the
        // command. It is applied to the first (primary) sort key, matching Excel.
        source.Should().Contain("CustomSortOrder.TryParse(dialog.ResultOptions.FirstKeySortOrder, out var customOrder)");
        source.Should().Contain("SortDialog.ApplyCustomOrderToFirstKey(keys, customOrder)");
    }
}
