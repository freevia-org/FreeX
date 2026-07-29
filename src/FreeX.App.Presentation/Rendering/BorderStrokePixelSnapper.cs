namespace FreeX.App.Presentation.Rendering;

/// <summary>
/// Converts authored spreadsheet border strokes to deterministic device-pixel geometry.
/// </summary>
public static class BorderStrokePixelSnapper
{
    public static int SnapThicknessToDevicePixels(double thicknessDip, double effectivePixelsPerDip)
    {
        var scale = NormalizePixelsPerDip(effectivePixelsPerDip);
        if (!double.IsFinite(thicknessDip) || thicknessDip <= 0)
            return 0;

        return Math.Max(1, (int)Math.Round(thicknessDip * scale, MidpointRounding.AwayFromZero));
    }

    public static double SnapThickness(double thicknessDip, double effectivePixelsPerDip)
    {
        var scale = NormalizePixelsPerDip(effectivePixelsPerDip);
        var devicePixels = SnapThicknessToDevicePixels(thicknessDip, scale);
        return devicePixels <= 0 ? 0 : devicePixels / scale;
    }

    public static double SnapCenter(double centerDip, double snappedThicknessDip, double effectivePixelsPerDip)
    {
        var scale = NormalizePixelsPerDip(effectivePixelsPerDip);
        if (!double.IsFinite(centerDip) || !double.IsFinite(snappedThicknessDip) || snappedThicknessDip <= 0)
            return centerDip;

        var centerPx = centerDip * scale;
        var halfThicknessPx = snappedThicknessDip * scale / 2.0;
        var snappedLeadingEdgePx = Math.Round(centerPx - halfThicknessPx, MidpointRounding.AwayFromZero);
        return (snappedLeadingEdgePx + halfThicknessPx) / scale;
    }

    public static double NormalizePixelsPerDip(double effectivePixelsPerDip) =>
        double.IsFinite(effectivePixelsPerDip) && effectivePixelsPerDip > 0
            ? effectivePixelsPerDip
            : 1.0;
}
