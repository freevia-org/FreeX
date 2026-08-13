namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free helpers for the editor zoom control: clamping a zoom factor to the supported
/// range and stepping it up/down in fixed increments. Lives in the model project so the arithmetic
/// is unit-testable without WPF; the view (DocumentView) applies the resulting factor as a transform.
///
/// Zoom is expressed as a multiplier where 1.0 == 100%. The supported range is
/// <see cref="Min"/>..<see cref="Max"/> (50%..200%).
/// </summary>
public static class ZoomLevels
{
    /// <summary>Smallest supported zoom factor (50%).</summary>
    public const double Min = 0.5;

    /// <summary>Largest supported zoom factor (200%).</summary>
    public const double Max = 2.0;

    /// <summary>The default/unscaled zoom factor (100%).</summary>
    public const double Default = 1.0;

    /// <summary>Increment used when stepping zoom in or out via the +/- buttons (10%).</summary>
    public const double Step = 0.1;

    /// <summary>Clamp <paramref name="factor"/> into the supported <see cref="Min"/>..<see cref="Max"/> range.</summary>
    public static double Clamp(double factor)
    {
        if (double.IsNaN(factor))
            return Default;
        if (factor < Min)
            return Min;
        if (factor > Max)
            return Max;
        return factor;
    }

    /// <summary>The next zoom factor one <see cref="Step"/> above <paramref name="factor"/>, clamped to the range.</summary>
    public static double StepUp(double factor) => Clamp(Clamp(factor) + Step);

    /// <summary>The next zoom factor one <see cref="Step"/> below <paramref name="factor"/>, clamped to the range.</summary>
    public static double StepDown(double factor) => Clamp(Clamp(factor) - Step);

    /// <summary>Convert a zoom factor (1.0 == 100%) to a whole-number percentage for display.</summary>
    public static int ToPercent(double factor) => (int)System.Math.Round(Clamp(factor) * 100.0);

    /// <summary>Format a zoom factor as the canonical whole-number percentage shown by editor chrome.</summary>
    public static string FormatPercent(double factor) => $"{ToPercent(factor)}%";

    /// <summary>Convert a whole-number percentage to a clamped zoom factor (100 -> 1.0).</summary>
    public static double FromPercent(double percent) => Clamp(percent / 100.0);
}
