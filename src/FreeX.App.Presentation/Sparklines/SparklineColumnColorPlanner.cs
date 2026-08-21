using FreeX.Core.Model;

namespace FreeX.App.Presentation.Sparklines;

/// <summary>
/// Resolves one fill color for every visible bar of a Column or Win/Loss sparkline.
/// The result is ordered exactly like <see cref="SparklineLayoutEngine.CalculateColumnLayout"/>
/// (finite, nonzero input values in source order), so host renderers can apply the authored
/// high/low/first/last point colors without duplicating value-role precedence.
/// </summary>
public static class SparklineColumnColorPlanner
{
    private const double Epsilon = 0.0000001;

    /// <summary>
    /// Resolves colors with Excel's point precedence: series, negative, first, last, low, then high.
    /// High and low point colors therefore override a negative color when the same visible bar has
    /// both roles. The planner is intentionally limited to Column/WinLoss; line markers keep their
    /// existing marker-dot rendering path.
    /// </summary>
    public static IReadOnlyList<CellColor> ResolveBarColors(
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        CellColor seriesColor,
        CellColor negativeColor,
        CellColor highColor,
        CellColor lowColor,
        CellColor firstColor,
        CellColor lastColor)
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        ArgumentNullException.ThrowIfNull(values);
        if (sparkline.Kind == SparklineKind.Line)
            throw new ArgumentException("Column bar colors are not used for line sparklines.", nameof(sparkline));

        var firstFiniteIndex = -1;
        var lastFiniteIndex = -1;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!double.IsFinite(value))
                continue;

            if (firstFiniteIndex < 0)
                firstFiniteIndex = index;
            lastFiniteIndex = index;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        var colors = new List<CellColor>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!double.IsFinite(value) || Math.Abs(value) < Epsilon)
                continue;

            var color = seriesColor;
            if (sparkline.ShowNegativePoints && value < 0)
                color = negativeColor;
            if (sparkline.ShowFirstPoint && index == firstFiniteIndex)
                color = firstColor;
            if (sparkline.ShowLastPoint && index == lastFiniteIndex)
                color = lastColor;
            if (sparkline.ShowLowPoint && Math.Abs(value - minimum) < Epsilon)
                color = lowColor;
            if (sparkline.ShowHighPoint && Math.Abs(value - maximum) < Epsilon)
                color = highColor;

            colors.Add(color);
        }

        return colors;
    }
}
