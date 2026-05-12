namespace Freexcel.Core.Model;

/// <summary>Display-ready cell data sent from the engine to the UI viewport.</summary>
public sealed record DisplayCell(
    uint Row,
    uint Col,
    ScalarValue? RawValue,
    string DisplayText,
    string? Formula,
    StyleId StyleId,
    CellError? Error,
    CellStyle? Style = null);

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
    bool IncludeObjects = true);

public sealed record ViewportModel(
    IReadOnlyList<DisplayCell> Cells,
    IReadOnlyList<RowMetric> RowMetrics,
    IReadOnlyList<ColMetric> ColMetrics,
    FrozenPaneState? FrozenPanes = null,
    IReadOnlyList<OverlayPrimitive> Overlays = null!);

public sealed record RowMetric(uint Row, double Height, double TopOffset);
public sealed record ColMetric(uint Col, double Width, double LeftOffset);
public sealed record FrozenPaneState(uint Rows, uint Cols);
public sealed record OverlayPrimitive(); // Placeholder for charts, etc.
