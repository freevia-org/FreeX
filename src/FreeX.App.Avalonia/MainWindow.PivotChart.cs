using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert ▸ PivotChart for the active PivotTable. Resolves the active (or first) pivot on the active sheet
/// — the same resolution <see cref="MainWindow.ResolveInsertControlPivot"/> uses for slicers/timelines —
/// then charts that pivot's rendered RESULT area via the shared Core <see cref="AddPivotChartCommand"/>.
/// The command refreshes the pivot, reads its materialized output range
/// (<c>PivotTableRefreshService.GetMaterializedOutputRange</c>), and adds a PivotChart-flagged
/// <see cref="ChartModel"/> to the sheet, so the shell never has to compute the range itself. A sensible
/// default chart type (clustered column) matches Excel's PivotChart default. On success the host refresh
/// hook redraws the grid so the new chart paints in the drawing-object overlay; on failure the Core guard
/// message is surfaced on the status bar. When no pivot is present the shell shows an explanatory status
/// line rather than crashing.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Inserts a PivotChart over the active PivotTable's result area (Insert-tab ribbon button). Excel's
    /// default PivotChart is a clustered column chart; the Core model represents that as
    /// <see cref="ChartType.Column"/>.
    /// </summary>
    private void InsertPivotChart()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotLoc_SelectCellForPivotChart"));
            return;
        }

        var command = ChartCommandWorkflowPlanner.BuildAddPivotChartCommand(
            _session.ActiveSheet.Id,
            pivot,
            ChartType.Column);
        var result = _session.ExecuteReviewCommand(command);

        RefreshShell(result.Success
            ? UiText.Format("PivotLoc_InsertedPivotChart", pivot.Name)
            : result.ErrorMessage ?? UiText.Get("PivotLoc_InsertPivotChartFailed"));
    }
}
