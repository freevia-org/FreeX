using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeEditingCommandSourceTests
{

    [Fact]
    public void EditingCommandHandlers_RouteThroughExpectedPlannersAndDelegates()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        source.Should().Contain("AutoSumFormulaPlanner.TryCreatePlan(_workbook.GetSheet(_currentSheetId), func, currentRange, out var plan)");
        source.Should().Contain("(plan.Target, Cell.FromFormula(plan.Formula))");
        source.Should().Contain("? outcome.AffectedCells[0]");
        source.Should().NotContain("GetNextAutoSumCell");
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
        source.Should().Contain("using FreeX.App.Presentation.FillSeries;");
        source.Should().Contain("FillSeriesPlanner.BuildSeriesEdits(");
        source.Should().Contain("dialog.Result");
        source.Should().Contain("UiText.Get(\"FillSeriesStep_SelectNumericOrDateStartMessage\")");
        source.Should().Contain("UiText.Get(\"FillSeriesStep_Title\")");
        source.Should().Contain("private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();");
        source.Should().Contain("var command = CreateFlashFillCommand(range.Value, out var hasExamples, out var hasFillTargets);");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_FlashFillNoExamples\")");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_FlashFillNoBlankAdjacentCells\")");
        source.Should().Contain("currentRange => CreateFlashFillCommand(currentRange, out _, out _) ?? new FailedWorkbookCommand(UiText.Get(\"MainWindowMessage_FlashFillNoBlankAdjacentCells\"))");
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
        source.Should().Contain("var goToDefinedNames = GoToDialogPlanner.BuildDefinedNamesForSheet(_workbook, _currentSheetId);");
        source.Should().Contain("new GoToSpecialDialog { Owner = this }");
        source.Should().Contain("new ClearContentsCommand(sheetId, currentRange)");
        source.Should().Contain("CellStyleDiffPlanner.ClearFormatsDiff()");
        source.Should().Contain("new ClearConditionalFormatsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearDataValidationCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearCommentsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(sheetId, currentRange)");
        source.Should().NotContain("RecalculateIfAutomatic(outcome.AffectedCells ?? [])");
        source.Should().Contain("TryExecuteRepeatableCurrentSelectionRangesCommand(");
        source.Should().Contain("new ClearCommentsCommand(sheetId, currentRange)");
        source.Should().Contain("new ClearHyperlinksCommand(sheetId, currentRange)");
    }

    [Fact]
    public void FillSeriesCommandHandlers_DoNotUseHostPlannerFacade()
    {
        var hostSourceDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.HomeEditing.cs");

        File.Exists(Path.Combine(hostSourceDirectory, "FillSeriesPlanner.cs")).Should().BeFalse();
    }
}
