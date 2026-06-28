using FreeX.Core.Model;
using SharedPivotGridAdornmentPlanner = FreeX.App.Presentation.PivotUI.PivotGridAdornmentPlanner;
using SharedPivotHeaderArea = FreeX.App.Presentation.PivotUI.PivotHeaderArea;

namespace FreeX.App.Host;

public enum PivotHeaderDropdownAxis
{
    Row,
    Column,
    Page
}

public sealed record PivotHeaderDropdownTarget(
    string PivotTableName,
    string FieldCaption,
    int SourceFieldIndex,
    PivotHeaderDropdownAxis Axis,
    CellAddress HeaderCell,
    bool IsActive);

/// <summary>
/// WPF shape adapter for shared PivotUI header-target planning. Keep coordinate, source-header, and
/// active-filter logic in <see cref="SharedPivotGridAdornmentPlanner"/>; this mapper preserves the Host
/// target record consumed by the WPF grid event pipeline.
/// </summary>
public static class PivotHeaderDropdownPlanner
{
    public static IReadOnlyList<PivotHeaderDropdownTarget> BuildTargets(Workbook workbook, Sheet sheet) =>
        SharedPivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet)
            .Select(target => new PivotHeaderDropdownTarget(
                target.MenuTarget.PivotTableName,
                target.MenuTarget.FieldCaption,
                target.MenuTarget.SourceFieldIndex,
                ToHostAxis(target.MenuTarget.Area),
                target.HeaderCell,
                target.IsActive))
            .ToList();

    private static PivotHeaderDropdownAxis ToHostAxis(SharedPivotHeaderArea area) =>
        area switch
        {
            SharedPivotHeaderArea.Row => PivotHeaderDropdownAxis.Row,
            SharedPivotHeaderArea.Column => PivotHeaderDropdownAxis.Column,
            SharedPivotHeaderArea.Page => PivotHeaderDropdownAxis.Page,
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "WPF pivot headers only expose row, column, and page dropdown targets.")
        };
}
