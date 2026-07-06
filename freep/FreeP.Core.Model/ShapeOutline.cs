namespace FreeP.Core.Model;

/// <summary>
/// Dash style for a shape outline. Matches OOXML <c>a:prstDash val="..."</c> presets.
/// </summary>
public enum OutlineDash
{
    Solid = 0,
    Dash = 1,
    Dot = 2,
    DashDot = 3,
    LongDash = 4,
    LongDashDot = 5,
    LongDashDotDot = 6,
    SystemDash = 7,
    SystemDot = 8,
    SystemDashDot = 9
}

/// <summary>
/// Bounded connector line-end marker kinds carried from DrawingML line properties.
/// </summary>
public enum ShapeLineEndKind
{
    Triangle = 1
}

/// <summary>
/// Metadata for an authored connector line-end marker.
/// </summary>
public sealed record ShapeLineEnd(ShapeLineEndKind Kind);

/// <summary>
/// Discriminated outline (border/stroke) type for a <see cref="SlideShape"/>.
/// </summary>
public abstract class ShapeOutline
{
    private ShapeOutline() { }

    /// <summary>No outline (invisible border).</summary>
    public sealed class None : ShapeOutline { public static readonly None Instance = new(); private None() { } }

    /// <summary>Visible outline with solid color, width, and dash style.</summary>
    public sealed class Visible : ShapeOutline
    {
        /// <summary>Stroke width in points (1 pt = 12700 EMU).</summary>
        public double WidthPt { get; }

        public OutlineDash Dash { get; }

        public ThemeAwareColor Color { get; }

        public ShapeLineEnd? BeginLineEnd { get; }

        public ShapeLineEnd? EndLineEnd { get; }

        public Visible(
            ThemeAwareColor color,
            double widthPt = 0.75,
            OutlineDash dash = OutlineDash.Solid,
            ShapeLineEnd? beginLineEnd = null,
            ShapeLineEnd? endLineEnd = null)
        {
            Color = color;
            WidthPt = widthPt;
            Dash = dash;
            BeginLineEnd = beginLineEnd;
            EndLineEnd = endLineEnd;
        }

        public Visible(
            SrgbColor color,
            double widthPt = 0.75,
            OutlineDash dash = OutlineDash.Solid,
            ShapeLineEnd? beginLineEnd = null,
            ShapeLineEnd? endLineEnd = null)
            : this(new ThemeAwareColor(color), widthPt, dash, beginLineEnd, endLineEnd) { }
    }

    // ── Wave 22B: gradient outline ─────────────────────────────────────────────

    /// <summary>
    /// Visible outline whose stroke is a gradient rather than a solid color.
    /// Corresponds to <c>a:ln</c> with a <c>a:gradFill</c> child.
    /// </summary>
    public sealed class GradientVisible : ShapeOutline
    {
        /// <summary>Stroke width in points.</summary>
        public double WidthPt { get; }

        public OutlineDash Dash { get; }

        /// <summary>Gradient specification reused from <see cref="ShapeFill.Gradient"/>.</summary>
        public ShapeFill.Gradient Gradient { get; }

        public ShapeLineEnd? BeginLineEnd { get; }

        public ShapeLineEnd? EndLineEnd { get; }

        public GradientVisible(
            ShapeFill.Gradient gradient,
            double widthPt = 0.75,
            OutlineDash dash = OutlineDash.Solid,
            ShapeLineEnd? beginLineEnd = null,
            ShapeLineEnd? endLineEnd = null)
        {
            Gradient = gradient ?? throw new ArgumentNullException(nameof(gradient));
            WidthPt = widthPt;
            Dash = dash;
            BeginLineEnd = beginLineEnd;
            EndLineEnd = endLineEnd;
        }
    }
}
