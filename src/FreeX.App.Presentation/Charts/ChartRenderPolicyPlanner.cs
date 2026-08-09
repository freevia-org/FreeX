using System.Globalization;
using System.Xml.Linq;

using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Portable chart-rendering decisions shared by the layout engine and native renderers. The
/// planner deliberately returns model values and plain geometry only; hosts remain responsible for
/// brushes, drawing APIs, text metrics, and framework-specific geometry materialization.
/// </summary>
public static class ChartRenderPolicyPlanner
{
    private const double DefaultBarHalfWidth = 0.35;
    private const double MaximumBubbleRadius = 20.0;
    private const double MinimumBubbleRadius = 1.0;

    public static CellColor WaterfallIncreaseColor { get; } = new(0x54, 0x82, 0x35);
    public static CellColor WaterfallDecreaseColor { get; } = new(0xC0, 0x00, 0x00);
    public static CellColor WaterfallTotalColor { get; } = new(0x44, 0x72, 0xC4);

    public sealed record BoxAndWhiskerStatistics(
        double LowerWhisker,
        double FirstQuartile,
        double Median,
        double ThirdQuartile,
        double UpperWhisker,
        IReadOnlyList<double> Outliers);

    public readonly record struct ErrorBarAmounts(double Plus, double Minus);

    /// <summary>Coerces a chart cell to a finite numeric value using the shared host policy.</summary>
    public static bool TryGetNumericValue(ScalarValue? rawValue, string displayText, out double value)
    {
        switch (rawValue)
        {
            case NumberValue number:
                value = number.Value;
                return double.IsFinite(value);
            case DateTimeValue dateTime:
                value = dateTime.Value;
                return double.IsFinite(value);
            case BoolValue boolean:
                value = boolean.Value ? 1 : 0;
                return true;
        }

        return double.TryParse(displayText, NumberStyles.Any, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);
    }

    /// <summary>Applies an authored category-axis number format while retaining display fallback.</summary>
    public static string FormatCategoryLabel(
        ChartModel chart,
        ScalarValue? rawValue,
        string displayText)
    {
        var formatCode = chart.XAxisNumberFormatCode;
        if (string.IsNullOrWhiteSpace(formatCode) ||
            formatCode.Equals("General", StringComparison.OrdinalIgnoreCase) ||
            (rawValue is not NumberValue && rawValue is not DateTimeValue))
        {
            return displayText;
        }

        try
        {
            var formatted = NumberFormatter.Format(rawValue, formatCode, chart.Uses1904DateSystem);
            return string.IsNullOrEmpty(formatted) ? displayText : formatted;
        }
        catch
        {
            return displayText;
        }
    }

    /// <summary>Resolves proportional date-axis positions using the shared OLE Automation scale.</summary>
    public static bool TryResolveDateCategoryPositions(
        ChartModel chart,
        IReadOnlyList<string> categories,
        out double[] positions,
        out double minimum,
        out double maximum)
    {
        positions = [];
        minimum = 0;
        maximum = 0;
        if (!chart.XAxisIsDateAxis || categories.Count == 0)
            return false;

        var values = new double[categories.Count];
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var index = 0; index < categories.Count; index++)
        {
            if (!TryParseDateCategory(categories[index], out var parsed))
                return false;

            var value = parsed.Date.ToOADate();
            values[index] = value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        positions = values;
        minimum = min;
        maximum = max;
        return true;
    }

    /// <summary>Parses an authored date category using invariant then current culture.</summary>
    public static bool TryParseDateCategory(string category, out DateTime value) =>
        DateTime.TryParse(
            category,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out value) ||
        DateTime.TryParse(
            category,
            CultureInfo.CurrentCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out value);

    /// <summary>Flips an axis edge only for Excel's explicit crosses-at-maximum policy.</summary>
    public static AxisSide ResolveAxisSide(AxisSide side, ChartAxisCrosses crosses)
    {
        if (crosses != ChartAxisCrosses.Maximum)
            return side;

        return side switch
        {
            AxisSide.Bottom => AxisSide.Top,
            AxisSide.Top => AxisSide.Bottom,
            AxisSide.Left => AxisSide.Right,
            AxisSide.Right => AxisSide.Left,
            _ => side,
        };
    }

    /// <summary>Resolves Excel's authored display-unit divisor.</summary>
    public static double? ResolveAxisDisplayUnitDivisor(
        ChartAxisDisplayUnit? unit,
        double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom;

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => 1e2,
            ChartAxisDisplayUnit.Thousands => 1e3,
            ChartAxisDisplayUnit.TenThousands => 1e4,
            ChartAxisDisplayUnit.HundredThousands => 1e5,
            ChartAxisDisplayUnit.Millions => 1e6,
            ChartAxisDisplayUnit.TenMillions => 1e7,
            ChartAxisDisplayUnit.HundredMillions => 1e8,
            ChartAxisDisplayUnit.Billions => 1e9,
            ChartAxisDisplayUnit.Trillions => 1e12,
            _ => null,
        };
    }

    /// <summary>Resolves the display-unit text used by axis-title materializers.</summary>
    public static string ResolveAxisDisplayUnitLabel(
        ChartAxisDisplayUnit? unit,
        double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom.ToString("0.###", CultureInfo.InvariantCulture);

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => "Hundreds",
            ChartAxisDisplayUnit.Thousands => "Thousands",
            ChartAxisDisplayUnit.TenThousands => "Ten Thousands",
            ChartAxisDisplayUnit.HundredThousands => "Hundred Thousands",
            ChartAxisDisplayUnit.Millions => "Millions",
            ChartAxisDisplayUnit.TenMillions => "Ten Millions",
            ChartAxisDisplayUnit.HundredMillions => "Hundred Millions",
            ChartAxisDisplayUnit.Billions => "Billions",
            ChartAxisDisplayUnit.Trillions => "Trillions",
            _ => "",
        };
    }

    /// <summary>Resolves the half-width of a column/bar category slot from Excel's gap width.</summary>
    public static double ResolveBarHalfWidth(ChartModel chart) =>
        chart.BarGapWidth is int gapWidth
            ? Math.Clamp(0.5 * 100.0 / (100.0 + gapWidth), 0.05, 0.5)
            : DefaultBarHalfWidth;

    /// <summary>Resolves the native default overlap when the workbook omits an explicit value.</summary>
    public static int ResolveEffectiveBarOverlap(ChartModel chart) =>
        chart.BarOverlap ?? (chart.Type is ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
                ? -27
                : 0);

    /// <summary>Returns one clustered series' left/right offsets inside a category slot.</summary>
    public static (double Left, double Right) ResolveClusteredBarOffsets(
        double halfWidth,
        int clusterOrdinal,
        int clusterCount,
        int overlapPercent)
    {
        if (clusterCount <= 1)
            return (-halfWidth, halfWidth);

        var overlap = Math.Clamp(overlapPercent, -100, 100) / 100.0;
        var denominator = clusterCount - (overlap * (clusterCount - 1));
        var unitWidth = Math.Abs(denominator) < 1e-9
            ? 2.0 * halfWidth
            : 2.0 * halfWidth / denominator;
        var step = unitWidth * (1 - overlap);
        var left = -halfWidth + clusterOrdinal * step;
        return (left, left + unitWidth);
    }

    /// <summary>Returns whether a physical source column is the scatter family's shared X column.</summary>
    public static bool ShouldSkipSourceColumn(ChartModel chart, uint column, uint dataStartColumn) =>
        chart.Type == ChartType.Scatter &&
        !chart.FirstColIsCategories &&
        column == dataStartColumn;

    /// <summary>Transposes a source or virtual cell coordinate around a range's fixed origin.</summary>
    public static (uint Row, uint Column) TransposeCoordinate(
        uint row,
        uint column,
        uint startRow,
        uint startColumn) =>
        (startRow + (column - startColumn), startColumn + (row - startRow));

    /// <summary>Resolves a transposed range's inclusive end coordinate.</summary>
    public static (uint EndRow, uint EndColumn) ResolveTransposedEnd(
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        (startRow + (endColumn - startColumn), startColumn + (endRow - startRow));

    /// <summary>
    /// Returns true when every authored series maps to a physical value column in the current data
    /// span. Row-major and transposed series intentionally fall back to positional extraction.
    /// </summary>
    public static bool HasAuthoritativeSeriesColumns(
        ChartModel chart,
        uint dataStartColumn,
        uint endColumn)
    {
        if (chart.SeriesInRows || chart.SeriesColumnMappings.Count == 0)
            return false;

        for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
        {
            var column = chart.SeriesColumnMappings[i].ValueColumn;
            if (column < dataStartColumn || column > endColumn)
                return false;
        }

        return true;
    }

    /// <summary>Returns whether a physical source column should become a plotted series.</summary>
    public static bool ShouldRenderSourceColumn(
        ChartModel chart,
        uint column,
        uint dataStartColumn,
        uint endColumn)
    {
        if (ShouldSkipSourceColumn(chart, column, dataStartColumn))
            return false;

        if (!HasAuthoritativeSeriesColumns(chart, dataStartColumn, endColumn))
            return true;

        for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
        {
            if (chart.SeriesColumnMappings[i].ValueColumn == column)
                return true;
        }

        return false;
    }

    /// <summary>Maps a physical value column to its chart-XML series index.</summary>
    public static int ResolveSeriesIndex(
        ChartModel chart,
        uint column,
        uint dataStartColumn,
        uint endColumn = uint.MaxValue)
    {
        if (HasAuthoritativeSeriesColumns(chart, dataStartColumn, endColumn))
        {
            for (var i = 0; i < chart.SeriesColumnMappings.Count; i++)
            {
                var mapping = chart.SeriesColumnMappings[i];
                if (mapping.ValueColumn == column)
                    return mapping.SeriesXmlIndex;
            }
        }

        var relativeColumn = column - dataStartColumn;
        if (chart.Type == ChartType.Bubble)
            return checked((int)(relativeColumn / 2));

        var scatterOffset = chart.Type == ChartType.Scatter && !chart.FirstColIsCategories ? 1u : 0u;
        return checked((int)(relativeColumn - scatterOffset));
    }

    /// <summary>Counts series that occupy clustered bar/column slots rather than combo overlays.</summary>
    public static int CountClusteredSeries(ChartModel chart, IEnumerable<int> seriesIndexes)
    {
        var count = 0;
        foreach (var seriesIndex in seriesIndexes)
        {
            if (!IsComboLineSeries(chart, seriesIndex) && !IsComboScatterSeries(chart, seriesIndex))
                count++;
        }

        return count;
    }

    /// <summary>Counts clustered series directly from a physical source-column span.</summary>
    public static int CountClusteredSourceSeries(
        ChartModel chart,
        uint dataStartColumn,
        uint endColumn)
    {
        var count = 0;
        for (var column = dataStartColumn; column <= endColumn; column++)
        {
            if (!ShouldRenderSourceColumn(chart, column, dataStartColumn, endColumn))
                continue;

            var seriesIndex = ResolveSeriesIndex(chart, column, dataStartColumn, endColumn);
            if (!IsComboLineSeries(chart, seriesIndex) && !IsComboScatterSeries(chart, seriesIndex))
                count++;
        }

        return count;
    }

    /// <summary>Returns whether the series is assigned to the secondary value axis.</summary>
    public static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        if (!chart.ShowSecondaryAxis || seriesIndex < 0)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0
            ? seriesIndex > 0
            : chart.SecondaryAxisSeriesIndexes.Contains(seriesIndex);
    }

    /// <summary>Returns whether a multi-series chart needs a secondary-axis materializer.</summary>
    public static bool HasAnySecondaryAxisSeries(
        ChartModel chart,
        IEnumerable<int> seriesIndexes)
    {
        var indexes = seriesIndexes.ToArray();
        if (!chart.ShowSecondaryAxis ||
            !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) ||
            indexes.Count < 2)
        {
            return false;
        }

        return indexes.Any(index => UsesSecondaryAxis(chart, index));
    }

    /// <summary>Returns whether the series is rendered as a combo line overlay.</summary>
    public static bool IsComboLineSeries(ChartModel chart, int seriesIndex) =>
        ChartTypeSupport.SupportsComboLineOverlay(chart.Type) &&
        chart.UseComboLineForSecondarySeries &&
        seriesIndex >= 0 &&
        chart.ComboLineSeriesIndexes.Contains(seriesIndex);

    /// <summary>Returns whether the series is rendered as a combo scatter overlay.</summary>
    public static bool IsComboScatterSeries(ChartModel chart, int seriesIndex) =>
        ChartTypeSupport.SupportsComboLineOverlay(chart.Type) &&
        seriesIndex >= 0 &&
        chart.ComboScatterSeriesIndexes.Contains(seriesIndex);

    /// <summary>Returns whether a series legend entry is deleted in declaration order.</summary>
    public static bool IsLegendEntryDeleted(ChartModel chart, int seriesIndex)
    {
        for (var i = 0; i < chart.LegendEntries.Count; i++)
        {
            var entry = chart.LegendEntries[i];
            var resolvedSeriesIndex = chart.SeriesPlotOrder.Count > 0 &&
                entry.Index >= 0 &&
                entry.Index < chart.SeriesPlotOrder.Count
                    ? chart.SeriesPlotOrder[entry.Index]
                    : entry.Index;
            if (resolvedSeriesIndex == seriesIndex)
                return entry.IsDeleted == true;
        }

        return false;
    }

    /// <summary>Returns whether a pie/doughnut category legend entry is deleted.</summary>
    public static bool IsPieLegendEntryDeleted(ChartModel chart, int pointIndex)
    {
        for (var i = 0; i < chart.LegendEntries.Count; i++)
        {
            if (chart.LegendEntries[i].Index == pointIndex)
                return chart.LegendEntries[i].IsDeleted == true;
        }

        return false;
    }

    /// <summary>Resolves both legacy scalar and per-point pie explosion settings.</summary>
    public static bool IsPieSliceExploded(ChartModel chart, int seriesIndex, int pointIndex) =>
        (seriesIndex == 0 && chart.ExplodedSliceIndex == pointIndex) ||
        chart.ExplodedSlices.Any(slice =>
            slice.SeriesIndex == seriesIndex && slice.PointIndex == pointIndex);

    /// <summary>
    /// Resolves the normalized pie-label radius. Hosts may supply their native annotation tuning
    /// while retaining one position-selection policy.
    /// </summary>
    public static double ResolvePieLabelRadiusFraction(
        ChartDataLabelPosition position,
        double center = 0.5,
        double insideEnd = 0.8,
        double outsideEnd = 1.15) =>
        position switch
        {
            ChartDataLabelPosition.Center => center,
            ChartDataLabelPosition.InsideEnd => insideEnd,
            ChartDataLabelPosition.OutsideEnd => outsideEnd,
            _ => insideEnd,
        };

    /// <summary>Resolves a bubble's unscaled pixel radius from the chart-wide size maximum.</summary>
    public static double ResolveBubbleRadius(
        double size,
        double maximumSize,
        ChartBubbleSizeRepresents represents)
    {
        if (maximumSize <= 0)
            return MinimumBubbleRadius;

        var fraction = Math.Clamp(size / maximumSize, 0, 1);
        var radiusFraction = represents == ChartBubbleSizeRepresents.Width
            ? fraction
            : Math.Sqrt(fraction);
        return Math.Max(MinimumBubbleRadius, MaximumBubbleRadius * radiusFraction);
    }

    /// <summary>Interpolates the shared blue-to-gold surface-chart palette.</summary>
    public static CellColor ResolveSurfaceCellColor(double value, double minimum, double maximum)
    {
        var t = maximum <= minimum ? 0.5 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        return new CellColor(
            (byte)Math.Round(68 + ((255 - 68) * t)),
            (byte)Math.Round(114 + ((192 - 114) * t)),
            (byte)Math.Round(196 - (196 * t)));
    }

    /// <summary>Resolves the shared color for one planned waterfall bar.</summary>
    public static CellColor ResolveWaterfallBarColor(WaterfallBarKind kind) =>
        kind switch
        {
            WaterfallBarKind.Total => WaterfallTotalColor,
            WaterfallBarKind.Increase => WaterfallIncreaseColor,
            _ => WaterfallDecreaseColor,
        };

    /// <summary>Calculates a percentile from sorted values using linear interpolation.</summary>
    public static double CalculatePercentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        if (sortedValues.Count == 0)
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 100) / 100.0 * (sortedValues.Count - 1);
        var lowerIndex = (int)position;
        var upperIndex = Math.Min(lowerIndex + 1, sortedValues.Count - 1);
        return sortedValues[lowerIndex] +
            ((position - lowerIndex) * (sortedValues[upperIndex] - sortedValues[lowerIndex]));
    }

    /// <summary>Plans quartiles, Tukey whiskers, and outliers for one box-and-whisker series.</summary>
    public static BoxAndWhiskerStatistics? PlanBoxAndWhisker(IEnumerable<double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return PlanBoxAndWhiskerCore(sorted);
    }

    public static BoxAndWhiskerStatistics? PlanBoxAndWhisker(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return PlanBoxAndWhiskerCore(values.ToList());
    }

    private static BoxAndWhiskerStatistics? PlanBoxAndWhiskerCore(List<double> sorted)
    {
        if (sorted.Count == 0)
            return null;

        sorted.Sort();
        var firstQuartile = CalculatePercentile(sorted, 25);
        var median = CalculatePercentile(sorted, 50);
        var thirdQuartile = CalculatePercentile(sorted, 75);
        var interquartileRange = thirdQuartile - firstQuartile;
        var lowerFence = firstQuartile - (1.5 * interquartileRange);
        var upperFence = thirdQuartile + (1.5 * interquartileRange);
        var lowerWhisker = sorted.First(value => value >= lowerFence);
        var upperWhisker = sorted.Last(value => value <= upperFence);
        var outliers = sorted.Where(value => value < lowerFence || value > upperFence).ToArray();
        return new BoxAndWhiskerStatistics(
            lowerWhisker,
            firstQuartile,
            median,
            thirdQuartile,
            upperWhisker,
            outliers);
    }

    /// <summary>Resolves final plus/minus error amounts, including direction and custom caches.</summary>
    public static ErrorBarAmounts ResolveErrorBarAmounts(
        ChartModel chart,
        IReadOnlyList<double> values,
        int index,
        IReadOnlyList<double>? customPlus,
        IReadOnlyList<double>? customMinus)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(values);
        if ((uint)index >= (uint)values.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var symmetric = chart.ErrorBarKind switch
        {
            ChartErrorBarKind.Percentage => Math.Abs(values[index]) * chart.ErrorBarValue / 100.0,
            ChartErrorBarKind.FixedValue => chart.ErrorBarValue,
            ChartErrorBarKind.StdDev => chart.ErrorBarValue * CalculateSampleStandardDeviation(values),
            ChartErrorBarKind.Custom => 0,
            _ => CalculateStandardError(values),
        };
        var authoredPlus = chart.ErrorBarKind == ChartErrorBarKind.Custom &&
            customPlus is not null && index < customPlus.Count
                ? Math.Abs(customPlus[index])
                : 0;
        var authoredMinus = chart.ErrorBarKind == ChartErrorBarKind.Custom &&
            customMinus is not null && index < customMinus.Count
                ? Math.Abs(customMinus[index])
                : 0;
        var plus = chart.ErrorBarDirection == ChartErrorBarDirection.Minus
            ? 0
            : authoredPlus > 0 ? authoredPlus : symmetric;
        var minus = chart.ErrorBarDirection == ChartErrorBarDirection.Plus
            ? 0
            : authoredMinus > 0 ? authoredMinus : symmetric;
        return new ErrorBarAmounts(plus, minus);
    }

    /// <summary>Parses an OOXML numeric cache into index order for custom error bars.</summary>
    public static IReadOnlyList<double>? ParseErrorBarRangeCache(string? cacheXml)
    {
        if (string.IsNullOrWhiteSpace(cacheXml))
            return null;

        try
        {
            var element = XElement.Parse(cacheXml);
            var points = new SortedDictionary<int, double>();
            foreach (var point in element.Elements().Where(candidate => candidate.Name.LocalName == "pt"))
            {
                var indexAttribute = point.Attribute("idx");
                var valueElement = point.Elements().FirstOrDefault(candidate => candidate.Name.LocalName == "v");
                if (indexAttribute is null || valueElement is null)
                    continue;
                if (int.TryParse(indexAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pointIndex) &&
                    double.TryParse(valueElement.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    points[pointIndex] = value;
                }
            }

            return points.Count == 0 ? null : points.Values.ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static double CalculateStandardError(IReadOnlyList<double> values) =>
        values.Count < 2
            ? 0
            : CalculateSampleStandardDeviation(values) / Math.Sqrt(values.Count);

    private static double CalculateSampleStandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var sumSquares = 0.0;
        for (var index = 0; index < values.Count; index++)
            sumSquares += (values[index] - mean) * (values[index] - mean);

        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
}
