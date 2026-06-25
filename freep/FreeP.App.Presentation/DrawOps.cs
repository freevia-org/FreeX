using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

// ─── Resolved text types ──────────────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>Two-stop linear gradient.</summary>
    public sealed class Gradient : ResolvedFill
    {
        public SrgbColor StartColor { get; }
        public SrgbColor EndColor { get; }
        /// <summary>Angle in degrees (0 = left->right, 90 = top->bottom).</summary>
        public double AngleDegrees { get; }
        public Gradient(SrgbColor startColor, SrgbColor endColor, double angleDegrees)
        {
            StartColor = startColor;
            EndColor = endColor;
            AngleDegrees = angleDegrees;
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
