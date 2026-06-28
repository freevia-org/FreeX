using FreeX.Core.Model;
using UiPivotRowLabelAdornment = FreeX.App.UI.PivotRowLabelAdornment;
using SharedPivotGridAdornmentPlanner = FreeX.App.Presentation.PivotUI.PivotGridAdornmentPlanner;

namespace FreeX.App.Host;

/// <summary>
/// WPF shape adapter for shared PivotUI row-label adornment planning. The shared planner owns the pivot
/// hierarchy and indent decisions; Host only maps the framework-neutral record into the UI assembly type.
/// </summary>
public static class PivotRowLabelAdornmentPlanner
{
    public static IReadOnlyList<UiPivotRowLabelAdornment> BuildAdornments(Workbook workbook, Sheet sheet) =>
        SharedPivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet)
            .Select(adornment => new UiPivotRowLabelAdornment(
                adornment.Cell,
                adornment.IndentLevel,
                adornment.ShowExpandCollapseButton,
                adornment.IsExpanded,
                adornment.ReserveTextPadding))
            .ToList();
}
