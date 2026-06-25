namespace FreeP.Core.Model;

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
    /// Two-stop linear gradient fill. The OOXML gradient model is richer; this covers the
    /// common two-stop case. <see cref="AngleDegrees"/> = 0 means left-to-right.
    /// </summary>
    public sealed class Gradient : ShapeFill
    {
        public ThemeAwareColor StartColor { get; }
        public ThemeAwareColor EndColor { get; }

        /// <summary>Gradient angle in degrees (0 = left→right, 90 = top→bottom).</summary>
        public double AngleDegrees { get; }

        public Gradient(ThemeAwareColor startColor, ThemeAwareColor endColor, double angleDegrees = 90)
        {
            StartColor = startColor;
            EndColor = endColor;
            AngleDegrees = angleDegrees;
        }
    }
}
