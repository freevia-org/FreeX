namespace FreeX.App.Presentation.Charts;

/// <summary>An automatically scaled value axis: bounds plus the major unit.</summary>
public readonly record struct AutoAxisRange(double Minimum, double Maximum, double MajorUnit, double RawMinimum, double RawMaximum);

/// <summary>
/// Microsoft Excel's automatic value-axis scaling, reverse engineered from real Excel (COM
/// <c>Axis.MinimumScale/MaximumScale/MajorUnit</c> of ~350 measured charts, line and column alike).
/// It is shared by every renderer so the two hosts agree with Excel instead of with their plotting
/// library's own auto-range:
/// <list type="number">
/// <item>When all data is on one side of zero and the spread is more than one sixth of the largest
/// magnitude (<c>(max-min)/max &gt; 16.667%</c>) the axis is anchored at zero. A narrower spread
/// keeps a non-zero baseline of <c>min - (max-min)/2</c>.</item>
/// <item>The far end is padded by 5%: <c>max + 0.05 * (max - axisMin)</c> for a zero-anchored axis,
/// <c>max + 0.05 * (max - min)</c> otherwise; mixed-sign data pads both ends by 5% of the spread.</item>
/// <item>The major unit is the smallest 1/2/5 x 10^n for which the padded range needs at most
/// <c>maxIntervals</c> intervals (10 for a tall/wide plot, fewer for small plots); the bounds are then
/// rounded out to whole major units.</item>
/// </list>
/// </summary>
public static class ExcelAxisAutoScale
{
    /// <summary>Most intervals Excel ever uses on one axis.</summary>
    public const int MaximumIntervals = 10;

    private const double DipPerPoint = 96.0 / 72.0;

    /// <summary>Excel keeps roughly one major interval per 14.3 pt of plot height.</summary>
    private const double PointsPerIntervalVertical = 14.3;

    /// <summary>Excel keeps roughly one major interval per ~36 pt of plot width (labels are wider than tall).</summary>
    private const double PointsPerIntervalHorizontal = 36.0;

    /// <summary>The interval budget for a vertical axis whose plot is <paramref name="plotHeightDip"/> tall.</summary>
    public static int MaxIntervalsForVerticalExtent(double plotHeightDip) =>
        ClampIntervals(plotHeightDip / DipPerPoint / PointsPerIntervalVertical);

    /// <summary>The interval budget for a horizontal axis whose plot is <paramref name="plotWidthDip"/> wide.</summary>
    public static int MaxIntervalsForHorizontalExtent(double plotWidthDip) =>
        ClampIntervals(plotWidthDip / DipPerPoint / PointsPerIntervalHorizontal);

    private static int ClampIntervals(double intervals)
    {
        if (double.IsNaN(intervals) || intervals >= MaximumIntervals)
            return MaximumIntervals;
        return Math.Max(1, (int)Math.Floor(intervals));
    }

    /// <summary>Computes Excel's automatic axis bounds and major unit for the data extent.</summary>
    public static AutoAxisRange Compute(double dataMin, double dataMax, int maxIntervals = MaximumIntervals)
    {
        if (double.IsNaN(dataMin) || double.IsInfinity(dataMin))
            dataMin = 0;
        if (double.IsNaN(dataMax) || double.IsInfinity(dataMax))
            dataMax = 0;
        if (dataMin > dataMax)
            (dataMin, dataMax) = (dataMax, dataMin);

        var intervals = Math.Clamp(maxIntervals, 1, MaximumIntervals);
        double rawMin;
        double rawMax;

        if (dataMin == 0 && dataMax == 0)
        {
            // An empty/all-zero chart shows 0..1.
            rawMin = 0;
            rawMax = 1;
        }
        else if (dataMin >= 0)
        {
            // A single value (or constant series) anchors at zero like a wide spread does.
            if (dataMin == 0 || dataMin == dataMax || (dataMax - dataMin) / dataMax > OneSixth)
            {
                rawMin = 0;
                rawMax = dataMax * 1.05;
            }
            else
            {
                var spread = dataMax - dataMin;
                rawMin = dataMin - (spread / 2);
                rawMax = dataMax + (0.05 * spread);
            }
        }
        else if (dataMax <= 0)
        {
            if (dataMax == 0 || dataMin == dataMax || (dataMin - dataMax) / dataMin > OneSixth)
            {
                rawMax = 0;
                rawMin = dataMin * 1.05;
            }
            else
            {
                var spread = dataMax - dataMin;
                rawMax = dataMax + (spread / 2);
                rawMin = dataMin - (0.05 * spread);
            }
        }
        else
        {
            var pad = 0.05 * (dataMax - dataMin);
            rawMin = dataMin - pad;
            rawMax = dataMax + pad;
        }

        var unit = ChooseMajorUnit(rawMin, rawMax, intervals);
        var minimum = Math.Floor((rawMin / unit) + Tolerance) * unit;
        var maximum = Math.Ceiling((rawMax / unit) - Tolerance) * unit;
        // Remove binary noise (0.30000000000000004) so bounds compare cleanly with Excel's values.
        minimum = Clean(minimum, unit);
        maximum = Clean(maximum, unit);
        if (maximum <= minimum)
            maximum = minimum + unit;

        return new AutoAxisRange(minimum, maximum, unit, rawMin, rawMax);
    }

    /// <summary>
    /// Major unit for an axis whose bounds are already fixed by the chart type (a 100% stacked axis runs
    /// exactly 0..100): the smallest 1/2/5 x 10^n unit that needs at most <paramref name="maxIntervals"/>
    /// intervals across <paramref name="minimum"/>..<paramref name="maximum"/> (Excel: 0%..100% by 10%).
    /// </summary>
    public static double ChooseMajorUnitForRange(double minimum, double maximum, int maxIntervals = MaximumIntervals) =>
        ChooseMajorUnit(minimum, maximum, Math.Clamp(maxIntervals, 1, MaximumIntervals));

    private const double Tolerance = 1e-9;

    // Excel treats an exact one-sixth spread as "narrow" (non-zero baseline); the margin guards
    // the floating-point representation of values such as 100 and 120.
    private const double OneSixth = (1.0 / 6.0) + 1e-12;

    private static double ChooseMajorUnit(double rawMin, double rawMax, int maxIntervals)
    {
        var range = rawMax - rawMin;
        if (!(range > 0))
            return 1;

        var exponent = Math.Floor(Math.Log10(range / maxIntervals)) - 1;
        for (var guard = 0; guard < 64; guard++, exponent++)
        {
            var decade = Math.Pow(10, exponent);
            foreach (var lead in Leads)
            {
                var unit = lead * decade;
                var count = Math.Ceiling((rawMax / unit) - Tolerance) - Math.Floor((rawMin / unit) + Tolerance);
                if (count <= maxIntervals)
                    return Clean(unit, decade);
            }
        }

        return range / maxIntervals;
    }

    private static readonly double[] Leads = [1, 2, 5];

    private static double Clean(double value, double unit)
    {
        var digits = (int)Math.Clamp(Math.Ceiling(-Math.Log10(Math.Abs(unit))) + 3, 0, 15);
        var rounded = Math.Round(value, digits);
        return rounded == 0 ? 0 : rounded;
    }
}
