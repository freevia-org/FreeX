using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class InsertCommandSourceTests
{

    [Fact]
    public void InsertTablesSparklineAndFilterHandlers_RouteThroughExpectedCommandsAndDialogs()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var homeFormattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        insertSource.Should().Contain("private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);");
        insertSource.Should().NotContain("RecommendedPivotTablesMenuItem_Click");
        insertSource.Should().NotContain("RecommendedPivotTablesDialog");
        homeFormattingSource.Should().Contain("TableCreationPlanner.PlanSourceRange(sheet, range)");
        insertSource.Should().Contain("private void SparklineLineBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"line\");");
        insertSource.Should().Contain("private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"column\");");
        insertSource.Should().Contain("private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"winloss\");");
        insertSource.Should().Contain("new SparklineDialog(");
        insertSource.Should().Contain("SparklinePlanner.ValidateInsertGroup(");
        insertSource.Should().Contain("new AddSparklineCommand(_currentSheetId, members[0].DataRange, currentRange.Start, kind)");
        insertSource.Should().Contain("new CompositeWorkbookCommand(\"Insert Sparkline\", commands)");

        pivotSource.Should().Contain("private void PivotTableBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("PivotCreatePlanner.CreateSourceRangePlan(sheet, SheetGrid.SelectedRange)");
        pivotSource.Should().Contain("ShowPivotTableSourceRangeError(sourcePlan.Error)");
        pivotSource.Should().Contain("new PivotTableDialog(");
        pivotSource.Should().Contain("PivotCreatePlanner.CreateDefaultLayout(sourceSheet, dialogSourceRange)");
        pivotSource.Should().Contain("PivotCreatePlanner.SuggestName(_workbook)");
        pivotSource.Should().Contain("PivotCreatePlanner.BuildInPlaceCommand(");
        pivotSource.Should().Contain("PivotCreatePlanner.BuildNewWorksheetCommand(");
        pivotSource.Should().Contain("ActivateNewWorksheetAtA1(createdSheetId)");
        pivotSource.Should().Contain("private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("new InsertSlicerDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName)");
        pivotSource.Should().Contain("private void PivotInsertTimelineBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("new InsertTimelineDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName)");
    }

    [Fact]
    public void InsertTablesChartsScreenshotTour_StaysBoundedToVisualEvidenceSlice()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        var tourStart = source.IndexOf("private async Task CaptureInsertTablesChartsTourAsync", StringComparison.Ordinal);
        var tourEnd = source.IndexOf("private async Task CaptureKeyTipOverlayTourAsync", tourStart, StringComparison.Ordinal);

        tourStart.Should().BeGreaterThanOrEqualTo(0);
        tourEnd.Should().BeGreaterThan(tourStart);
        var tourSource = source[tourStart..tourEnd];

        source.Should().Contain("FREEX_INSERT_TABLES_CHARTS_TOUR");
        tourSource.Should().Contain("CreateTableDialog");
        tourSource.Should().Contain("InsertChartDialog");
        tourSource.Should().Contain("SparklineDialog");
        tourSource.Should().Contain("CreateStyledStructuredTableCommand");
        tourSource.Should().Contain("AddChartCommand");
        tourSource.Should().Contain("AddSparklineCommand");
        tourSource.Should().NotContain("InsertPicture");
        tourSource.Should().NotContain("Hyperlink");
        tourSource.Should().NotContain("SymbolPicker");
        tourSource.Should().NotContain("TextBox");
    }

    [Fact]
    public void InsertHandlers_RouteThroughExpectedDialogsCommandsAndReviewDelegate()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");

        insertSource.Should().Contain("HyperlinkDialogPrefill.FromCell(");
        insertSource.Should().Contain("new HyperlinkDialog(prefill.Target, prefill.DisplayText)");
        insertSource.Should().Contain("TryExecuteRepeatableGroupedSheetCommand(");
        insertSource.Should().Contain("var address = GroupedSheetRangePlanner.RemapRangeToSheet(currentRange, sheetId).Start;");
        insertSource.Should().Contain("new SetHyperlinkCommand(");
        insertSource.Should().Contain("new HyperlinkMetadata(");
        insertSource.Should().Contain("ToCoreHyperlinkTargetKind(dialog.Result.LinkType)");
        insertSource.Should().Contain("private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);");
        insertSource.Should().Contain("new HeaderFooterDialog(sheet)");
        insertSource.Should().Contain("PageSetupCommandFactory.BuildHeaderFooterCommand(");
        insertSource.Should().Contain("new PageSetupHeaderFooterRequest");
        insertSource.Should().NotContain("new SetHeaderFooterCommand(");
        insertSource.Should().Contain("new SymbolPickerDialog");
        insertSource.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");

        drawingSource.Should().Contain("private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();");
        drawingSource.Should().NotContain("new TextEntryDialog(");
        drawingSource.Should().NotContain("UiText.Get(\"MainWindowDialog_InsertTextBoxTitle\")");
        drawingSource.Should().NotContain("UiText.Get(\"MainWindowDialog_TextEntryLabel\")");
        drawingSource.Should().Contain("DrawingInsertionPlanner.BuildInlineEditTextBoxCommand(");
        drawingSource.Should().Contain("BeginTextBoxInlineEdit(currentSheetCommand.TextBoxId)");
    }

}
