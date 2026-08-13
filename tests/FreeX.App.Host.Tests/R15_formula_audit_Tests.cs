using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-15 regression coverage for R15-formula-auditing-help-2:
/// ClearFormulaTraceArrowsAfterStructuralEdit existed in MainWindow.FormulaCommands.cs but had zero
/// call sites, so trace arrows went stale after a row/column insert or delete. These are
/// source-contract tests (matching the existing DialogSourceTestSupport pattern used elsewhere in
/// this project, e.g. FormulaAuditCommandSourceTests) rather than a live UI-driven test, since
/// MainWindow requires a full WPF/workbook host to construct.
/// </summary>
public sealed class R15_formula_audit_Tests
{
    [Fact]
    public void ClearFormulaTraceArrowsAfterStructuralEdit_ActuallyClearsTheArrowList()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var method = SourceTextTestSupport.ExtractBetweenMarkers(
            source,
            "private void ClearFormulaTraceArrowsAfterStructuralEdit()",
            "private void ShowFormulasBtn_Click");

        method.Should().Contain("_formulaTraceArrows.Clear();");
    }

    [Theory]
    [InlineData("private void InsertRows(uint beforeRow)", "private void InsertColumns(uint beforeCol)")]
    [InlineData("private void InsertColumns(uint beforeCol)", "private void DeleteSelectedRows()")]
    [InlineData("private void DeleteSelectedRows()", "private void DeleteSelectedColumns()")]
    [InlineData("private void DeleteSelectedColumns()", "private void ApplyNumberFormatShortcut")]
    public void CellsCommands_DedicatedInsertDeleteRowColumnMethods_ApplyPortableStructureOutcome(
        string startMarker, string endMarker)
    {
        // Pre-fix, ClearFormulaTraceArrowsAfterStructuralEdit() was never called from
        // MainWindow.CellsCommands.cs at all (it was "ready to wire in, not yet invoked"), so this
        // fails against the original source and passes once each structural-edit method calls it.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var body = SourceTextTestSupport.ExtractBetweenMarkers(source, startMarker, endMarker);

        body.Should().Contain("CompleteWorksheetStructureEdit(result");
    }

    [Fact]
    public void CellsCommands_InsertCellsMenuItemClick_AppliesPortableStructureOutcome()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var body = SourceTextTestSupport.ExtractBetweenMarkers(
            source, "private void InsertCellsMenuItem_Click", "private void InsertSheetMenuItem_Click");

        body.Should().Contain("CompleteWorksheetStructureEdit(result);");
    }

    [Fact]
    public void CellsCommands_DeleteCellsMenuItemClick_AppliesPortableStructureOutcome()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var body = SourceTextTestSupport.ExtractBetweenMarkers(
            source, "private void DeleteCellsMenuItem_Click", "private void DeleteSheetMenuItem_Click");

        body.Should().Contain("CompleteWorksheetStructureEdit(result);");
    }

    [Theory]
    [InlineData("private bool ExecuteKeyboardInsertCellsWithPrompt", "private bool ExecuteKeyboardDeleteCellsWithPrompt")]
    [InlineData("private bool ExecuteKeyboardDeleteCellsWithPrompt", "private bool TryShowCellShiftDialog")]
    public void CellsCommands_KeyboardEntireRowColumnPromptMethods_RouteThroughWorkbookSession(
        string startMarker, string endMarker)
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        var body = SourceTextTestSupport.ExtractBetweenMarkers(source, startMarker, endMarker);

        body.Should().Contain("TryExecuteWorksheetStructure(");
        body.Should().Contain("_session.");
    }

    [Theory]
    [InlineData("private void InsertSheetRows()", "private void InsertSheetColumns()")]
    [InlineData("private void InsertSheetColumns()", "private void DeleteSheetRows()")]
    [InlineData("private void DeleteSheetRows()", "private void DeleteSheetColumns()")]
    [InlineData("private void DeleteSheetColumns()", "private void ToggleSelectedRangeLock()")]
    public void AvaloniaRibbonMenuWires_InsertDeleteSheetRowColumnMethods_ApplyPortableStructureOutcome(
        string startMarker, string endMarker)
    {
        // The Avalonia shell reproduces the identical stale-trace-arrow bug on its own structural
        // row/column edit path (MainWindow.RibbonMenuWires.cs); read the raw source rather than via
        // DialogSourceTestSupport (which is WPF/FreeX.App.Host-scoped) since this is a plain text
        // check with no need for the FreeX.App.Avalonia project reference.
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs");
        var body = SourceTextTestSupport.ExtractBetweenMarkers(source, startMarker, endMarker);

        body.Should().Contain("ApplyWorksheetStructureResult(");
    }

    [Fact]
    public void AvaloniaRibbonMenuWires_DefinesClearFormulaTraceArrowsAfterStructuralEditHelper()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs");

        source.Should().Contain("private void ClearFormulaTraceArrowsAfterStructuralEdit()");
        source.Should().Contain("_formulaTraceArrows.Clear();");
    }
}
