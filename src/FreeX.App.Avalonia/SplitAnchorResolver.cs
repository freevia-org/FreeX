using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Pure decision logic for where View - Split should place its new divider, extracted out of
/// MainWindow.ParityWires.cs's SplitPanesAtActiveCell so the rule is unit-testable without
/// constructing a headless window. Mirrors WPF's SplitViewBtn_Click
/// (MainWindow.ViewCommands.cs, ~lines 372-418):
///   - If a split already exists, clear it (null, null) rather than recomputing a position.
///   - Otherwise split relative to the active/anchor cell: row/col resolve to null when the
///     active cell is already on row 1 / column 1 (nothing above/left of it to split off).
///   - Excel's Split command is never a no-op: when the active cell is A1 so BOTH of the above
///     resolve to null, there is no row/column context to derive a split position from, so fall
///     back to splitting the visible viewport into 4 roughly-equal panes at its midpoint instead
///     of silently doing nothing (R60-commands-freeze-split-6-2). The row and column fallbacks are
///     independent, matching WPF: a viewport with &gt;1 visible row (regardless of column count)
///     still gets a row-only split, and vice versa.
/// </summary>
internal static class SplitAnchorResolver
{
    public static (uint? SplitRow, uint? SplitColumn) Resolve(
        uint activeRow,
        uint activeCol,
        bool wasSplit,
        IReadOnlyList<RowMetric>? viewportRowMetrics = null,
        IReadOnlyList<ColMetric>? viewportColMetrics = null)
    {
        return WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow,
            activeCol,
            wasSplit,
            viewportRowMetrics,
            viewportColMetrics);
    }
}
