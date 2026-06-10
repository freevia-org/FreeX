using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InsertCommandSourceTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", "PT", "PivotTableBtn_Click")]
    [InlineData("Table", "Table", "TB", "TableBtn_Click")]
    [InlineData("Pictures", "Pictures", "IP", "InsertPictureBtn_Click")]
    [InlineData("Shapes", "Shapes", "SH", "DrawRectBtn_Click")]
    [InlineData("Line Sparkline", "Line", "SL", "SparklineLineBtn_Click")]
    [InlineData("Column Sparkline", "Column", "SK", "SparklineColumnBtn_Click")]
    [InlineData("Win/Loss Sparkline", "Win/Loss", "SW", "SparklineWinLossBtn_Click")]
    [InlineData("Insert Slicer", "Slicer", "SF", "PivotInsertSlicerBtn_Click")]
    [InlineData("Insert Timeline", "Timeline", "IT", "PivotInsertTimelineBtn_Click")]
    public void InsertTablesIllustrationsSparklineAndFilterCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Insert Link", "Link", "K", "InsertLinkBtn_Click")]
    [InlineData("Comment", "Comment", "C2", "InsertCommentBtn_Click")]
    [InlineData("Text Box", "Text Box", "TX", "DrawTextBtn_Click")]
    [InlineData("Header &amp; Footer", "Header &amp; Footer", "HF", "HeaderFooterBtn_Click")]
    [InlineData("Symbol", "Symbol", "SY", "SymbolPickerBtn_Click")]
    public void InsertTextLinkCommentAndSymbolCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName(title, $"Click=\"{handler}\"");

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Recommended PivotTables")]
    [InlineData("Place in Cell")]
    [InlineData("This Device Picture in Cell")]
    [InlineData("Stock Images in Cell")]
    [InlineData("Online Pictures in Cell")]
    [InlineData("Stock Images over Cells")]
    [InlineData("Online Pictures over Cells")]
    [InlineData("Get Add-ins")]
    [InlineData("My Add-ins")]
    [InlineData("3D Map")]
    [InlineData("Equation")]
    [InlineData("Object")]
    public void InsertOutOfScopeCommands_AreNotSurfacedAsDisabledRibbonButtons(string title)
    {
        LocalizedXamlTestSupport.ReadMainWindowXaml()
            .Should()
            .NotContain($"local:RibbonMetadata.CommandName=\"{LocalizedXamlTestSupport.EscapeAttribute(title)}\"");
    }

    [Fact]
    public void InsertShapesButton_ExposesExpectedShapeMenuRoutes()
    {
        var button = LocalizedXamlTestSupport.ReadMainWindowXaml()
            .ExtractButtonElementByInvariantCommandName("Shapes");

        var rectangle = button.ExtractMenuItemElementByClickHandler("DrawRectBtn_Click");
        rectangle.ShouldContainLocalizedAttribute("Header", "Rectangle");
        rectangle.Should().Contain("local:RibbonTooltip.KeyTip=\"R\"");

        var ellipse = button.ExtractMenuItemElementByClickHandler("DrawEllipseBtn_Click");
        ellipse.ShouldContainLocalizedAttribute("Header", "Ellipse");
        ellipse.Should().Contain("local:RibbonTooltip.KeyTip=\"E\"");

        var line = button.ExtractMenuItemElementByClickHandler("DrawLineBtn_Click");
        line.ShouldContainLocalizedAttribute("Header", "Line");
        line.Should().Contain("local:RibbonTooltip.KeyTip=\"L\"");
    }

    [Fact]
    public void InsertTablesIllustrationsSparklineAndFilterHandlers_RouteThroughExpectedCommandsAndDialogs()
    {
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");
        var drawingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Drawing.cs");
        var homeFormattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var pivotSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotCommands.cs");

        insertSource.Should().Contain("private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);");
        insertSource.Should().NotContain("RecommendedPivotTablesMenuItem_Click");
        insertSource.Should().NotContain("RecommendedPivotTablesDialog");
        homeFormattingSource.Should().Contain("CreateTableSourceRangePlanner.PlanSourceRange(sheet, range)");
        insertSource.Should().Contain("private void SparklineLineBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"line\");");
        insertSource.Should().Contain("private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"column\");");
        insertSource.Should().Contain("private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"winloss\");");
        insertSource.Should().Contain("new SparklineDialog(");
        insertSource.Should().Contain("new AddSparklineCommand(_currentSheetId, dataRange, currentRange.Start, kind)");

        drawingSource.Should().Contain("private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertObjectPlacementPlanner.CreateInsertPictureCommand(");
        drawingSource.Should().Contain("DrawRectBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Rectangle)");
        drawingSource.Should().Contain("DrawEllipseBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Ellipse)");
        drawingSource.Should().Contain("DrawLineBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Line)");

        pivotSource.Should().Contain("private void PivotTableBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("PivotTableSourceRangePlanner.CreatePlan(sheet, SheetGrid.SelectedRange)");
        pivotSource.Should().Contain("ShowPivotTableSourceRangeError(sourcePlan.Error)");
        pivotSource.Should().Contain("new PivotTableDialog(");
        pivotSource.Should().Contain("new AddPivotTableCommand(");
        pivotSource.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
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
        insertSource.Should().Contain("new SetHeaderFooterCommand(");
        insertSource.Should().Contain("new SymbolPickerDialog");
        insertSource.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");

        drawingSource.Should().Contain("private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();");
        drawingSource.Should().Contain("new TextEntryDialog(");
        drawingSource.Should().Contain("UiText.Get(\"MainWindowDialog_InsertTextBoxTitle\")");
        drawingSource.Should().Contain("UiText.Get(\"MainWindowDialog_TextEntryLabel\")");
        drawingSource.Should().Contain("new AddTextBoxCommand(");
    }

}
