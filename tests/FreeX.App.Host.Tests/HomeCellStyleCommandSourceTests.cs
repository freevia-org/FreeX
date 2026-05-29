using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeCellStyleCommandSourceTests
{
    [Fact]
    public void CellStylesRibbonButton_ExposesMenuWithExpectedKeyTip()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, "CellStylesBtn_Click");

        button.Should().Contain("local:RibbonTooltip.Title=\"Cell Styles\"");
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"J\"");
        button.Should().Contain("<Button.ContextMenu>");
    }

    [Theory]
    [InlineData("Normal", "NM", "CellStyleNormalMenuItem_Click", "Normal")]
    [InlineData("Good", "GD", "CellStyleGoodMenuItem_Click", "Good")]
    [InlineData("Bad", "BD", "CellStyleBadMenuItem_Click", "Bad")]
    [InlineData("Neutral", "NT", "CellStyleNeutralMenuItem_Click", "Neutral")]
    [InlineData("Input", "IN", "CellStyleInputMenuItem_Click", "Input")]
    [InlineData("Output", "OP", "CellStyleOutputMenuItem_Click", "Output")]
    [InlineData("Calculation", "CA", "CellStyleCalculationMenuItem_Click", "Calculation")]
    [InlineData("Check Cell", "CK", "CellStyleCheckCellMenuItem_Click", "CheckCell")]
    [InlineData("Linked Cell", "LK", "CellStyleLinkedCellMenuItem_Click", "LinkedCell")]
    [InlineData("Explanatory Text", "EX", "CellStyleExplanatoryTextMenuItem_Click", "ExplanatoryText")]
    [InlineData("Heading 1", "H1", "CellStyleH1MenuItem_Click", "Heading1")]
    [InlineData("Heading 2", "H2", "CellStyleH2MenuItem_Click", "Heading2")]
    [InlineData("Note", "NO", "CellStyleNoteMenuItem_Click", "Note")]
    [InlineData("Warning Text", "WT", "CellStyleWarningMenuItem_Click", "WarningText")]
    [InlineData("Total", "TT", "CellStyleTotalMenuItem_Click", "Total")]
    [InlineData("20% - Accent 1", "A1", "CellStyleAccent1_20MenuItem_Click", "Accent1_20")]
    [InlineData("20% - Accent 2", "A2", "CellStyleAccent2_20MenuItem_Click", "Accent2_20")]
    [InlineData("20% - Accent 3", "A3", "CellStyleAccent3_20MenuItem_Click", "Accent3_20")]
    [InlineData("20% - Accent 4", "A4", "CellStyleAccent4_20MenuItem_Click", "Accent4_20")]
    [InlineData("20% - Accent 5", "A5", "CellStyleAccent5_20MenuItem_Click", "Accent5_20")]
    [InlineData("20% - Accent 6", "A6", "CellStyleAccent6_20MenuItem_Click", "Accent6_20")]
    public void CellStyleMenuItems_RouteToPlannerPresets(
        string header,
        string keyTip,
        string handler,
        string preset)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.Should().Contain($"Header=\"{header}\"");
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        source.Should().Contain($"private void {handler}(object sender, RoutedEventArgs e)");
        source.Should().Contain($"=> ApplyCellStylePreset(CellStylePreset.{preset});");
    }

    [Fact]
    public void CellStylePresetApplication_UsesWorkbookThemeAndRepeatableStyleDiff()
    {
        var formattingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var workbookUiStateSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorkbookUiState.cs"));

        formattingSource.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
        formattingSource.Should().Contain("ApplyStyleDiff(CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme))");
        workbookUiStateSource.Should().Contain("TryExecuteRepeatableApplyStyle(diff, \"Apply Style\")");
    }

    private static string ExtractButtonElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should be present");

        var start = xaml.LastIndexOf("<Button", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} button should have a Button start tag");

        var end = xaml.IndexOf("</Button>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} button should have an end tag");
        return xaml.Substring(start, end - start + "</Button>".Length);
    }

    private static string ExtractMenuItemElementByClickHandler(string xaml, string clickHandler)
    {
        var clickIndex = xaml.IndexOf($"Click=\"{clickHandler}\"", StringComparison.Ordinal);
        clickIndex.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should be present");

        var start = xaml.LastIndexOf("<MenuItem", clickIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {clickHandler} menu item should have a start tag");

        var end = xaml.IndexOf("/>", clickIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(clickIndex, $"the {clickHandler} menu item should be self-closing");
        return xaml.Substring(start, end - start + 2);
    }
}
