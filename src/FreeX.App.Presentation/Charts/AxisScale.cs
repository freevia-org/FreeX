namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Which end of the plot rectangle an axis runs along. Mirrors the four axis positions the source
/// renderer uses (bottom/left primary, top/right secondary).
/// </summary>
public enum AxisSide
{
    Bottom,
    Left,
    Top,
    Right
}

/// <summary>
/// A linear value→pixel scale for one chart axis, plus the "nice" tick positions. This is the
/// portable equivalent of the layout math the source renderer delegates to its plotting library:
/// it pads the data range, computes a round major step, and maps data values onto the plot
/// rectangle's screen extent.
/// </summary>
public sealed class AxisScale
{
    /// <summary>Data-space minimum actually used for the scale (after padding / explicit bounds).</summary>
    public double Minimum { get; }

    /// <summary>Data-space maximum actually used for the scale.</summary>
    public double Maximum { get; }

    /// <summary>The major tick interval in data space.</summary>
    public double MajorStep { get; }

    /// <summary>Pixel coordinate corresponding to <see cref="Minimum"/>.</summary>
    public double ScreenMin { get; }

    /// <summary>Pixel coordinate corresponding to <see cref="Maximum"/>.</summary>
    public double ScreenMax { get; }

    /// <summary>Whether the axis is vertical (value grows upward on screen) versus horizontal.</summary>
    public bool IsVertical { get; }

    /// <summary>
    /// Whether this is a logarithmic axis: <see cref="Transform"/>/<see cref="InverseTransform"/> map
    /// through log-space and <see cref="GetMajorTickValues"/> returns one tick per decade (power of
    /// <see cref="LogBase"/>) instead of a linear step.
    /// </summary>
    public bool IsLogarithmic { get; }

    /// <summary>The logarithm base for a logarithmic axis (Excel default 10). Unused for linear axes.</summary>
    public double LogBase { get; }

    private AxisScale(
        double minimum,
        double maximum,
        double majorStep,
        double screenMin,
        double screenMax,
        bool isVertical,
        bool isLogarithmic = false,
        double logBase = 10)
    {
        Minimum = minimum;
        Maximum = maximum;
        MajorStep = majorStep;
        ScreenMin = screenMin;
        ScreenMax = screenMax;
        IsVertical = isVertical;
        IsLogarithmic = isLogarithmic;
        LogBase = logBase;
    }

    /// <summary>
    /// Builds a value axis from a data range and the plot rectangle. <paramref name="side"/>
    /// determines orientation and screen extent: bottom/top map onto X (left→right), left/right map
    /// onto Y (bottom→top, i.e. larger values nearer the plot top). Explicit bounds/step from the
    /// chart model override the auto-computed values when supplied.
    /// </summary>
    public static AxisScale CreateValueAxis(
        double dataMin,
        double dataMax,
        PlotRect plot,
        AxisSide side,
        double? explicitMin = null,
        double? explicitMax = null,
        double? explicitStep = null,
        int targetTickCount = 7,
        bool reverseOrder = false,
        bool includeZeroBaseline = true)
    {
        var (min, max) = NormalizeRange(dataMin, dataMax, includeZeroBaseline);

        // Pad the range to a round boundary unless the caller pinned the bound, matching the
        // source renderer's auto-fit behaviour (extend out to the next major step).
        var step = explicitStep is { } s && s > 0
            ? s
            : CalculateNiceStep(max - min, targetTickCount);

        var actualMin = explicitMin ?? FloorToStep(min, step);
        var actualMax = explicitMax ?? CeilingToStep(max, step);
        if (actualMax <= actualMin)
            actualMax = actualMin + step;

        var isVertical = side is AxisSide.Left or AxisSide.Right;
        double screenMin;
        double screenMax;
        if (isVertical)
        {
            // Larger values render nearer the top of the plot (smaller Y).
            screenMin = plot.Bottom;
            screenMax = plot.Top;
        }
        else
        {
            screenMin = plot.Left;
            screenMax = plot.Right;
        }

        // Excel's "Values in reverse order" (OOXML scaling orientation="maxMin") flips which screen
        // edge the minimum/maximum map onto — mirrors the WPF renderer's StartPosition/EndPosition
        // swap (ChartRenderer.Axes.cs ApplyAxisReverseOrder) so bars/lines/gridlines all reverse.
        if (reverseOrder)
            (screenMin, screenMax) = (screenMax, screenMin);

        return new AxisScale(actualMin, actualMax, step, screenMin, screenMax, isVertical);
    }

    /// <summary>
    /// Builds a logarithmic value axis from a data range and the plot rectangle. Mirrors Excel's
    /// log-scale axis: the range is expressed in log space (one major tick per decade — i.e. per
    /// power of <paramref name="logBase"/>), non-positive data is ignored (log of a non-positive
    /// value is undefined), and <see cref="Transform"/>/<see cref="InverseTransform"/> map through
    /// <c>log(value)</c> rather than linearly. When the data contains no positive values the axis
    /// falls back to the range [1, <paramref name="logBase"/>] so the chart still renders.
    /// </summary>
    public static AxisScale CreateLogValueAxis(
        double dataMin,
        double dataMax,
        PlotRect plot,
        AxisSide side,
        double? explicitMin = null,
        double? explicitMax = null,
        double? logBase = null,
        bool reverseOrder = false)
    {
        var effectiveBase = logBase is { } b && b > 1 ? b : 10;

        // Only positive values are meaningful on a log axis; non-positive data is guarded out.
        var positiveMin = dataMin > 0 ? dataMin : double.NaN;
        var positiveMax = dataMax > 0 ? dataMax : double.NaN;
        if (double.IsNaN(positiveMin) && double.IsNaN(positiveMax))
        {
            positiveMin = 1;
            positiveMax = effectiveBase;
        }
        else if (double.IsNaN(positiveMin))
        {
            positiveMin = positiveMax;
        }
        else if (double.IsNaN(positiveMax))
        {
            positiveMax = positiveMin;
        }

        if (positiveMin > positiveMax)
            (positiveMin, positiveMax) = (positiveMax, positiveMin);

        var actualMin = explicitMin is { } eMin && eMin > 0 ? eMin : positiveMin;
        var actualMax = explicitMax is { } eMax && eMax > 0 ? eMax : positiveMax;
        if (actualMax <= actualMin)
            actualMax = actualMin * effectiveBase;

        // Snap the bounds out to whole decades (powers of the log base) so major ticks land on
        // round values (1, 10, 100, ... for base 10), matching Excel's auto-scaled log axis.
        var logMin = Math.Log(actualMin, effectiveBase);
        var logMax = Math.Log(actualMax, effectiveBase);
        var decadeMin = explicitMin is { } ? logMin : Math.Floor(logMin + 1e-9);
        var decadeMax = explicitMax is { } ? logMax : Math.Ceiling(logMax - 1e-9);
        if (decadeMax <= decadeMin)
            decadeMax = decadeMin + 1;

        actualMin = Math.Pow(effectiveBase, decadeMin);
        actualMax = Math.Pow(effectiveBase, decadeMax);

        var isVertical = side is AxisSide.Left or AxisSide.Right;
        double screenMin;
        double screenMax;
        if (isVertical)
        {
            screenMin = plot.Bottom;
            screenMax = plot.Top;
        }
        else
        {
            screenMin = plot.Left;
            screenMax = plot.Right;
        }

        if (reverseOrder)
            (screenMin, screenMax) = (screenMax, screenMin);

        // MajorStep is expressed in log space (one decade) for GetMajorTickValues' benefit.
        return new AxisScale(actualMin, actualMax, 1, screenMin, screenMax, isVertical, isLogarithmic: true, logBase: effectiveBase);
    }

    /// <summary>
    /// Builds a category index axis. Column/line/area charts plot points at integer category
    /// indices; this maps the index range [min, max] onto the plot extent. For column charts the
    /// source renderer centers categories by spanning [-0.5, count-0.5]; for line/area it spans
    /// [0, count-1].
    /// </summary>
    public static AxisScale CreateIndexAxis(double indexMin, double indexMax, PlotRect plot, AxisSide side)
    {
        if (indexMax <= indexMin)
            indexMax = indexMin + 1;

        var isVertical = side is AxisSide.Left or AxisSide.Right;
        double screenMin;
        double screenMax;
        if (isVertical)
        {
            screenMin = plot.Bottom;
            screenMax = plot.Top;
        }
        else
        {
            screenMin = plot.Left;
            screenMax = plot.Right;
        }

        return new AxisScale(indexMin, indexMax, 1, screenMin, screenMax, isVertical);
    }

    /// <summary>Maps a data value to a pixel coordinate along this axis.</summary>
    public double Transform(double value)
    {
        if (IsLogarithmic)
        {
            // Non-positive values have no position on a log axis; clamp to the axis minimum so
            // callers get a finite (if visually clipped) coordinate instead of NaN/-Infinity.
            var safeValue = value > 0 ? value : Minimum;
            var logMin = Math.Log(Minimum, LogBase);
            var logMax = Math.Log(Maximum, LogBase);
            var logSpan = logMax - logMin;
            if (Math.Abs(logSpan) < double.Epsilon)
                return ScreenMin;

            var logT = (Math.Log(safeValue, LogBase) - logMin) / logSpan;
            return ScreenMin + (logT * (ScreenMax - ScreenMin));
        }

        var span = Maximum - Minimum;
        if (Math.Abs(span) < double.Epsilon)
            return ScreenMin;

        var t = (value - Minimum) / span;
        return ScreenMin + (t * (ScreenMax - ScreenMin));
    }

    /// <summary>Maps a pixel coordinate back to a data value (inverse of <see cref="Transform"/>).</summary>
    public double InverseTransform(double screen)
    {
        var screenSpan = ScreenMax - ScreenMin;
        if (Math.Abs(screenSpan) < double.Epsilon)
            return Minimum;

        var t = (screen - ScreenMin) / screenSpan;

        if (IsLogarithmic)
        {
            var logMin = Math.Log(Minimum, LogBase);
            var logMax = Math.Log(Maximum, LogBase);
            return Math.Pow(LogBase, logMin + (t * (logMax - logMin)));
        }

        return Minimum + (t * (Maximum - Minimum));
    }

    /// <summary>
    /// Enumerates the major tick values from <see cref="Minimum"/> to <see cref="Maximum"/>
    /// inclusive, stepping by <see cref="MajorStep"/>. Values are snapped to the step grid to avoid
    /// floating-point drift accumulating across many ticks.
    /// </summary>
    public IReadOnlyList<double> GetMajorTickValues()
    {
        var ticks = new List<double>();

        if (IsLogarithmic)
        {
            // One major tick per decade: LogBase^Minimum-power .. LogBase^Maximum-power.
            var logMin = Math.Round(Math.Log(Minimum, LogBase));
            var logMax = Math.Round(Math.Log(Maximum, LogBase));
            for (var power = logMin; power <= logMax + 1e-9; power++)
                ticks.Add(Math.Pow(LogBase, power));
            return ticks;
        }

        if (MajorStep <= 0)
        {
            ticks.Add(Minimum);
            return ticks;
        }

        // Snap the first tick onto the step grid at or above the minimum.
        var firstIndex = Math.Ceiling(Minimum / MajorStep - 1e-9);
        var count = (int)Math.Floor((Maximum / MajorStep) - firstIndex + 1e-9) + 1;
        for (var i = 0; i < count; i++)
        {
            var value = (firstIndex + i) * MajorStep;
            // Clamp tiny negative/positive zero noise to exactly zero.
            if (Math.Abs(value) < MajorStep * 1e-10)
                value = 0;
            ticks.Add(value);
        }

        return ticks;
    }

    private static (double Min, double Max) NormalizeRange(double dataMin, double dataMax, bool includeZeroBaseline)
    {
        if (double.IsNaN(dataMin) || double.IsInfinity(dataMin))
            dataMin = 0;
        if (double.IsNaN(dataMax) || double.IsInfinity(dataMax))
            dataMax = 0;

        if (dataMin > dataMax)
            (dataMin, dataMax) = (dataMax, dataMin);

        // The source renderer baselines value axes at zero unless the data goes negative, so bars
        // (and filled areas, which shade down to the zero baseline) grow from the zero line. Include
        // zero in the range when all data is on one side of it. Series with no zero-anchored geometry
        // (Line/Scatter/Bubble) pass includeZeroBaseline: false so they instead auto-fit tight to the
        // actual data extents, matching OxyPlot's own LineSeries/ScatterSeries auto-range (no baseline)
        // used by the WPF renderer for those chart types.
        if (includeZeroBaseline)
        {
            if (dataMin > 0)
                dataMin = 0;
            if (dataMax < 0)
                dataMax = 0;
        }

        if (Math.Abs(dataMax - dataMin) < double.Epsilon)
            dataMax = dataMin + 1;

        return (dataMin, dataMax);
    }

    /// <summary>
    /// Computes a "nice" major step: the smallest value of the form 1/2/5 × 10^n that splits the
    /// range into roughly <paramref name="targetTickCount"/> intervals. This is the standard
    /// round-number tick algorithm the source renderer's plotting library uses.
    /// </summary>
    public static double CalculateNiceStep(double range, int targetTickCount)
    {
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range))
            return 1;

        var ticks = Math.Max(1, targetTickCount);
        var rawStep = range / ticks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var normalized = rawStep / magnitude; // in [1, 10)

        double niceNormalized;
        if (normalized < 1.5)
            niceNormalized = 1;
        else if (normalized < 3)
            niceNormalized = 2;
        else if (normalized < 7)
            niceNormalized = 5;
        else
            niceNormalized = 10;

        return niceNormalized * magnitude;
    }

    private static double FloorToStep(double value, double step) =>
        step <= 0 ? value : Math.Floor(value / step + 1e-9) * step;

    private static double CeilingToStep(double value, double step) =>
        step <= 0 ? value : Math.Ceiling(value / step - 1e-9) * step;
}
