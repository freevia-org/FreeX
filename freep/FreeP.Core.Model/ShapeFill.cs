namespace FreeP.Core.Model;

/// <summary>
/// A single stop in a multi-stop gradient. Position is in [0,1] (0 = start, 1 = end).
/// </summary>
public sealed class GradientStop
{
    /// <summary>Stop position in [0, 1] (maps from OOXML pos 0..100000).</summary>
    public double Position { get; }

    /// <summary>Color at this stop.</summary>
    public ThemeAwareColor Color { get; }

    public GradientStop(double position, ThemeAwareColor color)
    {
        Position = Math.Clamp(position, 0.0, 1.0);
        Color = color;
    }
}

/// <summary>
/// Gradient kind: linear or radial (path=circle/rect).
/// </summary>
public enum GradientKind
{
    Linear = 0,
    Radial = 1
}

/// <summary>
/// Discriminated fill type for a <see cref="SlideShape"/>.
/// </summary>
public abstract class ShapeFill
{
    private ShapeFill() { }

    /// <summary>No fill (transparent interior).</summary>
    public sealed class None : ShapeFill { public static readonly None Instance = new(); private None() { } }

    /// <summary>Solid color fill.</summary>
    public sealed class Solid : ShapeFill
    {
        public ThemeAwareColor Color { get; }
        public Solid(ThemeAwareColor color) => Color = color;
        public Solid(SrgbColor color) => Color = new ThemeAwareColor(color);
    }

    /// <summary>
    /// Multi-stop gradient fill (2 or more stops). Supports linear and radial kinds.
    /// </summary>
    public sealed class Gradient : ShapeFill
    {
        /// <summary>All gradient stops in position order (position in [0,1]).</summary>
        public IReadOnlyList<GradientStop> Stops { get; }

        /// <summary>Gradient kind: Linear or Radial.</summary>
        public GradientKind Kind { get; }

        /// <summary>Gradient angle in degrees for linear gradients (0 = left→right, 90 = top→bottom).
        /// Stored as the OOXML ang value / 60000. Ignored for radial gradients.</summary>
        public double AngleDegrees { get; }

        // ── Back-compat convenience accessors ──────────────────────────────────────

        /// <summary>The first stop's color (back-compat with 2-stop callers).</summary>
        public ThemeAwareColor StartColor => Stops.Count > 0 ? Stops[0].Color : ThemeAwareColor.Black;

        /// <summary>The last stop's color (back-compat with 2-stop callers).</summary>
        public ThemeAwareColor EndColor => Stops.Count > 0 ? Stops[^1].Color : ThemeAwareColor.White;

        // ── Multi-stop constructor (primary) ───────────────────────────────────────

        public Gradient(IReadOnlyList<GradientStop> stops, GradientKind kind = GradientKind.Linear, double angleDegrees = 90)
        {
            Stops = stops ?? throw new ArgumentNullException(nameof(stops));
            Kind = kind;
            AngleDegrees = angleDegrees;
        }

        // ── Back-compat 2-stop constructor ─────────────────────────────────────────

        /// <summary>Creates a 2-stop linear gradient (back-compat with existing callers).</summary>
        public Gradient(ThemeAwareColor startColor, ThemeAwareColor endColor, double angleDegrees = 90)
            : this(new[]
            {
                new GradientStop(0.0, startColor),
                new GradientStop(1.0, endColor)
            }, GradientKind.Linear, angleDegrees)
        {
        }
    }

    /// <summary>
    /// Picture (blip) fill. The image is stretched or tiled to fill the shape.
    /// </summary>
    public sealed class Picture : ShapeFill
    {
        /// <summary>Raw image bytes.</summary>
        public byte[] ImageBytes { get; }

        /// <summary>MIME content type (e.g. "image/png", "image/jpeg").</summary>
        public string ContentType { get; }

        /// <summary>True = tile the image; false = stretch to fill.</summary>
        public bool Tile { get; }

        public Picture(byte[] imageBytes, string contentType, bool tile = false)
        {
            ImageBytes = imageBytes ?? throw new ArgumentNullException(nameof(imageBytes));
            ContentType = contentType ?? "image/png";
            Tile = tile;
        }
    }

    /// <summary>
    /// Pattern (hatch) fill — a preset DrawingML pattern with foreground and background colors.
    /// </summary>
    public sealed class Pattern : ShapeFill
    {
        /// <summary>OOXML preset pattern name (e.g. "pct50", "diagStripe", "cross", "dashDot").</summary>
        public string Preset { get; }

        /// <summary>Foreground color.</summary>
        public ThemeAwareColor ForegroundColor { get; }

        /// <summary>Background color.</summary>
        public ThemeAwareColor BackgroundColor { get; }

        public Pattern(string preset, ThemeAwareColor foregroundColor, ThemeAwareColor backgroundColor)
        {
            Preset = preset ?? throw new ArgumentNullException(nameof(preset));
            ForegroundColor = foregroundColor;
            BackgroundColor = backgroundColor;
        }
    }
}
