using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeCellsCommandSourceTests
{
    [Theory]
    [InlineData("Insert", "I", "InsertPickerBtn_Click")]
    [InlineData("Delete", "D", "DeletePickerBtn_Click")]
    [InlineData("Format", "O", "FormatPickerBtn_Click")]
    public void CellsCommandButtons_ExposeExpectedKeyTipsAndHandlers(
        string title,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var button = ExtractButtonElementByClickHandler(xaml, handler);

        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("Insert Cells...", "C", "InsertCellsMenuItem_Click")]
    [InlineData("Insert Sheet Rows", "R", "InsertRowBtn_Click")]
    [InlineData("Insert Sheet Columns", "O", "InsertColBtn_Click")]
    [InlineData("Insert Sheet", "S", "InsertSheetMenuItem_Click")]
    [InlineData("Delete Cells...", "C", "DeleteCellsMenuItem_Click")]
    [InlineData("Delete Sheet Rows", "R", "DeleteRowBtn_Click")]
    [InlineData("Delete Sheet Columns", "O", "DeleteColBtn_Click")]
    [InlineData("Delete Sheet", "S", "DeleteSheetMenuItem_Click")]
    [InlineData("Row Height...", "R", "FormatRowHeightMenuItem_Click")]
    [InlineData("AutoFit Row Height", "A", "FormatAutoRowMenuItem_Click")]
    [InlineData("Column Width...", "C", "FormatColWidthMenuItem_Click")]
    [InlineData("AutoFit Column Width", "W", "FormatAutoColMenuItem_Click")]
    [InlineData("Hide Rows", "H", "FormatHideRowMenuItem_Click")]
    [InlineData("Unhide Rows", "U", "FormatUnhideRowMenuItem_Click")]
    [InlineData("Hide Columns", "D", "FormatHideColMenuItem_Click")]
    [InlineData("Unhide Columns", "N", "FormatUnhideColMenuItem_Click")]
    [InlineData("Rename Sheet", "R", "FormatRenameSheetMenuItem_Click")]
    [InlineData("Hide Sheet", "S", "FormatHideSheetMenuItem_Click")]
    [InlineData("Unhide Sheet...", "T", "FormatUnhideSheetMenuItem_Click")]
    [InlineData("Protect Sheet...", "P", "FormatProtectSheetMenuItem_Click")]
    [InlineData("Lock Cell", "L", "FormatLockCellMenuItem_Click")]
    [InlineData("Format Cells...", "F", "FormatCellsMenuItem_Click")]
    public void CellsMenuItems_ExposeExpectedKeyTipsAndHandlers(
        string header,
        string keyTip,
        string handler)
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var menuItem = ExtractMenuItemElementByClickHandler(xaml, handler);

        menuItem.Should().Contain($"Header=\"{header}\"");
        menuItem.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        menuItem.Should().Contain($"Click=\"{handler}\"");
    }

    [Fact]
    public void CellsCommandHandlers_RouteThroughInsertDeleteDimensionAndFormatCellsCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs"));

        source.Should().Contain("new InsertCellsCommand(_currentSheetId, currentRange, InsertCellsShiftDirection.Down)");
        source.Should().Contain("new InsertCellsCommand(_currentSheetId, currentRange, InsertCellsShiftDirection.Right)");
        source.Should().Contain("new InsertRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.RowCount)");
        source.Should().Contain("new InsertColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.ColCount)");
        source.Should().Contain("private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }");
        source.Should().Contain("new DeleteCellsCommand(_currentSheetId, currentRange, DeleteCellsShiftDirection.Up)");
        source.Should().Contain("new DeleteCellsCommand(_currentSheetId, currentRange, DeleteCellsShiftDirection.Left)");
        source.Should().Contain("new DeleteRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.RowCount)");
        source.Should().Contain("new DeleteColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.ColCount)");
        source.Should().Contain("new RemoveSheetCommand(_currentSheetId)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("RowColumnDimensionPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("private void FormatRenameSheetMenuItem_Click(object sender, RoutedEventArgs e) => RenameCurrentSheet();");
        source.Should().Contain("private void FormatHideSheetMenuItem_Click(object sender, RoutedEventArgs e) => HideCurrentSheet();");
        source.Should().Contain("private void FormatUnhideSheetMenuItem_Click(object sender, RoutedEventArgs e) => UnhideSheet();");
        source.Should().Contain("private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Locked: !style.Locked))");
        source.Should().Contain("private void FormatCellsMenuItem_Click(object sender, RoutedEventArgs e) => OpenFormatCellsDialog();");
        source.Should().Contain("private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)");
        source.Should().Contain("FormatCellsMergePlanner.IsSelectionMerged(sheet, range)");
        source.Should().Contain("FormatCellsMergePlanner.CreateMergeCommands(sheet, sheetId, sheetRange, shouldMerge)");
    }

    [Fact]
    public void SheetVisibilityCommands_ShareSheetTabVisibilityWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.SheetTabs.cs"));

        source.Should().Contain("private void SheetCtxHide_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("HideSheet(tab.Id);");
        source.Should().Contain("private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("UnhideSheet();");
        source.Should().Contain("private void HideCurrentSheet()");
        source.Should().Contain("HideSheet(_currentSheetId);");
        source.Should().Contain("private void HideSheet(SheetId sheetId)");
        source.Should().Contain("new SetSheetHiddenCommand(sheetId, hidden: true)");
        source.Should().Contain("private void UnhideSheet()");
        source.Should().Contain("new UnhideSheetDialog(hiddenSheets.Select(sheet => sheet.Name))");
        source.Should().Contain("new SetSheetHiddenCommand(sheet.Id, hidden: false)");
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
