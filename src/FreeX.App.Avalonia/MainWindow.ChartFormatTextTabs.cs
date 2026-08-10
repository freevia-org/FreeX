using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The remaining Chart Format contextual-tab "quick" buttons that step a single value per click (no dialog):
/// the Text group's chart-title / axis-title / legend / data-label color and font-size buttons, and the
/// Shape Styles group's Series Dash, Series Marker (opens the full series dialog) and Marker Size, plus the
/// Chart Design Type group's Combo Chart and Combo Chart Series quick mutations. Each resolves the selected chart, computes the next
/// value through the shared <see cref="ChartQuickCommandPlanner"/>, and applies it through the shared
/// <see cref="SetChartLayoutCommand"/> via <see cref="ApplyChartLayout"/>.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Chart Design ▸ Type: Combo Chart (WPF's immediate toggle) -------------------------------

    private void CycleChartCombo()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.ComboToggle,
            ChartWorkflowUnsupportedStatus(ChartWorkflowCommandCatalog.ComboChart));
    }

    // ---- Chart Design ▸ Type: Combo Chart Series (quick toggle) ---------------------------------------

    private void CycleChartComboSeries()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.ComboSeries,
            ChartWorkflowUnsupportedStatus(ChartWorkflowCommandCatalog.ComboChart));
    }

    // ---- Chart Format ▸ Text group: title / axis-title / legend / data-label color & size -------------

    private void CycleChartSecondaryAxisSeries()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.SecondaryAxisSeries,
            UiText.Get("MainWindowMessage_ChartSecondaryAxisUnsupported"));
    }

    private void CycleChartTitleColor()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleColor);
    }

    private void CycleChartTitleSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.ChartTitleFontSize);
    }

    private void CycleChartAxisTitleColor()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleColor);
    }

    private void CycleChartAxisTitleSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.AxisTitleFontSize);
    }

    private void CycleChartLegendFontSize()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.LegendFontSize);
    }

    private void CycleChartDataLabelText()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelTextColor);
    }

    private void CycleChartDataLabelFill()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelFill);
    }

    private void CycleChartDataLabelBorder()
    {
        ExecuteChartQuickCommand(ChartQuickCommandCatalog.DataLabelBorder);
    }

    // ---- Chart Format ▸ Shape Styles group: Series Dash / Marker / Marker Size ------------------------

    private void CycleChartSeriesDash()
    {
        ExecuteChartQuickCommand(
            ChartQuickCommandCatalog.SeriesDash,
            ChartWorkflowUnsupportedStatus(ChartWorkflowCommandCatalog.FormatDataSeries));
    }

    private void CycleChartMarkerSize()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;
        var command = ChartQuickCommandCatalog.SeriesMarkerSize;
        if (!TryGetSelectedChart(command.Label, out var chart))
            return;

        if (!ChartWorkflowCommandCatalog.CanOpenDialog(chart, ChartWorkflowCommandCatalog.FormatDataSeries))
        {
            RefreshUnsupportedChartWorkflow(ChartWorkflowCommandCatalog.FormatDataSeries);
            return;
        }

        ExecuteChartQuickCommand(command);
    }

    private void ExecuteChartQuickCommand(
        ChartQuickCommandDescriptor command,
        string? unsupportedMessage = null)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var selectedChartId = _selectedDrawingObjectKind == SelectionPaneObjectKind.Chart
            ? _selectedDrawingObjectId
            : null;
        var plan = ChartCommandWorkflowPlanner.PlanQuickCommand(
            _session.ActiveSheet.Id,
            _session.ActiveSheet,
            selectedChartId,
            ChartWorkflowTargetPolicy.SelectedOnly,
            command);
        if (!plan.CanExecute)
        {
            RefreshShell(plan.Issue == ChartLayoutCommandIssue.MissingChart
                ? UiText.Format(ChartWorkflowCommandCatalog.SelectChartBeforeUsingStatusResourceKey, command.Label)
                : unsupportedMessage ?? ChartQuickUnsupportedStatus(command));
            return;
        }

        var result = _session.ExecuteReviewCommand(plan.Command!);
        RefreshShell(ChartWorkflowCommandCatalog
            .DescribeCommandResult(result.Success, command.Label, result.ErrorMessage)
            .Resolve(UiText.Get, UiText.Format));
    }

    private static string ChartQuickUnsupportedStatus(ChartQuickCommandDescriptor command) =>
        command.UnsupportedStatusResourceKey is { } resourceKey
            ? UiText.Get(resourceKey)
            : UiText.Format(ChartWorkflowCommandCatalog.CommandNotYetAvailableStatusResourceKey, command.Label);
}
