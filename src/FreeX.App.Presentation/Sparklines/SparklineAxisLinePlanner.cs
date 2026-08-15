using FreeX.Core.Model;

namespace FreeX.App.Presentation.Sparklines;

/// <summary>
/// Resolves the renderer-neutral zero-axis position for an in-cell sparkline.
/// </summary>
public static class SparklineAxisLinePlanner
{
    private const double Epsilon = 0.0000001;

    /// <summary>
    /// Returns the Y coordinate of the visible zero axis, or <see langword="null"/> when a line
    /// sparkline's plotted range does not contain zero. Column sparklines use the same baseline
    /// policy as <see cref="SparklineLayoutEngine"/>: bottom for positive-only data, top for
    /// negative-only data, and the midpoint for mixed-sign or win/loss data.
    /// </summary>
    public static double? ResolveY(
        SparklineKind kind,
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMinimum = null,
        double? overrideMaximum = null)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        return kind == SparklineKind.Line
            ? ResolveLineY(values, rect, overrideMinimum, overrideMaximum)
            : ResolveColumnY(values, rect, kind == SparklineKind.WinLoss);
    }

    private static double? ResolveLineY(
        IReadOnlyList<double> values,
        LayoutRect rect,
        double? overrideMinimum,
        double? overrideMaximum)
    {
        double? minimum = null;
        double? maximum = null;
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                continue;

            if (minimum is null || value < minimum)
                minimum = value;
            if (maximum is null || value > maximum)
                maximum = value;
        }

        if (minimum is null || maximum is null)
            return null;

        var low = overrideMinimum is { } requestedMinimum && double.IsFinite(requestedMinimum)
            ? requestedMinimum
            : minimum.Value;
        var high = overrideMaximum is { } requestedMaximum && double.IsFinite(requestedMaximum)
            ? requestedMaximum
            : maximum.Value;
        if (low > high)
            (low, high) = (high, low);

        if (low > 0 || high < 0)
            return null;

        var span = Math.Abs(high - low) < Epsilon ? 1 : high - low;
        return rect.Bottom - (Math.Clamp((0 - low) / span, 0, 1) * rect.Height);
    }

    private static double ResolveColumnY(
        IReadOnlyList<double> values,
        LayoutRect rect,
        bool winLoss)
    {
        var hasPositive = false;
        var hasNegative = false;
        if (!winLoss)
        {
            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                    continue;

                if (value > 0)
                    hasPositive = true;
                else if (value < 0)
                    hasNegative = true;
            }
        }

        if (hasPositive && !hasNegative)
            return rect.Bottom;
        if (hasNegative && !hasPositive)
            return rect.Top;
        return rect.Top + (rect.Height / 2);
    }
}
