using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HomeEditingCommandSourceTests
{
    [Theory]
    [InlineData("AutoSum", "U", "AutoSumPickerBtn_Click")]
    [InlineData("Fill", "FI", "FillPickerBtn_Click")]
    [InlineData("Clear", "E", "ClearPickerBtn_Click")]
    [InlineData("Sort &amp; Filter", "S", "SortFilterPickerBtn_Click")]
    [InlineData("Find &amp; Select", "FD", "FindSelectPickerBtn_Click")]
    public void EditingCommandButtons_ExposeExpectedKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, handler);

        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Sum", "S", "AutoSumSumMenuItem_Click")]
    [InlineData("Average", "A", "AutoSumAvgMenuItem_Click")]
    [InlineData("Count Numbers", "C", "AutoSumCountMenuItem_Click")]
    [InlineData("Count All", "T", "AutoSumCountAllMenuItem_Click")]
    [InlineData("Max", "X", "AutoSumMaxMenuItem_Click")]
    [InlineData("Min", "M", "AutoSumMinMenuItem_Click")]
    [InlineData("More Functions...", "F", "AutoSumMoreMenuItem_Click")]
    [InlineData("Fill Down", "D", "FillDownMenuItem_Click")]
    [InlineData("Fill Right", "R", "FillRightMenuItem_Click")]
    [InlineData("Fill Up", "U", "FillUpMenuItem_Click")]
    [InlineData("Fill Left", "L", "FillLeftMenuItem_Click")]
    [InlineData("Series...", "S", "FillSeriesMenuItem_Click")]
    [InlineData("Flash Fill", "F", "FlashFillMenuItem_Click")]
    [InlineData("Clear All", "A", "ClearAllMenuItem_Click")]
    [InlineData("Clear Formats", "F", "ClearFormatsMenuItem_Click")]
    [InlineData("Clear Contents", "C", "ClearValuesMenuItem_Click")]
    [InlineData("Clear Comments", "M", "ClearCommentsMenuItem_Click")]
    [InlineData("Clear Hyperlinks", "H", "ClearHyperlinksMenuItem_Click")]
    [InlineData("Sort A to Z", "A", "SortAZMenuItem_Click")]
    [InlineData("Sort Z to A", "Z", "SortZAMenuItem_Click")]
    [InlineData("Custom Sort...", "S", "SortCustomMenuItem_Click")]
    [InlineData("Filter", "F", "FilterToggleMenuItem_Click")]
    [InlineData("Clear", "C", "FilterClearMenuItem_Click")]
    [InlineData("Reapply", "R", "FilterReapplyMenuItem_Click")]
    [InlineData("Find...", "F", "FindFindMenuItem_Click")]
    [InlineData("Replace...", "R", "FindReplaceMenuItem_Click")]
    [InlineData("Go To...", "G", "FindGoToMenuItem_Click")]
    [InlineData("Go To Special...", "S", "FindGoToSpecialMenuItem_Click")]
    public void EditingMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.Should().Contain($"Header=\"{header}\"");
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void EditingCommandHandlers_RouteThroughExpectedPlannersAndDelegates()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeEditing.cs"));

        source.Should().Contain("AutoSumFormulaPlanner.BuildFormula(_workbook.GetSheet(_currentSheetId), func, addr)");
        source.Should().Contain("private void AutoSumSumMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula(\"SUM\");");
        source.Should().Contain("private void AutoSumAvgMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula(\"AVERAGE\");");
        source.Should().Contain("private void AutoSumCountMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula(\"COUNT\");");
        source.Should().Contain("private void AutoSumCountAllMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula(\"COUNTA\");");
        source.Should().Contain("private void AutoSumMaxMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula(\"MAX\");");
        source.Should().Contain("private void AutoSumMinMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula(\"MIN\");");
        source.Should().Contain("private void AutoSumMoreMenuItem_Click(object sender, RoutedEventArgs e)  => InsertFunctionBtn_Click(sender, e);");
        source.Should().Contain("=> ExecuteFillCells(FillCellsDirection.Down)");
        source.Should().Contain("=> ExecuteFillCells(FillCellsDirection.Right)");
        source.Should().Contain("=> ExecuteFillCells(FillCellsDirection.Up)");
        source.Should().Contain("=> ExecuteFillCells(FillCellsDirection.Left)");
        source.Should().Contain("FillSeriesPlanner.BuildLinearSeriesEdits(currentSheet, currentRange, dialog.Result.Step)");
        source.Should().Contain("private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();");
        source.Should().Contain("currentRange => CreateFlashFillCommand(sheet, currentRange)");
        source.Should().Contain("private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);");
        source.Should().Contain("private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);");
        source.Should().Contain("private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortCustomButton_Click(sender, e);");
        source.Should().Contain("private void FilterToggleMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);");
        source.Should().Contain("private void FilterClearMenuItem_Click(object sender, RoutedEventArgs e)  => ClearFilterButton_Click(sender, e);");
        source.Should().Contain("private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();");
        source.Should().Contain("private void FindFindMenuItem_Click(object sender, RoutedEventArgs e)       => FindButton_Click(sender, e);");
        source.Should().Contain("private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)    => ReplaceButton_Click(sender, e);");
        source.Should().Contain("new GoToDialog(_currentSheetId, defaultAddress, _workbook.NamedRanges)");
        source.Should().Contain("new GoToSpecialDialog { Owner = this }");
        source.Should().Contain("new ClearContentsCommand(sheetId, currentRange)");
        source.Should().Contain("CellStyleDiffPlanner.ClearFormatsDiff()");
        source.Should().Contain("new ClearCommentsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(sheetId, currentRange)");
        source.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? [])");
        source.Should().Contain("new ClearCommentsCommand(_currentSheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(_currentSheetId, currentRange)");
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</Button>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }

    private static string ExtractMenuItemElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should be present");

        var start = xaml.LastIndexOf("<MenuItem", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should have a start tag");

        var end = xaml.IndexOf("</MenuItem>", clickIndex, StringComparison.Ordinal);
        if (end >= clickIndex)
            return xaml.Substring(start, end - start + "</MenuItem>".Length);

        end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} menu item should have an end tag or be self-closing");
        return xaml.Substring(start, end - start + 2);
    }
}
