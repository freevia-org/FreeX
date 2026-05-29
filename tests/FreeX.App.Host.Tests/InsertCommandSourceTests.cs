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
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Link", "Link", "K", "InsertLinkBtn_Click")]
    [InlineData("New Comment", "Comment", "C2", "InsertCommentBtn_Click")]
    [InlineData("Text Box", "Text Box", "TX", "DrawTextBtn_Click")]
    [InlineData("Header &amp; Footer", "Header &amp; Footer", "HF", "HeaderFooterBtn_Click")]
    [InlineData("Symbol", "Symbol", "SY", "SymbolPickerBtn_Click")]
    public void InsertTextLinkCommentAndSymbolCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Get Add-ins", "GA")]
    [InlineData("My Add-ins", "AI")]
    [InlineData("3D Map", "3D")]
    public void InsertDeferredAddInAndTourCommands_RemainDisabledWithoutClickHandlers(string title, string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Theory]
    [InlineData("Object", "O")]
    [InlineData("Equation", "EQ")]
    public void InsertDeferredTextAndSymbolCommands_RemainDisabledWithoutClickHandlers(string title, string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), title);

        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Fact]
    public void InsertShapesButton_ExposesExpectedShapeMenuRoutes()
    {
        var button = ExtractButtonElementByTitle(ReadMainWindowXaml(), "Shapes");

        button.Should().Contain("<MenuItem Header=\"Rectangle\" local:RibbonTooltip.KeyTip=\"R\" Click=\"DrawRectBtn_Click\"/>");
        button.Should().Contain("<MenuItem Header=\"Ellipse\" local:RibbonTooltip.KeyTip=\"E\" Click=\"DrawEllipseBtn_Click\"/>");
        button.Should().Contain("<MenuItem Header=\"Line\" local:RibbonTooltip.KeyTip=\"L\" Click=\"DrawLineBtn_Click\"/>");
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

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} Insert command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} Insert command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var nextButton = xaml.IndexOf("<Button ", titleIndex + 1, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (nextButton < 0 || closingEnd < nextButton)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} Insert button should have a closing marker");
        return xaml[start..end];
    }
}
