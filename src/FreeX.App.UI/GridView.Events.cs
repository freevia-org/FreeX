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

    /// <summary>Fired while the user drags the autofill handle near a viewport edge.</summary>
    public event Action<GridAutoScrollRequest>? AutofillEdgeScrollRequested;

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

    /// <summary>Fired when the user activates a rendered PivotChart field button.</summary>
    public event Action<ChartModel, string, System.Windows.Point>? PivotChartFieldButtonRequested;

    /// <summary>Fired when the user right-clicks a waterfall chart point.</summary>
    public event Action<ChartModel, int, System.Windows.Point>? WaterfallChartPointContextMenuRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    /// <summary>Fired when the user releases after dragging a split-pane divider.</summary>
    public event Action<uint?, uint?>? SplitDividerMoved;

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
}
