using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

// ─── Resolved text types ──────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolved text-run shadow for the renderer (concrete DIP values).
/// </summary>
public sealed class ResolvedRunShadow
{
    public SrgbColor Color { get; init; }
    public byte Alpha { get; init; }
    /// <summary>Blur radius in DIP.</summary>
    public double BlurDip { get; init; }
    /// <summary>Offset distance in DIP.</summary>
    public double DistDip { get; init; }
    /// <summary>Direction in degrees clockwise from right.</summary>
    public double DirDeg { get; init; }
}

/// <summary>
/// A fully-resolved text run ready for the renderer: all inherited properties have been applied
/// so the renderer sees concrete values without any nulls.
/// </summary>
public sealed class ResolvedRun
{
    public string Text { get; init; } = string.Empty;
    public string FontFamily { get; init; } = "Calibri";
    public double FontSizePt { get; init; } = 18.0;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public SrgbColor Color { get; init; } = SrgbColor.Black;

    // ── Wave 16A: text effects ────────────────────────────────────────────────
    /// <summary>
    /// Resolved glyph fill — null means use <see cref="Color"/> as solid fill (legacy path).
    /// Non-null carries a gradient or other complex fill for the glyphs.
    /// </summary>
    public ResolvedFill? TextFill { get; init; }

    /// <summary>Resolved glyph outline — null means no outline.</summary>
    public ResolvedOutline? TextOutline { get; init; }

    /// <summary>Resolved glyph shadow — null means no shadow.</summary>
    public ResolvedRunShadow? TextShadow { get; init; }
}

/// <summary>A resolved tab stop with position in DIP.</summary>
public sealed class ResolvedTabStop
{
    /// <summary>Position from left edge of text area in DIP.</summary>
    public double PositionDip { get; init; }
    /// <summary>Tab alignment.</summary>
    public TabStopAlignment Alignment { get; init; }
}

/// <summary>
/// A fully-resolved paragraph ready for the renderer.
/// </summary>
public sealed class ResolvedParagraph
{
    public IReadOnlyList<ResolvedRun> Runs { get; init; } = Array.Empty<ResolvedRun>();
    public TextAlign Align { get; init; } = TextAlign.Left;
    public int Level { get; init; }
    public BulletKind BulletKind { get; init; } = BulletKind.None;
    public string? BulletChar { get; init; }
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; }

    /// <summary>
    /// Resolved tab stops for this paragraph in position order (DIP from text area left edge).
    /// Empty means use the default tab spacing (1 inch = 96 DIP at default DPI).
    /// </summary>
    public IReadOnlyList<ResolvedTabStop> TabStops { get; init; } = Array.Empty<ResolvedTabStop>();

    // ── Wave 19A: bullet rendering fields ────────────────────────────────────

    /// <summary>
    /// The resolved bullet glyph/number text to draw to the left of the paragraph.
    /// Empty string = no bullet (BulletKind.None or empty body paragraph).
    /// </summary>
    public string BulletText { get; init; } = string.Empty;

    /// <summary>Resolved bullet color (may differ from the run color for Char bullets).</summary>
    public SrgbColor BulletColor { get; init; } = SrgbColor.Black;

    /// <summary>Resolved bullet font family (may differ from the paragraph font for Char bullets).</summary>
    public string BulletFontFamily { get; init; } = "Calibri";

    /// <summary>Resolved bullet font size in points (= run size * BulletSizePct / 100).</summary>
    public double BulletFontSizePt { get; init; } = 18.0;

    /// <summary>
    /// Left indent in DIP from the text area edge to the TEXT start position (= marL / EmuPerDip).
    /// Bullets are drawn at IndentDip - HangingDip (the bullet slot).
    /// Zero = no explicit indent.
    /// </summary>
    public double IndentDip { get; init; }

    /// <summary>
    /// Hanging distance in DIP: how far the bullet hangs to the left of IndentDip.
    /// = -indent / EmuPerDip when indent &lt; 0 (hanging), else 0.
    /// </summary>
    public double HangingDip { get; init; }
}

/// <summary>
/// A fully-resolved text layout for a shape: paragraphs with concrete properties + body settings.
/// </summary>
public sealed class ResolvedTextLayout
{
    public IReadOnlyList<ResolvedParagraph> Paragraphs { get; init; } = Array.Empty<ResolvedParagraph>();
    public VerticalAnchor Anchor { get; init; } = VerticalAnchor.Top;

    /// <summary>Left inset in DIP (device-independent pixels at 96 DPI).</summary>
    public double InsetLeftDip { get; init; } = 9.14;  // ~7pt default

    /// <summary>Right inset in DIP.</summary>
    public double InsetRightDip { get; init; } = 9.14;

    /// <summary>Top inset in DIP.</summary>
    public double InsetTopDip { get; init; } = 4.57;   // ~3.5pt default

    /// <summary>Bottom inset in DIP.</summary>
    public double InsetBottomDip { get; init; } = 4.57;

    public bool Wrap { get; init; } = true;

    /// <summary>
    /// WordArt warp preset name, e.g. "textArchUp", "textWave1", "textTriangle".
    /// Null = no warp (flat text).  Used by renderers to apply glyph-path warping.
    /// </summary>
    public string? WarpPreset { get; init; }

    /// <summary>
    /// Text orientation for this body.  Renderers rotate the text block accordingly.
    /// <see cref="TextVerticalType.Horizontal"/> = no extra rotation (default).
    /// </summary>
    public TextVerticalType VerticalType { get; init; } = TextVerticalType.Horizontal;

    // ── Wave 19A: autofit applied ─────────────────────────────────────────────

    /// <summary>
    /// Font scale factor from normAutofit (1.0 = no scaling; 0.625 = 62.5%).
    /// Already applied to all ResolvedRun.FontSizePt values in the Paragraphs list.
    /// Carried here only so renderers can skip double-application.
    /// </summary>
    public double FontScale { get; init; } = 1.0;

    /// <summary>
    /// Line-spacing reduction fraction from normAutofit (0.0 = no reduction; 0.2 = 20% reduction).
    /// Renderers multiply their natural line spacing by (1.0 - LnSpcReduction).
    /// </summary>
    public double LnSpcReduction { get; init; } = 0.0;
}

// ─── Resolved fill/outline ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Resolved fill for a draw operation: concrete sRGB values, no theme refs needed.</summary>
public abstract class ResolvedFill
{
    private ResolvedFill() { }

    /// <summary>Transparent (no fill).</summary>
    public sealed class None : ResolvedFill { public static readonly None Instance = new(); private None() { } }

    /// <summary>Solid color fill.</summary>
    public sealed class Solid : ResolvedFill
    {
        public SrgbColor Color { get; }
        public Solid(SrgbColor color) => Color = color;
    }

    /// <summary>A single resolved gradient stop with concrete sRGB color + position.</summary>
    public sealed class ResolvedGradientStop
    {
        /// <summary>Stop position in [0, 1].</summary>
        public double Position { get; }
        public SrgbColor Color { get; }
        public ResolvedGradientStop(double position, SrgbColor color)
        {
            Position = position;
            Color = color;
        }
    }

    /// <summary>Multi-stop gradient (linear or radial) with resolved colors.</summary>
    public sealed class Gradient : ResolvedFill
    {
        /// <summary>All gradient stops in position order (positions in [0,1]).</summary>
        public IReadOnlyList<ResolvedGradientStop> Stops { get; }

        /// <summary>Gradient kind (Linear or Radial).</summary>
        public GradientKind Kind { get; }

        /// <summary>Angle in degrees (0 = left->right, 90 = top->bottom). Linear only.</summary>
        public double AngleDegrees { get; }

        // ── Back-compat 2-stop accessors ────────────────────────────────────────────
        public SrgbColor StartColor => Stops.Count > 0 ? Stops[0].Color : SrgbColor.Black;
        public SrgbColor EndColor   => Stops.Count > 0 ? Stops[^1].Color : SrgbColor.White;

        public Gradient(IReadOnlyList<ResolvedGradientStop> stops, GradientKind kind, double angleDegrees)
        {
            Stops = stops;
            Kind = kind;
            AngleDegrees = angleDegrees;
        }

        /// <summary>Back-compat 2-stop linear constructor.</summary>
        public Gradient(SrgbColor startColor, SrgbColor endColor, double angleDegrees)
            : this(new[]
            {
                new ResolvedGradientStop(0.0, startColor),
                new ResolvedGradientStop(1.0, endColor)
            }, GradientKind.Linear, angleDegrees)
        {
        }
    }

    /// <summary>Picture (blip) fill with resolved image bytes.</summary>
    public sealed class Picture : ResolvedFill
    {
        public byte[] ImageBytes { get; }
        public string ContentType { get; }
        public bool Tile { get; }
        public Picture(byte[] imageBytes, string contentType, bool tile = false)
        {
            ImageBytes = imageBytes;
            ContentType = contentType;
            Tile = tile;
        }
    }

    /// <summary>Pattern (hatch) fill with resolved fg/bg colors and preset name.</summary>
    public sealed class PatternFill : ResolvedFill
    {
        public string Preset { get; }
        public SrgbColor ForegroundColor { get; }
        public SrgbColor BackgroundColor { get; }
        public PatternFill(string preset, SrgbColor foregroundColor, SrgbColor backgroundColor)
        {
            Preset = preset;
            ForegroundColor = foregroundColor;
            BackgroundColor = backgroundColor;
        }
    }
}

/// <summary>Resolved outline for a draw operation.</summary>
public abstract class ResolvedOutline
{
    private ResolvedOutline() { }

    public sealed class None : ResolvedOutline { public static readonly None Instance = new(); private None() { } }

    public sealed class Visible : ResolvedOutline
    {
        /// <summary>Stroke width in DIP (converted from points via 96/72 scaling).</summary>
        public double WidthDip { get; }
        public OutlineDash Dash { get; }
        public SrgbColor Color { get; }
        public Visible(SrgbColor color, double widthDip, OutlineDash dash)
        {
            Color = color;
            WidthDip = widthDip;
            Dash = dash;
        }
    }
}

// ─── Resolved shape effects ───────────────────────────────────────────────────────────────────────────────────────

/// <summary>Resolved bevel descriptor with DIP values for the renderer.</summary>
public sealed class ResolvedBevel
{
    /// <summary>Bevel width in DIP (converted from EMU).</summary>
    public double WidthDip { get; init; }
    /// <summary>Bevel height in DIP (converted from EMU).</summary>
    public double HeightDip { get; init; }
    /// <summary>Preset name, e.g. "circle", "relaxedInset", "cross". Empty = circle.</summary>
    public string PresetName { get; init; } = string.Empty;
}

/// <summary>Resolved shape effects with concrete DIP values for the renderer.</summary>
public sealed class ResolvedShapeEffects
{
    // Outer shadow
    public bool HasOuterShadow { get; init; }
    public SrgbColor OuterShadowColor { get; init; }
    public byte OuterShadowAlpha { get; init; }
    /// <summary>Blur radius in DIP.</summary>
    public double OuterShadowBlurDip { get; init; }
    /// <summary>Offset distance in DIP.</summary>
    public double OuterShadowDistDip { get; init; }
    /// <summary>Direction in degrees (clockwise from right).</summary>
    public double OuterShadowDirDeg  { get; init; }

    // Glow
    public bool HasGlow { get; init; }
    public SrgbColor GlowColor { get; init; }
    public byte GlowAlpha { get; init; }
    public double GlowRadiusDip { get; init; }

    // Soft edge
    public bool HasSoftEdge { get; init; }
    public double SoftEdgeRadiusDip { get; init; }

    // Bevel / 3-D
    /// <summary>Top-face bevel, or null if none.</summary>
    public ResolvedBevel? BevelTop { get; init; }
    /// <summary>Bottom-face bevel, or null if none.</summary>
    public ResolvedBevel? BevelBottom { get; init; }
    /// <summary>Extrusion depth in DIP. 0 = none.</summary>
    public double ExtrusionDepthDip { get; init; }
    /// <summary>Contour width in DIP. 0 = none.</summary>
    public double ContourWidthDip { get; init; }
    /// <summary>Extrusion colour (for contour outline rendering). Null = no override.</summary>
    public SrgbColor? ContourColor { get; init; }
    /// <summary>
    /// Light direction hint (derived from scene3d lightRig dir=). Used to shift
    /// bevel highlight/shade sides. 0=top, 45=top-right, 90=right, 135=bottom-right etc.
    /// -1 means no scene3d → use default top-left illumination.
    /// </summary>
    public double LightDirDeg { get; init; } = -1;
}

// ─── Draw operations ──────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Base class for a single resolved draw operation emitted by the compositor.
/// Operations are ordered back-to-front (painter's algorithm = z-order).
/// </summary>
public abstract class DrawOp
{
    private DrawOp() { }

    // ── Shape draw op ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw a shape geometry with optional fill, outline, rotation/flip, and text overlay.
    /// All coordinates are in DIP (device-independent pixels, 96 DPI) relative to the slide
    /// top-left corner.
    /// </summary>
    public sealed class Shape : DrawOp
    {
        /// <summary>
        /// The computed geometry for this shape, in DIP coordinates (origin = slide top-left).
        /// Built by <see cref="ShapeGeometryBuilder"/> from the resolved bounds.
        /// </summary>
        public ShapeGeometry Geometry { get; init; } = ShapeGeometry.Empty;

        /// <summary>Resolved fill (None, Solid, or Gradient).</summary>
        public ResolvedFill Fill { get; init; } = ResolvedFill.None.Instance;

        /// <summary>Resolved outline (None or Visible with concrete width/dash/color).</summary>
        public ResolvedOutline Outline { get; init; } = ResolvedOutline.None.Instance;

        /// <summary>Rotation around the shape center, in degrees clockwise.</summary>
        public double RotationDeg { get; init; }

        /// <summary>Horizontal flip flag.</summary>
        public bool FlipH { get; init; }

        /// <summary>Vertical flip flag.</summary>
        public bool FlipV { get; init; }

        /// <summary>
        /// Bounding box of the shape in DIP coordinates (used for text layout, rotation pivot, and hit testing).
        /// </summary>
        public LayoutRect BoundsDip { get; init; }

        /// <summary>Text to render over the shape, or null if the shape has no text.</summary>
        public ResolvedTextLayout? Text { get; init; }

        /// <summary>Resolved shape effects (shadow, glow, soft-edge), or null if none.</summary>
        public ResolvedShapeEffects? Effects { get; init; }
    }

    // ── Picture draw op ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw a picture (raster or vector image) at a given rectangle.
    /// </summary>
    public sealed class Picture : DrawOp
    {
        /// <summary>Raw image bytes (JPEG, PNG, GIF, ...).</summary>
        public byte[] Bytes { get; init; } = Array.Empty<byte>();

        /// <summary>MIME content type (e.g. "image/png").</summary>
        public string ContentType { get; init; } = "image/png";

        /// <summary>Destination rectangle in DIP coordinates.</summary>
        public LayoutRect DestDip { get; init; }

        /// <summary>Rotation around the picture center, in degrees clockwise.</summary>
        public double RotationDeg { get; init; }

        /// <summary>Optional outline drawn around the picture frame (None if no outline).</summary>
        public ResolvedOutline Outline { get; init; } = ResolvedOutline.None.Instance;

        /// <summary>
        /// When true, this picture is a media placeholder (video/audio poster).
        /// The renderer draws a play-button triangle overlay on top.
        /// </summary>
        public bool IsMedia { get; init; }

        // ── 18A: Crop + colour effects ────────────────────────────────────────────

        /// <summary>
        /// Fraction of the source image to remove from the left edge.
        /// 0 = no crop. Combined with CropRight: visible width fraction = 1 - CropLeft - CropRight.
        /// </summary>
        public double CropLeft   { get; init; }
        /// <summary>Fraction of source image to crop from the top.</summary>
        public double CropTop    { get; init; }
        /// <summary>Fraction of source image to crop from the right.</summary>
        public double CropRight  { get; init; }
        /// <summary>Fraction of source image to crop from the bottom.</summary>
        public double CropBottom { get; init; }

        /// <summary>True when any crop fraction is non-zero.</summary>
        public bool HasCrop =>
            CropLeft != 0 || CropTop != 0 || CropRight != 0 || CropBottom != 0;

        /// <summary>Convert to grayscale.</summary>
        public bool Grayscale { get; init; }

        /// <summary>
        /// Black/white threshold (0..1). Null = not active.
        /// Pixels above threshold render white, below render black.
        /// </summary>
        public double? BiLevelThreshold { get; init; }

        /// <summary>Brightness adjustment -1..1. Null = not active.</summary>
        public double? Brightness { get; init; }

        /// <summary>Contrast adjustment -1..1. Null = not active.</summary>
        public double? Contrast { get; init; }

        /// <summary>Opacity multiplier 0..1. Null = fully opaque.</summary>
        public double? AlphaModPct { get; init; }
    }

    // ── Background draw op ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw the slide background (always the first op in the list when the background is not transparent).
    /// </summary>
    public sealed class Background : DrawOp
    {
        public ResolvedFill Fill { get; init; } = ResolvedFill.None.Instance;

        /// <summary>Slide bounds in DIP (always origin-anchored: 0,0 x slideCx x slideCy).</summary>
        public LayoutRect BoundsDip { get; init; }
    }

    // ── Table draw op ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a table: an ordered list of resolved cell operations in painter's order.
    /// The overall bounding box of the table frame is <see cref="BoundsDip"/>.
    /// </summary>
    public sealed class Table : DrawOp
    {
        /// <summary>Bounding box of the entire table frame in DIP.</summary>
        public LayoutRect BoundsDip { get; init; }

        /// <summary>Ordered list of cell draw ops (back to front, row-major).</summary>
        public IReadOnlyList<TableCellOp> Cells { get; init; } = Array.Empty<TableCellOp>();
    }

    // ── Chart draw op ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws an embedded chart with all resolved colors.
    /// The renderer is responsible for computing the full chart layout (axes, bars, lines, etc.)
    /// from the model data within <see cref="BoundsDip"/>.
    /// </summary>
    public sealed class Chart : DrawOp
    {
        /// <summary>Bounding box of the chart frame in DIP.</summary>
        public LayoutRect BoundsDip { get; init; }

        /// <summary>The chart data model (ChartType, Series, Categories, Axes, Legend).</summary>
        public FreeP.Core.Model.ChartShape ChartShape { get; init; } = new();

        /// <summary>
        /// Resolved series fill colors, one per series (index matches ChartShape.Series).
        /// For pie charts, per-point overrides are in ChartShape.Series[i].PointColors
        /// but the base color for each slice is SeriesColors[0][pointIndex % 6].
        /// </summary>
        public IReadOnlyList<SrgbColor> SeriesColors { get; init; } = Array.Empty<SrgbColor>();
    }
}

/// <summary>
/// A single resolved table cell draw operation.
/// Contains the cell's bounding rect (already accounting for spans + table frame position),
/// its resolved fill, per-side borders, and optional text layout.
/// </summary>
public sealed class TableCellOp
{
    /// <summary>Cell rectangle in DIP (the origin cell for merged cells; covered cells are skipped).</summary>
    public LayoutRect BoundsDip { get; init; }

    /// <summary>Resolved fill for the cell (may be None).</summary>
    public ResolvedFill Fill { get; init; } = ResolvedFill.None.Instance;

    /// <summary>Left border (may be None).</summary>
    public ResolvedOutline BorderLeft   { get; init; } = ResolvedOutline.None.Instance;
    /// <summary>Right border.</summary>
    public ResolvedOutline BorderRight  { get; init; } = ResolvedOutline.None.Instance;
    /// <summary>Top border.</summary>
    public ResolvedOutline BorderTop    { get; init; } = ResolvedOutline.None.Instance;
    /// <summary>Bottom border.</summary>
    public ResolvedOutline BorderBottom { get; init; } = ResolvedOutline.None.Instance;

    /// <summary>Text to render in this cell, or null if the cell is empty.</summary>
    public ResolvedTextLayout? Text { get; init; }

    /// <summary>Vertical anchor for the cell text.</summary>
    public TableCellAnchor Anchor { get; init; } = TableCellAnchor.Top;
}
