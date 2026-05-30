using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InsertCommandSourceTests
{
    [Theory]
    [InlineData("PivotTable", "PivotTable", "PT", "PivotTableBtn_Click")]
    [InlineData("Recommended PivotTables", "Recommended", "RP", "RecommendedPivotTablesMenuItem_Click")]
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
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

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
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title, handler);

        button.ShouldContainLocalizedAttribute("Content", content);
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Equation", "EQ")]
    public void InsertDeferredTextAndSymbolCommands_RemainDisabledWithoutClickHandlers(string title, string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain("IsEnabled=\"False\"");
        button.ShouldContainInvariantCommandName(title);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Theory]
    [InlineData("Get Add-ins")]
    [InlineData("My Add-ins")]
    [InlineData("3D Map")]
    [InlineData("Object")]
    public void InsertOutOfScopeCommands_AreNotSurfacedAsDisabledRibbonButtons(string title)
    {
        ReadMainWindowXaml().Should().NotContain($"local:RibbonMetadata.CommandName=\"{LocalizedXamlTestSupport.EscapeAttribute(title)}\"");
    }

    [Fact]
    public void InsertShapesButton_ExposesExpectedShapeMenuRoutes()
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), "Shapes");

        var rectangle = ExtractMenuItemElementByClickHandler(button, "DrawRectBtn_Click");
        rectangle.ShouldContainLocalizedAttribute("Header", "Rectangle");
        rectangle.Should().Contain("local:RibbonTooltip.KeyTip=\"R\"");

        var ellipse = ExtractMenuItemElementByClickHandler(button, "DrawEllipseBtn_Click");
        ellipse.ShouldContainLocalizedAttribute("Header", "Ellipse");
        ellipse.Should().Contain("local:RibbonTooltip.KeyTip=\"E\"");

        var line = ExtractMenuItemElementByClickHandler(button, "DrawLineBtn_Click");
        line.ShouldContainLocalizedAttribute("Header", "Line");
        line.Should().Contain("local:RibbonTooltip.KeyTip=\"L\"");
    }

    [Fact]
    public void InsertTablesIllustrationsSparklineAndFilterHandlers_RouteThroughExpectedCommandsAndDialogs()
    {
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));
        var pivotSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));

        insertSource.Should().Contain("private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);");
        insertSource.Should().Contain("private void RecommendedPivotTablesMenuItem_Click(object sender, RoutedEventArgs e) => PivotTableBtn_Click(sender, e);");
        insertSource.Should().Contain("private void SparklineLineBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"line\");");
        insertSource.Should().Contain("private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"column\");");
        insertSource.Should().Contain("private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline(\"winloss\");");
        insertSource.Should().Contain("new SparklineDialog(");
        insertSource.Should().Contain("new AddSparklineCommand(_currentSheetId, dataRange, currentRange.Start, kind)");

        drawingSource.Should().Contain("private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("new InsertPictureCommand(");
        drawingSource.Should().Contain("DrawRectBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Rectangle)");
        drawingSource.Should().Contain("DrawEllipseBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Ellipse)");
        drawingSource.Should().Contain("DrawLineBtn_Click(object sender, RoutedEventArgs e)");
        drawingSource.Should().Contain("InsertDrawingShape(DrawingShapeKind.Line)");

        pivotSource.Should().Contain("private void PivotTableBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("new PivotTableDialog(");
        pivotSource.Should().Contain("new AddPivotTableCommand(");
        pivotSource.Should().Contain("new AddPivotTableToNewWorksheetCommand(");
        pivotSource.Should().Contain("private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("new InsertSlicerDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName)");
        pivotSource.Should().Contain("private void PivotInsertTimelineBtn_Click(object sender, RoutedEventArgs e)");
        pivotSource.Should().Contain("new InsertTimelineDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName)");
    }

    [Fact]
    public void InsertHandlers_RouteThroughExpectedDialogsCommandsAndReviewDelegate()
    {
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));
        var drawingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Drawing.cs"));

        insertSource.Should().Contain("HyperlinkDialogPrefill.FromCell(");
        insertSource.Should().Contain("new HyperlinkDialog(prefill.Target, prefill.DisplayText)");
        insertSource.Should().Contain("new SetHyperlinkCommand(");
        insertSource.Should().Contain("new HyperlinkMetadata(");
        insertSource.Should().Contain("ToCoreHyperlinkTargetKind(dialog.Result.LinkType)");
        insertSource.Should().Contain("private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);");
        insertSource.Should().Contain("new HeaderFooterDialog(sheet)");
        insertSource.Should().Contain("new SetHeaderFooterCommand(");
        insertSource.Should().Contain("new SymbolPickerDialog");
        insertSource.Should().Contain("CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)))");

        drawingSource.Should().Contain("private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();");
        drawingSource.Should().Contain("new TextEntryDialog(\"Insert Text Box\", \"Text:\", \"\")");
        drawingSource.Should().Contain("new AddTextBoxCommand(");
    }

    private static string ReadMainWindowXaml() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

    private static string ExtractButtonElementByTitle(string xaml, string title, string? handler = null) =>
        xaml.ExtractElementByInvariantCommandName(
            "Button",
            title,
            handler is null ? null : $"Click=\"{handler}\"");

    private static string ExtractMenuItemElementByClickHandler(string xaml, string clickHandler)
    {
        var searchIndex = 0;
        while (true)
        {
            var handlerIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", searchIndex, StringComparison.Ordinal);
            handlerIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should be present");

            var start = xaml.LastIndexOf("<MenuItem", handlerIndex, StringComparison.Ordinal);
            if (start >= 0)
            {
                var startTagEnd = xaml.IndexOf(">", start, StringComparison.Ordinal);
                if (startTagEnd > handlerIndex)
                {
                    var end = xaml.IndexOf("/>", handlerIndex, StringComparison.Ordinal);
                    end.Should().BeGreaterThan(handlerIndex);
                    return xaml[start..(end + 2)];
                }
            }

            searchIndex = handlerIndex + clickHandler.Length;
        }
    }
}
