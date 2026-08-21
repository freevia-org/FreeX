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
    bool HasComment = false,
    ConditionalFormatDataBar? ConditionalDataBar = null,
    CellCommentDisplay? CommentDisplay = null);

public enum CellCommentDisplayKind
{
    Note,
    ThreadedComment,
    Mixed
}

public sealed record CellCommentDisplay(
    CellCommentDisplayKind Kind,
    string Title,
    string Body,
    bool IsResolved = false);

public readonly record struct ConditionalFormatIcon(
    string Style,
    int IconIndex,
    int IconCount,
    bool ShowValue);

public readonly record struct ConditionalFormatDataBar(
    double StartFraction,
    double EndFraction,
    RgbColor FillColor,
    bool Gradient,
    bool Border,
    bool ShowValue,
    bool IsNegative = false,
    double AxisFraction = 0d,
    RgbColor? NegativeFillColor = null,
    RgbColor? AxisColor = null,
    RgbColor? BorderColor = null);

/// <summary>Represents a cell-level error for display purposes.</summary>
public sealed record CellError(string Code, string? Message = null);

/// <summary>Result of an edit operation.</summary>
public sealed record EditResult(
    IReadOnlyList<CellAddress> ChangedCells,
    IReadOnlyList<CellAddress> DirtyCells,
    bool RequiresRecalc);

public sealed record ViewportRequest(
    uint TopRow,
    uint LeftCol,
    double AvailableHeight,
    double AvailableWidth,
    bool IncludeFormulas = true,
    bool IncludeStyles = true,
    bool IncludeObjects = true,
    SplitPaneViewportOffsets? SplitPaneOffsets = null,
    uint? FrozenRowsOverride = null,
    uint? FrozenColsOverride = null,
    SplitPaneStateOverride? SplitOverride = null,
    // A caller's own per-view Show Formulas toggle (e.g. the WPF host's per-window
    // WorksheetViewStateStore -- R89-show-formulas-per-window-1), so Ctrl+` set in one
    // View > New Window sibling never leaks into another sibling's displayed cell text.
    // Null (the default) preserves the pre-existing behavior of ViewportService.GetDisplayText
    // reading the shared Sheet.ShowFormulas field directly.
    bool? ShowFormulasOverride = null);

public sealed record SplitPaneViewportOffsets(
    uint? TopRightLeftCol = null,
    uint? BottomLeftTopRow = null);

/// <summary>
/// Carries a caller's own per-view Window ▸ Split boundary (e.g. <c>WorkbookSession</c>'s
/// per-window <c>GetEffectiveSplitRow</c>/<c>GetEffectiveSplitCol</c>) so <see cref="ViewportRequest"/>
/// can override the shared <see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/> fields for
/// this one viewport build. Passing an instance -- even one whose fields are both null, meaning
/// "no split in this view" -- is authoritative and wins over the shared sheet fields; leaving
/// <see cref="ViewportRequest.SplitOverride"/> null (the default) preserves the pre-existing
/// behavior of always reading the shared sheet fields directly.
/// </summary>
public sealed record SplitPaneStateOverride(uint? SplitRow, uint? SplitCol);

public sealed record ViewportModel(
    IReadOnlyList<DisplayCell> Cells,
    IReadOnlyList<RowMetric> RowMetrics,
    IReadOnlyList<ColMetric> ColMetrics,
    FrozenPaneState? FrozenPanes = null,
    IReadOnlyList<OverlayPrimitive> Overlays = null!,
    SplitPaneState? SplitPanes = null,
    IReadOnlyList<ChartDataCell> ChartDataCells = null!,
    IReadOnlyList<DrawingObjectBounds> DrawingObjects = null!,
    IReadOnlyList<OutlineGroupRange> RowOutlineGroups = null!,
    IReadOnlyList<OutlineGroupRange> ColumnOutlineGroups = null!,
    IReadOnlyDictionary<(uint Row, uint Col), BorderFringeEdges>? BorderFringe = null);

/// <summary>
/// Borders authored on a cell that has scrolled just off one edge of the rendered viewport
/// window (<see cref="ViewportModel.RowMetrics"/>/<see cref="ViewportModel.ColMetrics"/>),
/// keyed by the still-visible boundary cell whose physical edge it shares. Real Excel renders a
/// shared-edge border identically regardless of scroll position; without this, the renderer's
/// own neighbor-precedence lookup (which is built only from the currently windowed
/// <see cref="ViewportModel.Cells"/>) can never see that off-screen neighbor's border, so the
/// line silently disappears the instant its authoring cell scrolls out of view. Populated only
/// for the four true viewport edges (never for an interior scroll boundary, which always has
/// both neighboring cells loaded), and only when the off-screen neighbor actually carries a
/// border on the facing edge -- so this is null/empty in the overwhelmingly common case.
/// </summary>
public sealed record BorderFringeEdges(
    CellBorder? Top = null,
    CellBorder? Bottom = null,
    CellBorder? Left = null,
    CellBorder? Right = null);

public sealed record OutlineGroupRange(
    int Level,
    uint Start,
    uint End,
    uint ToggleIndex,
    bool IsCollapsed);

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
    bool FlipHorizontal = false,
    bool FlipVertical = false,
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
    IReadOnlyList<PictureCellSnapshot> PictureCells = null!,
    DrawingObjectEffect? Effect = null,
    // Shape outline — populated only for shapes; defaults preserve the legacy 1.5 px solid stroke.
    double OutlineWidthPoints = 0,
    DrawingShapeOutlineDash OutlineDash = DrawingShapeOutlineDash.Solid,
    bool OutlineHasNoFill = false,
    // Arrowheads — populated only for line-like shapes (Line, ElbowConnector, CurvedConnector).
    DrawingArrowhead? HeadArrowhead = null,
    DrawingArrowhead? TailArrowhead = null,
    // Shape text — populated only for shapes that carry authored text.
    string? ShapeText = null,
    double ShapeTextFontSizePoints = 0,
    bool ShapeTextBold = false,
    bool ShapeTextItalic = false,
    bool ShapeTextUnderline = false,
    CellColor? ShapeTextColor = null,
    DrawingShapeTextHAlign ShapeTextHAlign = DrawingShapeTextHAlign.Left,
    DrawingShapeTextVAnchor ShapeTextVAnchor = DrawingShapeTextVAnchor.Middle,
    bool ShapeTextWrap = true,
    // WordArt — populated only for WordArt-style shapes (IsWordArt = true).
    bool IsWordArt = false,
    CellColor? ShapeTextGradientEndColor = null,
    CellColor? ShapeTextOutlineColor = null,
    double ShapeTextOutlineWidthPoints = 0,
    // Authored shape gradient fill, projected from DrawingShapeModel for hosts that render
    // drawing overlays from viewport bounds rather than the original model.
    CellColor? GradientFillEndColor = null,
    DrawingShapeGradientDirection GradientFillDirection = DrawingShapeGradientDirection.DiagonalDown);

/// <summary>
/// Render-plan projection of a drawing object's authored visual effect (shadow / glow /
/// soft-edges / bevel / reflection / 3-D rotation). Kept deliberately minimal: just enough for a
/// shell to render a believable approximation without re-deriving the source theme effect data.
/// </summary>
public sealed record DrawingObjectEffect(
    DrawingShapeEffectPreset Preset,
    double OffsetX = 0,
    double OffsetY = 0,
    double BlurRadius = 0,
    double Opacity = 0,
    CellColor? Color = null)
{
    public bool HasShadow => Preset is DrawingShapeEffectPreset.Shadow or DrawingShapeEffectPreset.InnerShadow;
    public bool HasGlow => Preset == DrawingShapeEffectPreset.Glow;
    public bool HasSoftEdges => Preset == DrawingShapeEffectPreset.SoftEdges;
}

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
