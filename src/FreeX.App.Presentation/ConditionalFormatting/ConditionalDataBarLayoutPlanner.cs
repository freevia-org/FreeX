using System;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Normalized geometry for a conditional-format data bar, expressed as fractions of the cell's
/// drawable content width together with the device-pixel insets that frame the bar. A renderer
/// scales <see cref="Start"/>/<see cref="End"/> by the inset-adjusted cell size to obtain the bar
/// rectangle; no framework rectangle type is involved.
/// </summary>
public readonly record struct ConditionalDataBarLayout(
    double Start,
    double End,
    double HorizontalInset,
    double VerticalInset)
{
    /// <summary>Extent of the bar as a fraction of the drawable width, always ≥ 0.</summary>
    public double FractionWidth => End - Start;
}

/// <summary>
/// Portable, single-source layout math for conditional-format data bars: clamps the rule's
/// start/end fractions to [0, 1], normalizes them so start ≤ end, and carries the horizontal /
/// vertical insets every shell uses to frame the bar within a cell. Pure decision logic with no
/// UI-framework dependencies, so it can be unit-tested and reused across hosts. This is the source
/// of truth previously inlined in the desktop data-bar renderer and re-declared in the
/// cross-platform port's <c>ConditionalFormatCellRenderPlanner</c>.
/// </summary>
public static class ConditionalDataBarLayoutPlanner
{
    /// <summary>Horizontal inset (device pixels at 100% zoom) of a data bar from the cell edges.</summary>
    public const double HorizontalInset = 2d;

    /// <summary>Vertical inset (device pixels at 100% zoom) of a data bar from the cell edges.</summary>
    public const double VerticalInset = 3d;

    /// <summary>
    /// Normalize the supplied start/end fractions into a data-bar layout, or <c>null</c> when the
    /// bar would have zero (or negative) width. Fractions are clamped to [0, 1] and swapped so that
    /// start ≤ end, matching the geometry both renderers draw.
    /// </summary>
    public static ConditionalDataBarLayout? Plan(double startFraction, double endFraction)
    {
        var start = Math.Clamp(startFraction, 0d, 1d);
        var end = Math.Clamp(endFraction, 0d, 1d);
        if (end < start)
            (start, end) = (end, start);

        if (end - start <= 0d)
            return null;

        return new ConditionalDataBarLayout(start, end, HorizontalInset, VerticalInset);
    }
}
