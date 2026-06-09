using FluentAssertions;

namespace FreeX.App.Host.Tests;

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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(handler);

        button.ShouldContainInvariantCommandName(title);
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
    [InlineData("Down", "D", "FillDownMenuItem_Click")]
    [InlineData("Right", "R", "FillRightMenuItem_Click")]
    [InlineData("Up", "U", "FillUpMenuItem_Click")]
    [InlineData("Left", "L", "FillLeftMenuItem_Click")]
    [InlineData("Series...", "S", "FillSeriesMenuItem_Click")]
    [InlineData("Flash Fill", "F", "FlashFillMenuItem_Click")]
    [InlineData("Clear All", "A", "ClearAllMenuItem_Click")]
    [InlineData("Clear Formats", "F", "ClearFormatsMenuItem_Click")]
    [InlineData("Clear Contents", "C", "ClearValuesMenuItem_Click")]
    [InlineData("Clear Comments and Notes", "M", "ClearCommentsMenuItem_Click")]
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
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var menuItem = xaml.ExtractMenuItemElementByClickHandler(handler);

        menuItem.ShouldContainLocalizedAttribute("Header", header);
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void EditingCommandHandlers_RouteThroughExpectedPlannersAndDelegates()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        source.Should().Contain("AutoSumFormulaPlanner.BuildFormula(_workbook.GetSheet(_currentSheetId), func, addr)");
        var autoSumButtonHandler = SourceMethodExtractor.ExtractMethodSource(source, "private void AutoSumPickerBtn_Click(");
        autoSumButtonHandler.Should().Contain("InsertAutoSumFormula(\"SUM\");");
        autoSumButtonHandler.Should().NotContain("OpenRibbonContextMenu");
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
        source.Should().Contain("FillSeriesPlanner.BuildSeriesEdits(");
        source.Should().Contain("dialog.Result");
        source.Should().Contain("UiText.Get(\"FillSeriesStep_SelectNumericOrDateStartMessage\")");
        source.Should().Contain("UiText.Get(\"FillSeriesStep_Title\")");
        source.Should().Contain("private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();");
        source.Should().Contain("var command = CreateFlashFillCommand(range.Value, out var hasExamples, out var hasFillTargets);");
        source.Should().Contain("No examples found. Type at least one value in the fill column.");
        source.Should().Contain("Flash Fill found examples, but there are no blank adjacent cells to fill.");
        source.Should().Contain("currentRange => CreateFlashFillCommand(currentRange, out _, out _) ?? new FailedWorkbookCommand(\"Flash Fill could not find blank adjacent cells to fill.\")");
        source.Should().Contain("GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId)");
        source.Should().Contain("new CompositeWorkbookCommand(\"Flash Fill\", commands)");
        source.Should().Contain("FlashFillRangePlanner.Plan(sheet, sheetRange)");
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
        source.Should().Contain("new ClearConditionalFormatsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearDataValidationCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearCommentsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(sheetId, currentRange)");
        source.Should().Contain("RecalculateIfAutomatic(outcome.AffectedCells ?? [])");
        source.Should().Contain("TryExecuteRepeatableCurrentSelectionRangesCommand(");
        source.Should().Contain("new ClearCommentsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(sheetId, currentRange)");
    }
}
