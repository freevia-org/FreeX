namespace Free.Shared.Drawing;

/// <summary>
/// The two halves of an OOXML <c>a:srcRect</c> placement: the sub-rectangle of the source bitmap
/// that is visible (in source pixels), plus the fraction of the destination frame that must be
/// left empty on each edge.
/// </summary>
/// <remarks>
/// Positive <c>a:srcRect</c> insets crop: the visible source region shrinks and still fills the
/// whole frame. Negative insets <em>outset</em>: the requested source region extends past the
/// bitmap, so the bitmap covers only part of the frame and the remainder is padding. Padding
/// cannot be expressed as a source rectangle at all, which is why it is reported separately as a
/// destination inset.
/// </remarks>
public readonly record struct SourceRectCropPlan(
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    double DestinationInsetLeft,
    double DestinationInsetTop,
    double DestinationInsetRight,
    double DestinationInsetBottom)
{
    /// <summary>True when the visible region is a strict sub-rectangle of the source bitmap.</summary>
    public bool HasSourceCrop { get; init; }

    /// <summary>True when the bitmap covers only part of the destination frame (negative insets).</summary>
    public bool HasDestinationInset =>
        DestinationInsetLeft > 0 ||
        DestinationInsetTop > 0 ||
        DestinationInsetRight > 0 ||
        DestinationInsetBottom > 0;

    /// <summary>True when the picture is cropped or padded in any way.</summary>
    public bool HasCrop => HasSourceCrop || HasDestinationInset;
}

/// <summary>
/// Renderer-neutral <c>a:srcRect</c> arithmetic shared by the on-screen picture planners and the
/// PDF writers so screen and print resolve the same geometry.
/// </summary>
public static class SourceRectCropGeometry
{
    /// <summary>
    /// Resolves <paramref name="left"/>/<paramref name="top"/>/<paramref name="right"/>/
    /// <paramref name="bottom"/> (signed fractions, OOXML <c>a:srcRect</c> semantics) against a
    /// bitmap of the given pixel size.
    /// </summary>
    public static SourceRectCropPlan Plan(
        int pixelWidth,
        int pixelHeight,
        double left,
        double top,
        double right,
        double bottom)
    {
        var width = Math.Max(1, pixelWidth);
        var height = Math.Max(1, pixelHeight);

        var horizontal = PlanAxis(width, left, right);
        var vertical = PlanAxis(height, top, bottom);

        return new SourceRectCropPlan(
            horizontal.Origin,
            vertical.Origin,
            horizontal.Extent,
            vertical.Extent,
            horizontal.InsetNear,
            vertical.InsetNear,
            horizontal.InsetFar,
            vertical.InsetFar)
        {
            HasSourceCrop =
                horizontal.Origin != 0 ||
                vertical.Origin != 0 ||
                horizontal.Extent != width ||
                vertical.Extent != height,
        };
    }

    private static (int Origin, int Extent, double InsetNear, double InsetFar) PlanAxis(
        int pixels,
        double near,
        double far)
    {
        near = Normalize(near);
        far = Normalize(far);

        // The source rectangle can only ever name pixels that exist, so the crop it carries is the
        // non-negative part of each inset. This keeps the historical (crop-only) results bit-exact.
        var croppedNear = Math.Max(0, near);
        var croppedFar = Math.Max(0, far);

        var origin = Math.Clamp((int)Math.Round(croppedNear * pixels), 0, pixels - 1);
        var extent = Math.Clamp(
            (int)Math.Round((1.0 - croppedNear - croppedFar) * pixels),
            1,
            pixels - origin);

        if (near >= 0 && far >= 0)
            return (origin, extent, 0, 0);

        // Negative inset: the frame spans source fractions [near, 1 - far], which is wider than the
        // bitmap. Map the pixels we do have back onto the frame to find the empty margins.
        var span = 1.0 - near - far;
        if (!double.IsFinite(span) || span <= 1e-9)
            return (origin, extent, 0, 0);

        var insetNear = Math.Clamp((origin / (double)pixels - near) / span, 0, 1);
        var insetFar = Math.Clamp(1.0 - ((origin + extent) / (double)pixels - near) / span, 0, 1);
        if (insetNear + insetFar >= 1)
            return (origin, extent, 0, 0);

        return (origin, extent, insetNear, insetFar);
    }

    private static double Normalize(double value) => double.IsFinite(value) ? value : 0.0;
}
