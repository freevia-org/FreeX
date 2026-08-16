namespace Free.Shared.Pdf;

/// <summary>
/// Renderer-neutral affine-transform and page-coordinate rules shared by PDF adapters.
/// </summary>
public static class PdfTransformMath
{
    /// <summary>
    /// Returns whether every component of a two-dimensional affine matrix is finite.
    /// </summary>
    public static bool IsFiniteAffineMatrix(
        double m11,
        double m12,
        double m21,
        double m22,
        double offsetX,
        double offsetY) =>
        double.IsFinite(m11) &&
        double.IsFinite(m12) &&
        double.IsFinite(m21) &&
        double.IsFinite(m22) &&
        double.IsFinite(offsetX) &&
        double.IsFinite(offsetY);

    /// <summary>
    /// Estimates a uniform scale from the average magnitudes of an affine matrix's two axes.
    /// Degenerate, non-finite, and overflowed matrices resolve to <paramref name="fallbackScale"/>.
    /// </summary>
    public static double EstimateUniformScale(
        double m11,
        double m12,
        double m21,
        double m22,
        double fallbackScale = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fallbackScale);
        if (!double.IsFinite(fallbackScale))
            throw new ArgumentOutOfRangeException(nameof(fallbackScale), fallbackScale, "Fallback scale must be finite.");

        var scaleX = Math.Sqrt(m11 * m11 + m12 * m12);
        var scaleY = Math.Sqrt(m21 * m21 + m22 * m22);
        var scale = (scaleX + scaleY) / 2.0;
        return double.IsFinite(scale) && scale > 0 ? scale : fallbackScale;
    }

    /// <summary>
    /// Maps an unset canvas coordinate (represented by <see cref="double.NaN"/>) to the canvas
    /// origin while preserving explicitly supplied coordinates.
    /// </summary>
    public static double ResolveCanvasCoordinate(double coordinate) =>
        double.IsNaN(coordinate) ? 0 : coordinate;
}
