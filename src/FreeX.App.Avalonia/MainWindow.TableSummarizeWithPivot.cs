using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Table Design ▸ Tools ▸ "Summarize with PivotTable" for the Avalonia/macOS shell: selects the active
/// structured table's full range, then opens the existing Insert PivotTable dialog
/// (<see cref="ShowInsertPivotTableDialogAsync"/>) seeded from that range — reusing the shared
/// <c>PivotCreatePlanner</c> + Core <c>AddPivotTable…</c> path verbatim. Reached from the
/// <c>tableDesign.summarizeWithPivot</c> ribbon command. Mirrors the WPF host's
/// TableDesignSummarizeWithPivotTableBtn handler, which likewise reuses the Insert PivotTable flow over the
/// active table's range.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Summarize with PivotTable — seeds the Insert PivotTable dialog from the active table's range. Reports
    /// an honest status when the active cell is not inside a structured table.
    /// </summary>
    private void SummarizeActiveTableWithPivot()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        if (!TryGetActiveStructuredTable(out var table))
        {
            RefreshShell(UiText.Get("PivotSummarize_NoTable"));
            return;
        }

        // Anchor the table range onto the active sheet id so the Insert dialog's source is the table itself.
        var sheetId = _session.ActiveSheet.Id;
        var range = new GridRange(
            new CellAddress(sheetId, table.Range.Start.Row, table.Range.Start.Col),
            new CellAddress(sheetId, table.Range.End.Row, table.Range.End.Col));
        _session.SelectRange(range);

        RunGuarded(() => ShowInsertPivotTableDialogAsync());
    }
}
