using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Portable owner of the "materialize the whole copied range, not just what is scrolled into view"
/// viewport request (P41 / R14-clipboard-formats-deep-1). Both renderer families' clipboard copy
/// paths -- <see cref="WorkbookSession"/> and the WPF host's
/// <c>MainWindow.BuildFullRangeViewportForClipboard</c> -- request their viewport through this
/// single helper, so the generous per-row/per-column pixel bounds and the overflow clamp are
/// declared exactly once rather than copied per host.
/// </summary>
public static class ClipboardViewportPlanner
{
    /// <summary>
    /// Generous per-row pixel bound so the viewport's internal "stop materializing" heuristic
    /// (which walks actual row heights, not this estimate) always reaches past the end of the
    /// requested range even for very tall rows.
    /// </summary>
    public const double MaxPlausibleRowHeight = 500.0;

    /// <summary>Per-column counterpart of <see cref="MaxPlausibleRowHeight"/>, for very wide columns.</summary>
    public const double MaxPlausibleColWidth = 2000.0;

    /// <summary>
    /// Builds a <see cref="ViewportRequest"/> whose top-left is <paramref name="range"/>'s own start
    /// and whose available height/width is sized (generously) to the range's own row/column span, so
    /// every cell in the range is materialized regardless of the current scroll position. The
    /// available extents stay a small constant multiple of the range size rather than the whole
    /// sheet, and are clamped to <c>double.MaxValue / 2</c> so a whole-column/whole-row range cannot
    /// overflow into infinity.
    /// </summary>
    public static ViewportRequest BuildFullRangeViewportRequest(GridRange range)
    {
        var rowSpan = (double)range.RowCount;
        var colSpan = (double)range.ColCount;
        var availableHeight = Math.Min(double.MaxValue / 2, (rowSpan + 2) * MaxPlausibleRowHeight);
        var availableWidth = Math.Min(double.MaxValue / 2, (colSpan + 2) * MaxPlausibleColWidth);

        return new ViewportRequest(
            TopRow: range.Start.Row,
            LeftCol: range.Start.Col,
            AvailableHeight: availableHeight,
            AvailableWidth: availableWidth,
            IncludeObjects: false,
            SplitPaneOffsets: null);
    }
}
