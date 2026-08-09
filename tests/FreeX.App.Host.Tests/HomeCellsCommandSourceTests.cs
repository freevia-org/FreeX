using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeCellsCommandSourceTests
{

    [Fact]
    public void CellsCommandHandlers_RouteInsertDeleteThroughWorkbookSession()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");

        SourceMethodExtractor.ExtractMethodSource(source, "private void InsertPickerBtn_Click(")
            .Should().Contain("InsertCellsMenuItem_Click(sender, e);");
        SourceMethodExtractor.ExtractMethodSource(source, "private void DeletePickerBtn_Click(")
            .Should().Contain("DeleteCellsMenuItem_Click(sender, e);");
        source.Should().Contain("_session.InsertSelectedCells(InsertCellsShiftDirection.Down)");
        source.Should().Contain("_session.InsertSelectedCells(InsertCellsShiftDirection.Right)");
        source.Should().Contain("_session.InsertSelectedRows()");
        source.Should().Contain("_session.InsertSelectedColumns()");
        source.Should().Contain("private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }");
        source.Should().Contain("_session.DeleteSelectedCells(DeleteCellsShiftDirection.Up)");
        source.Should().Contain("_session.DeleteSelectedCells(DeleteCellsShiftDirection.Left)");
        source.Should().Contain("_session.DeleteSelectedRows()");
        source.Should().Contain("_session.DeleteSelectedColumns()");
        source.Should().Contain("CompleteWorksheetStructureEdit(result");
        source.Should().NotContain("new InsertRowsCommand");
        source.Should().NotContain("new InsertColumnsCommand");
        source.Should().NotContain("new InsertCellsCommand");
        source.Should().NotContain("new DeleteRowsCommand");
        source.Should().NotContain("new DeleteColumnsCommand");
        source.Should().NotContain("new DeleteCellsCommand");
        source.Should().Contain("new RemoveSheetCommand(_currentSheetId)");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height)");
        source.Should().Contain("RowColumnSizingPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width)");
        source.Should().Contain("RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)");
        source.Should().Contain("RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)");
        source.Should().Contain("RowColumnSizingPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("RowColumnSizingPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden)");
        source.Should().Contain("private void FormatRenameSheetMenuItem_Click(object sender, RoutedEventArgs e) => RenameCurrentSheet();");
        source.Should().Contain("private void FormatTabColorMenuItem_Click(object sender, RoutedEventArgs e) => ColorCurrentSheetTab();");
        source.Should().Contain("private void FormatHideSheetMenuItem_Click(object sender, RoutedEventArgs e) => HideCurrentSheet();");
        source.Should().Contain("private void FormatUnhideSheetMenuItem_Click(object sender, RoutedEventArgs e) => UnhideSheet();");
        source.Should().Contain("private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(Locked: !style.Locked))");
        source.Should().Contain("private void FormatCellsMenuItem_Click(object sender, RoutedEventArgs e) => OpenFormatCellsDialog();");
        source.Should().Contain("private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)");
        // R128-cellscmds-formatcells-activecell-1: the dialog must seed from the TRUE active cell of
        // the selection (SheetGrid.ActiveCell), not SelectedRange's normalized top-left Start -- see
        // ResolveFormatCellsSeedCell and R128_FormatCellsDialogActiveCellSeedTests. Previously this
        // pinned "var selectedCell = sheet.GetCell(range.Start);", which encoded the bug: a backward-
        // extended selection (e.g. click C5, Shift+click A1) seeded the dialog from A1 instead of C5,
        // contradicting the ribbon toggles shown for the same selection.
        source.Should().Contain("var selectedCell = sheet.GetCell(ResolveFormatCellsSeedCell(range));");
        source.Should().Contain("var numberPreviewText = selectedCell is null");
        source.Should().Contain(": GetAutoFitDisplayText(sheet, selectedCell);");
        source.Should().Contain("new FormatCellsDialog(currentStyle, initialTab, mergeCells, numberPreviewText)");
        source.Should().Contain("CellMergePlanner.IsSelectionMerged(sheet, range)");
        source.Should().Contain("dlg.ResultMergeCells == true && !TryResolveMergeContentResolution(range, out mergeContentResolution)");
        source.Should().Contain("MergeCellContentResolution mergeContentResolution = MergeCellContentResolution.KeepFirstCell");
        source.Should().Contain("CellMergePlanner.CreateFormatCellsMergeCommands(");
        source.Should().Contain("mergeContentResolution");
    }

    [Fact]
    public void GetAutoFitCellText_ThreadsFontSizeAlongsideWrapTextAndTextRotation()
    {
        // R83-commands-rowcol-size-5-2: GetAutoFitCellText must read style.FontSize (not just
        // WrapText/TextRotation), otherwise a large-font, unwrapped/unrotated cell never grows the
        // row via AutoFitSizingService.EstimateRowHeight (which needs FontSize to do so).
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");

        source.Should().Contain(
            "return new AutoFitCellText(GetAutoFitDisplayText(sheet, cell), style.WrapText, TextRotation: style.TextRotation, FontSize: style.FontSize);");
    }

    [Fact]
    public void SheetVisibilityCommands_ShareSheetTabVisibilityWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        source.Should().Contain("private void SheetCtxHide_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("HideSheets(selectedSheetIds);");
        source.Should().Contain("private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("UnhideSheet();");
        source.Should().Contain("private void HideCurrentSheet()");
        source.Should().Contain("private void HideSheets(IReadOnlyCollection<SheetId> sheetIds)");
        source.Should().Contain("new SetSheetHiddenCommand(sheetId, hidden: true)");
        source.Should().Contain("private void UnhideSheet()");
        source.Should().Contain("new UnhideSheetDialog(hiddenSheets.Select(sheet => sheet.Name))");
        source.Should().Contain("new SetSheetHiddenCommand(sheet.Id, hidden: false)");
    }

    [Fact]
    public void TabColorCommand_SharesSheetTabColorWorkflow()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        source.Should().Contain("private void SheetCtxTabColor_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ColorSheetTabs(tab.Id, selectedSheetIds);");
        source.Should().Contain("private void ColorCurrentSheetTab()");
        source.Should().Contain("ColorSheetTabs(_currentSheetId, selectedSheetIds);");
        source.Should().Contain("private void ColorSheetTabs(SheetId sheetId, IReadOnlyCollection<SheetId> sheetIds)");
        source.Should().Contain("TryShowColorPicker(\"Tab Color\"");
        source.Should().Contain("new SetSheetTabColorCommand(id, tabColor)");
    }

}
