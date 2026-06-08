namespace FreeX.Core.Model;

/// <summary>Display-ready cell data sent from the engine to the UI viewport.</summary>
public readonly record struct DisplayCell(
    uint Row,
    uint Col,
    ScalarValue? RawValue,
    string DisplayText,
    string? Formula,
    StyleId StyleId,
    CellError? Error,
    CellStyle? Style = null,
    ConditionalFormatIcon? ConditionalIcon = null,
    bool HasComment = false);

public readonly record struct ConditionalFormatIcon(
    string Style,
    int IconIndex,
    int IconCount,
    bool ShowValue);

/// <summary>Represents a cell-level error for display purposes.</summary>
public sealed record CellError(string Code, string? Message = null);

/// <summary>Result of an edit operation.</summary>
public sealed record EditResult(
    IReadOnlyList<CellAddress> ChangedCells,
    IReadOnlyList<CellAddress> DirtyCells,
    bool RequiresRecalc);

/// <summary>Options for creating a new workbook.</summary>
public sealed record NewWorkbookOptions(
    string Name = "Untitled",
    int InitialSheetCount = 1);

/// <summary>Metadata about a workbook.</summary>
public sealed record WorkbookMeta(
    WorkbookId Id,
    string Name,
    int SheetCount,
    bool IsDirty);

/// <summary>Metadata about a sheet.</summary>
public sealed record SheetMeta(
    SheetId Id,
    string Name,
    int Index,
    int CellCount);

public sealed record ViewportRequest(
    uint TopRow,
    uint LeftCol,
    double AvailableHeight,
    double AvailableWidth,
    bool IncludeFormulas = true,
    bool IncludeStyles = true,
    bool IncludeObjects = true,
    SplitPaneViewportOffsets? SplitPaneOffsets = null);

public sealed record SplitPaneViewportOffsets(
    uint? TopRightLeftCol = null,
    uint? BottomLeftTopRow = null);

public sealed record ViewportModel(
    IReadOnlyList<DisplayCell> Cells,
    IReadOnlyList<RowMetric> RowMetrics,
    IReadOnlyList<ColMetric> ColMetrics,
    FrozenPaneState? FrozenPanes = null,
    IReadOnlyList<OverlayPrimitive> Overlays = null!,
    SplitPaneState? SplitPanes = null,
    IReadOnlyList<ChartDataCell> ChartDataCells = null!,
    IReadOnlyList<DrawingObjectBounds> DrawingObjects = null!);

public sealed record ChartDataCell(
    SheetId SheetId,
    uint Row,
    uint Col,
    string DisplayText,
    ScalarValue? RawValue = null);

public sealed record DrawingObjectBounds(
    SelectionPaneObjectKind Kind,
    Guid Id,
    string DisplayName,
    uint AnchorRow,
    uint AnchorCol,
    double Left,
    double Top,
    double Width,
    double Height,
    double RotationDegrees = 0,
    DrawingShapeKind? ShapeKind = null,
    PictureKind? PictureKind = null,
    string? Text = null,
    CellColor? FillColor = null,
    CellColor? OutlineColor = null,
    byte[]? ImageBytes = null,
    string? ImageContentType = null,
    double CropLeft = 0,
    double CropTop = 0,
    double CropRight = 0,
    double CropBottom = 0,
    uint SourceRowCount = 0,
    uint SourceColumnCount = 0,
    IReadOnlyList<PictureCellSnapshot> PictureCells = null!);

public enum DrawingObjectRenderPrimitiveKind
{
    BoundsFallback,
    Shape,
    Image,
    CroppedImage,
    CellRangeSnapshot,
    TextBox
}

public sealed record DrawingObjectRenderPlan(
    DrawingObjectBounds Bounds,
    DrawingObjectRenderPrimitiveKind PrimitiveKind,
    DrawingPictureCrop? Crop = null,
    DrawingPictureGrid? PictureGrid = null,
    string? FallbackReason = null)
{
    public bool IsReady => FallbackReason is null;
}

public sealed record DrawingPictureCrop(
    double Left,
    double Top,
    double Right,
    double Bottom);

public sealed record DrawingPictureGrid(
    uint RowCount,
    uint ColumnCount,
    IReadOnlyList<PictureCellSnapshot> Cells);

public sealed record RowMetric(uint Row, double Height, double TopOffset);
public sealed record ColMetric(uint Col, double Width, double LeftOffset);
public sealed record FrozenPaneState(uint Rows, uint Cols);
public sealed record SplitPaneState(
    uint? Row,
    uint? Column,
    IReadOnlyList<RowMetric> TopRows = null!,
    IReadOnlyList<ColMetric> LeftColumns = null!,
    IReadOnlyList<DisplayCell> Cells = null!,
    IReadOnlyList<ColMetric> TopRightColumns = null!,
    IReadOnlyList<RowMetric> BottomLeftRows = null!);
public sealed record OverlayPrimitive(); // Placeholder for charts, etc.
