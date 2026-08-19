using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public enum GridHeaderContextMenuTarget
{
    Row,
    Column
}

public enum GridOutlineGroupAxis
{
    Rows,
    Columns
}

public readonly record struct GridOutlineGroupToggleRequest(
    GridOutlineGroupAxis Axis,
    int Level,
    uint Start,
    uint End,
    bool Collapse);

/// <summary>A click on one of the numbered "Show Outline Level N" gutter buttons.</summary>
public readonly record struct GridOutlineLevelButtonRequest(
    GridOutlineGroupAxis Axis,
    int Level);

public sealed class GridNoteInlineEditSubmittedEventArgs(CellAddress address, string text) : EventArgs
{
    public CellAddress Address { get; } = address;

    public string Text { get; } = text;

    public bool KeepOpen { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class GridThreadedCommentInlineEditSubmittedEventArgs(
    CellAddress address,
    ThreadedCommentDialogResult result) : EventArgs
{
    public CellAddress Address { get; } = address;

    public ThreadedCommentDialogResult Result { get; } = result;

    public bool KeepOpen { get; set; }

    public string? ErrorMessage { get; set; }
}

public partial class GridView
{
    /// <summary>Fired while the user drags a column border (real-time).</summary>
    public event Action<uint, double>? ColumnResizing;
    /// <summary>Fired when the user releases after resizing a column.</summary>
    public event Action<uint, double>? ColumnResized;
    /// <summary>Fired when the user double-clicks a column border to AutoFit.</summary>
    public event Action<uint>? ColumnAutoFitRequested;

    /// <summary>Fired while the user drags a row border (real-time).</summary>
    public event Action<uint, double>? RowResizing;
    /// <summary>Fired when the user releases after resizing a row.</summary>
    public event Action<uint, double>? RowResized;
    /// <summary>Fired when the user double-clicks a row border to AutoFit.</summary>
    public event Action<uint>? RowAutoFitRequested;
    /// <summary>Fired when an in-progress row or column resize is canceled.</summary>
    public event Action? ResizeCanceled;

    /// <summary>Fired when the user drags the autofill handle and releases.</summary>
    public event Action<GridRange, GridRange>? AutofillRequested;

    /// <summary>
    /// Fired immediately before <see cref="AutofillRequested"/> with the Ctrl-key state at
    /// release. Excel uses Ctrl at drop time to flip the fill handle's default behavior between
    /// copy and series continuation. Hosts that want Ctrl-flip support should read this value
    /// (e.g. into a field) in a handler for this event and pass it into
    /// <c>new AutofillCommand(sheetId, sourceRange, fillRange, ctrlHeld)</c> when handling the
    /// paired <see cref="AutofillRequested"/> call.
    /// </summary>
    public event Action<bool>? AutofillModifiersResolved;

    /// <summary>Fired while the user drags the autofill handle near a viewport edge.</summary>
    public event Action<GridAutoScrollRequest>? AutofillEdgeScrollRequested;

    /// <summary>
    /// Fired when the user double-clicks the fill handle instead of dragging it. Excel fills
    /// straight down to match the populated data extent of the nearest adjacent column. GridView
    /// has no access to cell data, so the host must resolve that extent (e.g. scanning the
    /// nearest non-blank neighbor column) and pass it to
    /// <see cref="FreeX.App.Presentation.GridInteraction.GridAutofillPlanner.CalculateDoubleClickFillRange"/>
    /// to compute the resulting fill range, then execute the fill the same way as
    /// <see cref="AutofillRequested"/>.
    /// </summary>
    public event Action<GridRange>? AutofillHandleDoubleClicked;

    /// <summary>Fired when the user drags a selected range border and releases on a new range.</summary>
    public event Action<GridRange, GridRange>? SelectionMoveRequested;

    /// <summary>Fired on right mouse button down with the clicked cell address.</summary>
    public event Action<CellAddress, System.Windows.Point>? ContextMenuRequested;

    /// <summary>Fired on right mouse button down over a row or column header.</summary>
    public event Action<GridHeaderContextMenuTarget, uint, System.Windows.Point>? HeaderContextMenuRequested;

    /// <summary>Fired when the user activates a rendered worksheet AutoFilter dropdown button.</summary>
    public event Action<CellAddress, System.Windows.Point>? AutoFilterDropdownRequested;

    /// <summary>Fired when the user activates a rendered PivotTable row/column header dropdown button.</summary>
    public event Action<CellAddress, System.Windows.Point>? PivotHeaderDropdownRequested;

    /// <summary>Fired when the user activates a rendered outline group collapse/expand button.</summary>
    public event Action<GridOutlineGroupToggleRequest>? OutlineGroupToggleRequested;

    /// <summary>Fired when the user clicks a numbered "Show Outline Level N" gutter button.</summary>
    public event Action<GridOutlineLevelButtonRequest>? OutlineLevelButtonRequested;

    /// <summary>Fired when the user activates a rendered PivotChart field button.</summary>
    public event Action<ChartModel, string, System.Windows.Point>? PivotChartFieldButtonRequested;

    /// <summary>Fired when the user right-clicks a waterfall chart point.</summary>
    public event Action<ChartModel, int, System.Windows.Point>? WaterfallChartPointContextMenuRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    /// <summary>Fired when the user releases after dragging a split-pane divider.</summary>
    public event Action<uint?, uint?>? SplitDividerMoved;

    /// <summary>
    /// Fired when the user releases after dragging a manual page-break line in Page Break Preview
    /// view. <c>originalIndex</c> is the break's row/column before the drag; <c>newIndex</c> is where
    /// it should move to, or <c>null</c> if it was dragged off the print area and should be removed.
    /// </summary>
    public event Action<PageBreakLineOrientation, uint, uint?>? PageBreakLineMoved;

    /// <summary>Fired when the user clicks or drags a split-pane mini scrollbar.</summary>
    public event Action<SplitPaneScrollbarScrollTarget>? SplitPaneScrollbarScrolled;

    /// <summary>Fired when the user finishes dragging a drawing object to a new anchor cell.</summary>
    public event Action<Guid, ObjectKind, CellAddress>? ObjectMoved;

    /// <summary>Fired when the user finishes moving or resizing an embedded chart object.</summary>
    public event Action<Guid, double, double, double, double>? ChartBoundsChanged;

    /// <summary>Fired when the user finishes drag-resizing a drawing object.</summary>
    public event Action<Guid, ObjectKind, double, double, bool, bool>? ObjectResized;

    /// <summary>
    /// Fired when the user finishes drag-resizing a drawing object from a handle that also
    /// moves the normalized top-left corner, so the new anchor cell, size, and flip state are committed together.
    /// </summary>
    public event Action<Guid, ObjectKind, CellAddress, double, double, bool, bool>? ObjectResizedWithAnchor;

    /// <summary>Fired when the user finishes rotating a drawing object via the rotation grip.</summary>
    public event Action<Guid, ObjectKind, double>? ObjectRotated;

    /// <summary>Fired when the user finishes dragging a selected picture crop handle.</summary>
    public event Action<Guid, PictureCropRatios>? PictureCropped;

    /// <summary>Fired when the user finishes placing a new drawing shape on the grid.</summary>
    public event Action<ShapePlacementRequest>? ShapePlacementRequested;

    /// <summary>Fired when the user finishes placing a new text box on the grid.</summary>
    public event Action<TextBoxPlacementRequest>? TextBoxPlacementRequested;

    /// <summary>Fired when the user saves an in-window legacy note edit.</summary>
    public event EventHandler<GridNoteInlineEditSubmittedEventArgs>? NoteInlineEditSubmitted;

    /// <summary>Fired when the user saves an in-window threaded comment edit.</summary>
    public event EventHandler<GridThreadedCommentInlineEditSubmittedEventArgs>? ThreadedCommentInlineEditSubmitted;

    /// <summary>Fired when the user requests in-place editing for an existing text box.</summary>
    public event Action<Guid>? TextBoxEditRequested;

    /// <summary>
    /// Fired when the user clicks the clear-filter icon on a native slicer header — carry the slicer
    /// name. The host should commit a <c>SetSlicerSelectionCommand</c> with an empty selection list.
    /// </summary>
    public event Action<string>? NativeSlicerClearFilterRequested;

    /// <summary>
    /// Fired when the user clicks a tile in a native slicer — carry the slicer name and the tile
    /// caption that was hit. The host computes the toggle and commits the filter command.
    /// </summary>
    public event Action<string, string>? NativeSlicerTileToggleRequested;

    /// <summary>
    /// Fired when the user clicks the clear-filter icon on a native timeline header — carry the
    /// timeline name. The host should commit a <c>SetTimelineRangeCommand(null, null)</c>.
    /// </summary>
    public event Action<string>? NativeTimelineClearFilterRequested;

    /// <summary>
    /// Fired when the user clicks the granularity dropdown on a native timeline header — carry the
    /// timeline name. The host cycles the granularity level and commits the command.
    /// </summary>
    public event Action<string>? NativeTimelineGranularityToggleRequested;

    /// <summary>
    /// Fired when the user clicks the track or a drag handle on a native timeline — carry the
    /// timeline name and the new start/end date strings (yyyy-MM-dd, or null for open-ended).
    /// </summary>
    public event Action<string, string?, string?>? NativeTimelineRangeRequested;
}
