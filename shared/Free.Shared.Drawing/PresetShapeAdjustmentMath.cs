namespace Free.Shared.Drawing;

/// <summary>
/// Shared DrawingML preset-adjustment formulas used by geometry and editing surfaces.
/// </summary>
public static class PresetShapeAdjustmentMath
{
    private const double DrawingMlAdjustmentScale = 100000.0;
    private const double MaximumRoundedRectangleAdjustment = 50000.0;

    /// <summary>
    /// Resolves a rounded rectangle's corner radius from the shorter side and optional authored
    /// DrawingML adjustment. A null adjustment retains the established fixed fallback band.
    /// </summary>
    public static double RoundedRectangleCornerRadius(
        double minimumDimension,
        double? adjustment)
    {
        if (adjustment is null)
            return Math.Clamp(minimumDimension * 0.18, 2, 18);

        return minimumDimension *
            Math.Clamp(adjustment.Value, 0, MaximumRoundedRectangleAdjustment) /
            DrawingMlAdjustmentScale;
    }

    /// <summary>
    /// Resolves the normalized top edge of an authored ribbon's center band.
    /// </summary>
    public static double RibbonBandTop(double foldAdjustment) =>
        Math.Clamp(foldAdjustment / DrawingMlAdjustmentScale, 0.04, 0.45);
}
