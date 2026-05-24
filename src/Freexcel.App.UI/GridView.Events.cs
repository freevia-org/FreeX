using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    /// <summary>Fired while the user drags a column border (real-time).</summary>
    public event Action<uint, double>? ColumnResizing;
    /// <summary>Fired when the user releases after resizing a column.</summary>
    public event Action<uint, double>? ColumnResized;

    /// <summary>Fired while the user drags a row border (real-time).</summary>
    public event Action<uint, double>? RowResizing;
    /// <summary>Fired when the user releases after resizing a row.</summary>
    public event Action<uint, double>? RowResized;

    /// <summary>Fired when the user drags the autofill handle and releases.</summary>
    public event Action<GridRange, GridRange>? AutofillRequested;

    /// <summary>Fired on right mouse button down with the clicked cell address.</summary>
    public event Action<CellAddress, System.Windows.Point>? ContextMenuRequested;

    /// <summary>Fired when the user activates a rendered PivotChart field button.</summary>
    public event Action<ChartModel, string, System.Windows.Point>? PivotChartFieldButtonRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    /// <summary>Fired when the user releases after dragging a split-pane divider.</summary>
    public event Action<uint?, uint?>? SplitDividerMoved;

    /// <summary>Fired when the user clicks or drags a split-pane mini scrollbar.</summary>
    public event Action<SplitPaneScrollbarScrollTarget>? SplitPaneScrollbarScrolled;

    /// <summary>Fired when the user finishes dragging a drawing object to a new anchor cell.</summary>
    public event Action<Guid, ObjectKind, CellAddress>? ObjectMoved;

    /// <summary>Fired when the user finishes drag-resizing a drawing object.</summary>
    public event Action<Guid, ObjectKind, double, double>? ObjectResized;
}
