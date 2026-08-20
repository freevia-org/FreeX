using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Localization;

namespace FreeX.App.Presentation.FillSeries;

/// <summary>Whether a fill series runs across rows (down a column) or across columns (along a row).</summary>
public enum FillSeriesDirection
{
    Rows,
    Columns,
}

/// <summary>The kind of fill series: arithmetic, geometric, date increment, or copy/autofill.</summary>
public enum FillSeriesType
{
    Linear,
    Growth,
    Date,
    AutoFill,
}

/// <summary>The calendar unit a date series steps by.</summary>
public enum FillSeriesDateUnit
{
    Day,
    Weekday,
    Month,
    Year,
}

/// <summary>The validated options the Fill ▸ Series dialog produces.</summary>
/// <param name="Trend">
/// Excel's Fill ▸ Series "Trend" checkbox (enabled for the Linear and Growth series types only).
/// When set, the Step value is ignored and the series instead continues a least-squares best-fit
/// line (Linear) or best-fit exponential curve (Growth) computed from every already-populated seed
/// value at the head of each series line, extrapolated into that line's remaining (blank) cells --
/// see <see cref="FillSeriesPlanner.BuildLinearSeriesEdits"/> / <see cref="FillSeriesPlanner.BuildGrowthSeriesEdits"/>.
/// Has no effect on the Date or AutoFill series types.
/// </param>
public sealed record FillSeriesOptions(
    double Step,
    FillSeriesDirection SeriesIn = FillSeriesDirection.Columns,
    FillSeriesType Type = FillSeriesType.Linear,
    FillSeriesDateUnit DateUnit = FillSeriesDateUnit.Day,
    double? StopValue = null,
    bool Trend = false);

/// <summary>Why the Fill ▸ Series inputs could not be turned into options.</summary>
public enum FillSeriesInputError
{
    None,
    InvalidStep,
    InvalidStop,
}

/// <summary>The Fill Series input that should receive focus after a validation error.</summary>
public enum FillSeriesInputFocusTarget
{
    StepValue,
    StopValue,
}

/// <summary>
/// Portable (no UI) backing logic for the Fill ▸ Series dialog (Home ▸ Fill ▸ Series). It parses and
/// validates the step/stop inputs and builds the linear / growth / date cell edits over a range, reading the
/// seed value from the active sheet. Kept UI-free so any desktop or cross-platform shell can reuse it and so it is
/// unit-testable without a window.
/// </summary>
public static class FillSeriesPlanner
{
    public static FillSeriesOptions DefaultOptions { get; } = new(
        Step: 1,
        SeriesIn: FillSeriesDirection.Columns,
        Type: FillSeriesType.Linear,
        DateUnit: FillSeriesDateUnit.Day);

    public static FillSeriesOptions CreateDefaultOptions(double step) =>
        DefaultOptions with { Step = step };

    public static bool IsDateUnitEnabled(FillSeriesType type) =>
        type == FillSeriesType.Date;

    /// <summary>
    /// Whether the Fill ▸ Series "Trend" checkbox (see <see cref="FillSeriesOptions.Trend"/>) is enabled
    /// for the given series type -- matching Excel, which only offers Trend for Linear and Growth.
    /// </summary>
    public static bool IsTrendEnabled(FillSeriesType type) =>
        type is FillSeriesType.Linear or FillSeriesType.Growth;

    public static FillSeriesInputFocusTarget FocusTargetFor(FillSeriesInputError error) =>
        error == FillSeriesInputError.InvalidStop
            ? FillSeriesInputFocusTarget.StopValue
            : FillSeriesInputFocusTarget.StepValue;

    public static ValidationPresentationDescriptor<FillSeriesInputFocusTarget> DescribeInputError(
        FillSeriesInputError error)
    {
        var resourceKey = error switch
        {
            FillSeriesInputError.InvalidStop => "FillSeriesStep_InvalidStopMessage",
            _ => "FillSeriesStep_InvalidStepMessage",
        };

        return new(
            LocalizedTextDescriptor.Resource(resourceKey),
            FocusTargetFor(error));
    }

    public static LocalizedTextDescriptor DescribeNoSeed() =>
        LocalizedTextDescriptor.Resource("FillSeries_NoSeed");

    public static LocalizedTextDescriptor DescribeCommandFailure(string? errorMessage) =>
        errorMessage is null
            ? LocalizedTextDescriptor.Resource("FillSeries_Failed")
            : LocalizedTextDescriptor.Literal(errorMessage);

    public static LocalizedTextDescriptor DescribeSuccess(string rangeReference) =>
        LocalizedTextDescriptor.Resource("FillSeries_Filled", rangeReference);

    /// <summary>
    /// Parses a step value, accepting the invariant decimal form and the current UI culture (so a typed
    /// <c>1.5</c> or a locale's <c>1,5</c> both work). Rejects non-finite values.
    /// </summary>
    public static bool TryParseStep(string? input, out double step)
    {
        if (TryParseFiniteDouble(input, CultureInfo.InvariantCulture, out step))
            return true;
        if (TryParseFiniteDouble(input, CultureInfo.CurrentCulture, out step))
            return true;

        step = 0;
        return false;
    }

    /// <summary>Parses a finite step value using only the supplied culture.</summary>
    public static bool TryParseStep(string? input, CultureInfo culture, out double step)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return TryParseFiniteDouble(input, culture, out step);
    }

    /// <summary>
    /// Parses and validates the dialog inputs into <see cref="FillSeriesOptions"/>. The stop value is
    /// optional (blank leaves it open); a present-but-unparseable stop or an unparseable step is rejected.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        out FillSeriesOptions options,
        out FillSeriesInputError error) =>
        TryCreateOptions(seriesIn, type, dateUnit, stepText, stopText, trend: false, out options, out error);

    /// <summary>
    /// Parses and validates the dialog inputs into <see cref="FillSeriesOptions"/>, including the Fill ▸
    /// Series "Trend" checkbox (see <see cref="FillSeriesOptions.Trend"/> and <see cref="IsTrendEnabled"/>).
    /// The stop value is optional (blank leaves it open); a present-but-unparseable stop or an unparseable
    /// step is rejected.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        bool trend,
        out FillSeriesOptions options,
        out FillSeriesInputError error)
    {
        options = new FillSeriesOptions(1, seriesIn, type, dateUnit, Trend: trend);
        error = FillSeriesInputError.None;

        if (!TryParseStep(stepText, out var step))
        {
            error = FillSeriesInputError.InvalidStep;
            return false;
        }

        double? stopValue = null;
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!TryParseStep(stopText, out var parsedStop))
            {
                error = FillSeriesInputError.InvalidStop;
                return false;
            }

            stopValue = parsedStop;
        }

        options = new FillSeriesOptions(step, seriesIn, type, dateUnit, stopValue, trend);
        return true;
    }

    /// <summary>
    /// Parses and validates dialog inputs with one explicit culture. Use this when a shell must preserve
    /// current-culture-only decimal handling instead of the invariant-first portable default.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        CultureInfo culture,
        out FillSeriesOptions options,
        out FillSeriesInputError error) =>
        TryCreateOptions(seriesIn, type, dateUnit, stepText, stopText, trend: false, culture, out options, out error);

    /// <summary>
    /// Parses and validates dialog inputs (including the Trend checkbox) with one explicit culture. Use
    /// this when a shell must preserve current-culture-only decimal handling instead of the
    /// invariant-first portable default.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        bool trend,
        CultureInfo culture,
        out FillSeriesOptions options,
        out FillSeriesInputError error)
    {
        ArgumentNullException.ThrowIfNull(culture);

        options = new FillSeriesOptions(1, seriesIn, type, dateUnit, Trend: trend);
        error = FillSeriesInputError.None;

        if (!TryParseStep(stepText, culture, out var step))
        {
            error = FillSeriesInputError.InvalidStep;
            return false;
        }

        double? stopValue = null;
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!TryParseStep(stopText, culture, out var parsedStop))
            {
                error = FillSeriesInputError.InvalidStop;
                return false;
            }

            stopValue = parsedStop;
        }

        options = new FillSeriesOptions(step, seriesIn, type, dateUnit, stopValue, trend);
        return true;
    }

    /// <summary>True when the selection is big enough to fill in the requested direction.</summary>
    public static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction is FillCellsDirection.Down or FillCellsDirection.Up
            ? range.RowCount >= 2
            : range.ColCount >= 2;

    /// <summary>Builds the series cell edits for the given options, dispatching by series type.</summary>
    public static List<(CellAddress Address, Cell NewCell)> BuildSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        return options.Type switch
        {
            FillSeriesType.Growth => BuildGrowthSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.StopValue, options.Trend),
            FillSeriesType.Date => BuildDateSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.DateUnit, options.StopValue),
            FillSeriesType.AutoFill => BuildAutoFillSeriesEdits(sheet, range, options.SeriesIn),
            _ => BuildLinearSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.StopValue, options.Trend),
        };
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        double? stopValue = null,
        bool trend = false)
    {
        // Trend replaces the fixed-step chain below with a per-line least-squares best fit --
        // see BuildTrendSeriesEdits. The Step value plays no part in Trend mode (Excel disables
        // the Step box once Trend is checked), and the non-Trend path below is left byte-for-byte
        // unchanged so existing Linear callers are unaffected.
        if (trend)
            return BuildTrendSeriesEdits(sheet, range, step, seriesIn, growth: false);

        var edits = new List<(CellAddress, Cell)>();
        var value = 0d;
        var hasValue = false;
        var lineStopped = false;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            // Excel treats every row/column in the selection as its own series line: each
            // line's own leading cell (the top cell of its column for "Series in Columns", or
            // the leading cell of its row for "Series in Rows") reseeds that line when it holds
            // a number. This check must run for EVERY line's leading cell, not just once up
            // front against the selection's very first cell -- otherwise an invalid seed in the
            // very first column/row would wipe out every other column/row's perfectly valid
            // fill too. A line whose leading cell is not numeric simply does not reseed: if a
            // running value has already been established by an earlier line in the selection it
            // keeps chaining forward (matching a fill-handle drag across the whole rectangle),
            // and if no value has been established yet the line is left untouched until one is.
            // Reaching a line's own leading cell also resets the Stop Value clamp for that line:
            // a stop reached on an earlier line/column must not suppress a later, independent
            // line's fill.
            if (IsSeriesLineStart(address, range, seriesIn))
            {
                lineStopped = false;
                if (TryGetLinearOrGrowthSeriesSeed(sheet.GetValue(address.Row, address.Col), out var lineSeedValue))
                {
                    value = lineSeedValue + step;
                    hasValue = true;
                    continue;
                }
            }

            if (lineStopped || !hasValue)
                continue;

            if (IsPastStopValue(value, step, stopValue))
            {
                lineStopped = true;
                continue;
            }

            edits.Add((address, Cell.FromValue(new NumberValue(value))));
            value += step;
        }

        return edits;
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildGrowthSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        double? stopValue = null,
        bool trend = false)
    {
        // See BuildLinearSeriesEdits' matching Trend guard: Growth-Trend fits a best-fit
        // exponential curve (log-linear least squares) through each line's seed run instead of
        // chaining the fixed multiplicative Step below, and the non-Trend path is unchanged.
        if (trend)
            return BuildTrendSeriesEdits(sheet, range, step, seriesIn, growth: true);

        var edits = new List<(CellAddress, Cell)>();
        var value = 0d;
        var ascending = false;
        var hasValue = false;
        var lineStopped = false;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            // Same per-line-seed handling as BuildLinearSeriesEdits: every row/column in the
            // selection is its own independent series line, so a line whose own leading cell
            // holds a number reseeds THAT line -- both the running value and the
            // ascending/descending direction used for the Stop Value clamp below, since two
            // different lines' own seeds can trend in opposite directions for the same step. This
            // check runs for every line, not just the selection's first, so one column's invalid
            // seed can never wipe out another column's valid one. A line whose leading cell is
            // not numeric does not reseed: an already-established running value keeps chaining
            // forward into it, and if no value has been established yet the line is left
            // untouched until one is. Reaching a line's own leading cell also resets the Stop
            // Value clamp for that line: a stop reached on an earlier line/column must not
            // suppress a later, independent line's fill.
            if (IsSeriesLineStart(address, range, seriesIn))
            {
                lineStopped = false;
                if (TryGetLinearOrGrowthSeriesSeed(sheet.GetValue(address.Row, address.Col), out var lineSeedValue))
                {
                    ascending = IsGrowthAscending(lineSeedValue, step);
                    value = lineSeedValue * step;
                    hasValue = true;
                    continue;
                }
            }

            if (lineStopped || !hasValue)
                continue;

            if (IsPastStopValue(value, ascending, stopValue))
            {
                lineStopped = true;
                continue;
            }

            edits.Add((address, Cell.FromValue(new NumberValue(value))));
            value *= step;
        }

        return edits;
    }

    /// <summary>
    /// Builds the Fill ▸ Series "Trend" edits shared by Linear and Growth (see
    /// <see cref="FillSeriesOptions.Trend"/>): each independent series line (column, for "Series in
    /// Columns"; row, for "Series in Rows" -- the same per-line split <see cref="EnumerateSeriesLines"/>
    /// already provides for the AutoFill series type) is fitted and extrapolated on its own, exactly
    /// like the non-Trend engines treat each line as an independent series.
    /// </summary>
    private static List<(CellAddress Address, Cell NewCell)> BuildTrendSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        bool growth)
    {
        var edits = new List<(CellAddress, Cell)>();
        foreach (var line in EnumerateSeriesLines(sheet.Id, range, seriesIn))
            BuildTrendLineEdits(sheet, line, step, growth, edits);

        return edits;
    }

    /// <summary>
    /// Builds one line's Trend edits: reads the leading contiguous run of already-populated
    /// numeric/date seed cells (matching <see cref="BuildAutoFillLineEdits"/>'s own seed-run scan),
    /// fits a best-fit line through them, and extrapolates the fit into the line's remaining
    /// (blank) cells. A line with no seed at all is left untouched, matching every other series
    /// engine's own no-seed no-op.
    /// </summary>
    private static void BuildTrendLineEdits(
        Sheet sheet,
        IReadOnlyList<CellAddress> line,
        double step,
        bool growth,
        List<(CellAddress Address, Cell NewCell)> edits)
    {
        var seedValues = new List<ScalarValue>();
        foreach (var address in line)
        {
            var value = sheet.GetValue(address.Row, address.Col);
            if (!TryGetLinearOrGrowthSeriesSeed(value, out _))
                break;

            seedValues.Add(value);
        }

        var seedCount = seedValues.Count;
        if (seedCount == 0)
            return;

        // A single known point has no trend line to fit -- Excel's own Trend behavior for a lone
        // seed falls back to the manually entered Step value instead (additive for Linear,
        // multiplicative for Growth), the same single-seed chaining the non-Trend engines above
        // use for their own lone-seed case.
        if (seedCount == 1)
        {
            TryGetLinearOrGrowthSeriesSeed(seedValues[0], out var value);
            var createSingle = SeedValueFactory(seedValues[0]);
            for (var i = seedCount; i < line.Count; i++)
            {
                value = growth ? value * step : value + step;
                edits.Add((line[i], Cell.FromValue(createSingle(value))));
            }

            return;
        }

        Func<int, ScalarValue> lineSeries;
        var fitted = growth
            ? TryCreateGrowthTrendLineSeries(seedValues, out lineSeries)
            : TryCreateNumericOrDateLineSeries(seedValues, out lineSeries);
        if (!fitted)
            return; // Mixed/non-numeric seed run, or (Growth) a zero/negative seed with no logarithm.

        for (var i = seedCount; i < line.Count; i++)
            edits.Add((line[i], Cell.FromValue(lineSeries(i - seedCount + 1))));
    }

    /// <summary>
    /// Fits a least-squares exponential curve through a Growth-Trend line's seed run by
    /// linearizing it: a least-squares fit of ln(y) against x = 0, 1, 2, ... yields a slope/
    /// intercept whose exponential y = exp(intercept + slope * x) is the best-fit growth curve,
    /// mirroring <see cref="TryCreateNumericOrDateLineSeries"/>'s own linear fit (and its anchor-at-
    /// the-last-seed-index convention) but in log space. A seed run containing a zero or negative
    /// value has no logarithm and cannot be growth-fitted -- Excel raises #NUM! for this case, so
    /// this returns false and the caller leaves that line untouched, matching every other
    /// unsupported-seed case in this file (e.g. <see cref="BuildGrowthSeriesEdits"/>'s own
    /// non-numeric-seed no-op).
    /// </summary>
    private static bool TryCreateGrowthTrendLineSeries(
        IReadOnlyList<ScalarValue> seedValues,
        out Func<int, ScalarValue> lineSeries)
    {
        var createValue = SeedValueFactory(seedValues[0]);
        double[] numbers;
        if (seedValues.All(value => value is NumberValue))
            numbers = seedValues.Select(value => ((NumberValue)value).Value).ToArray();
        else if (seedValues.All(value => value is DateTimeValue))
            numbers = seedValues.Select(value => ((DateTimeValue)value).Value).ToArray();
        else
        {
            lineSeries = null!;
            return false;
        }

        if (numbers.Any(number => number <= 0))
        {
            lineSeries = null!;
            return false;
        }

        var logs = numbers.Select(number => Math.Log(number)).ToArray();
        var slope = ComputeLeastSquaresSlope(logs);
        var meanX = (numbers.Length - 1) / 2.0;
        var intercept = logs.Average() - slope * meanX;
        var anchorLog = intercept + slope * (numbers.Length - 1);

        lineSeries = offset => createValue(Math.Exp(anchorLog + slope * offset));
        return true;
    }

    /// <summary>Builds a same-typed (number or date-serial) value factory from a sample seed cell's value.</summary>
    private static Func<double, ScalarValue> SeedValueFactory(ScalarValue seed) =>
        seed is DateTimeValue ? serial => new DateTimeValue(serial) : serial => new NumberValue(serial);

    public static List<(CellAddress Address, Cell NewCell)> BuildDateSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        FillSeriesDateUnit dateUnit,
        double? stopValue = null)
    {
        var edits = new List<(CellAddress, Cell)>();
        var seed = 0d;
        var value = 0d;
        var preserveEndOfMonth = false;
        var stepIndex = 0;
        var hasValue = false;
        var lineStopped = false;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            // Same per-line-seed handling as BuildLinearSeriesEdits/BuildGrowthSeriesEdits: a
            // line (column, for "Series in Columns"; row, for "Series in Rows") whose own
            // leading cell holds a date restarts the Month/Year end-of-month clamp and step
            // count from THAT date. This check runs for every line, not just the selection's
            // first, so one column's invalid seed can never wipe out another column's valid one.
            // A line whose leading cell is not a date does not reseed: an already-established
            // running value keeps chaining forward into it, and if no value has been established
            // yet the line is left untouched until one is. Reaching a line's own leading cell
            // also resets the Stop Value clamp for that line: a stop reached on an earlier
            // line/column must not suppress a later, independent line's fill.
            if (IsSeriesLineStart(address, range, seriesIn))
            {
                lineStopped = false;
                if (sheet.GetValue(address.Row, address.Col) is DateTimeValue lineSeed)
                {
                    seed = lineSeed.Value;
                    preserveEndOfMonth = IsLastDayOfMonth(lineSeed.ToDateTime());
                    stepIndex = 1;
                    value = NextDateSerial(seed, seed, step, dateUnit, preserveEndOfMonth, stepIndex);
                    hasValue = true;
                    continue;
                }
            }

            if (lineStopped || !hasValue)
                continue;

            // NextDateSerial yields NaN once the series would leave the calendar range; stop that
            // line there rather than writing an unrepresentable date.
            if (!double.IsFinite(value))
            {
                lineStopped = true;
                continue;
            }

            if (IsPastStopValue(value, step, stopValue))
            {
                lineStopped = true;
                continue;
            }

            edits.Add((address, Cell.FromValue(new DateTimeValue(value))));
            stepIndex++;
            value = NextDateSerial(seed, value, step, dateUnit, preserveEndOfMonth, stepIndex);
        }

        return edits;
    }

    /// <summary>
    /// Builds the AutoFill series-type edits for Fill ▸ Series. Each line (column, for "Series in
    /// Columns"; row, for "Series in Rows") in the selection is its own independent series,
    /// detected from that line's own leading run of already-populated "seed" cells -- exactly
    /// like a fill-handle drag, where the source cells precede the destination cells being
    /// filled. A numeric or date seed run (Excel's AutoFill continues a 2+ cell arithmetic/date
    /// trend; a lone date seed defaults to a +1-day step, while a lone plain number defaults to a
    /// COPY, matching <see cref="AutofillCommand"/>'s own lone-cell default) is detected first; a
    /// text seed run falls back to <see cref="AutofillCommand"/>'s own trailing-number /
    /// built-in-list detection, replaying the exact same logic a fill-handle drag uses for
    /// "Item 1" -&gt; "Item 2" or weekday/month lists, and any other seed run (numeric/date
    /// seeds that don't match a detectable series, or non-list/non-trailing-number text) falls
    /// back to a cyclic replay of the seed run itself, matching AutofillCommand's own
    /// ResolvePatternSourceAddress fallback for the identical case.
    /// </summary>
    public static List<(CellAddress Address, Cell NewCell)> BuildAutoFillSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesDirection seriesIn)
    {
        var edits = new List<(CellAddress, Cell)>();
        foreach (var line in EnumerateSeriesLines(sheet.Id, range, seriesIn))
            BuildAutoFillLineEdits(sheet, line, edits);

        return edits;
    }

    /// <summary>
    /// Builds one line's AutoFill edits: reads the leading contiguous run of populated cells as
    /// the line's seed(s), detects a numeric/date/text series from them, and fills the remaining
    /// (blank) cells of the line. A text seed run that doesn't match a detectable trailing-number
    /// or built-in/custom-list series instead replays cyclically, matching a fill-handle drag's
    /// own pattern-copy fallback for the identical case (see
    /// <see cref="AutofillCommand"/>.ResolvePatternSourceAddress) -- a line with no leading seed
    /// at all is still left untouched.
    /// </summary>
    private static void BuildAutoFillLineEdits(
        Sheet sheet,
        IReadOnlyList<CellAddress> line,
        List<(CellAddress Address, Cell NewCell)> edits)
    {
        var seedValues = new List<ScalarValue>();
        foreach (var address in line)
        {
            var value = sheet.GetValue(address.Row, address.Col);
            if (value is BlankValue)
                break;

            seedValues.Add(value);
        }

        var seedCount = seedValues.Count;
        if (seedCount == 0)
            return;

        if (TryCreateNumericOrDateLineSeries(seedValues, out var numericSeries))
        {
            for (var i = seedCount; i < line.Count; i++)
                edits.Add((line[i], Cell.FromValue(numericSeries(i - seedCount + 1))));

            return;
        }

        if (seedValues.All(value => value is TextValue))
        {
            var texts = seedValues.Select(value => ((TextValue)value).Value).ToList();
            var textSeries = AutofillCommand.TryCreateAutoFillTextSeries(texts);
            if (textSeries is not null)
            {
                for (var i = seedCount; i < line.Count; i++)
                    edits.Add((line[i], Cell.FromValue(textSeries(i - seedCount + 1))));

                return;
            }

            // No trailing-number/list series detected (e.g. an arbitrary alternating "Red",
            // "Blue" pair, or a single plain word): replay the seed run's own values cyclically
            // instead of leaving the rest of the line untouched, matching AutofillCommand's own
            // ResolvePatternSourceAddress fallback for the identical non-series text case.
            for (var i = seedCount; i < line.Count; i++)
                edits.Add((line[i], Cell.FromValue(seedValues[(i - seedCount) % seedCount])));
        }
    }

    /// <summary>
    /// Detects a numeric or date AutoFill series from a line's leading seed cells, mirroring the
    /// fill handle's own numeric/date trend detection (<see cref="AutofillCommand"/>'s
    /// TryCreateScalarSeries / TryCreateForcedSingleCellSeries / WantsSingleCellSeriesDefault): a
    /// homogeneous 2+ cell seed run fits a least-squares regression line through ALL the seed
    /// points and continues THAT line (Excel's own behavior for a fill-handle drag), not a naive
    /// endpoint-average step -- for a non-arithmetic seed run like 1, 2, 6 the two-point-average
    /// step would produce 8.5, 11, 13.5, while the correctly-fitted regression line continues as
    /// 8, 10.5, 13. A lone seed's default is type-dependent, matching
    /// <see cref="AutofillCommand"/>.WantsSingleCellSeriesDefault's un-Ctrl'd default (AutoFill
    /// has no Ctrl-equivalent toggle to flip it): a lone DATE seed defaults to a +1-day step,
    /// since dates default to a series; a lone plain NUMBER seed instead defaults to a COPY (step
    /// 0), since numbers default to a copy. A seed run that mixes types (or holds no
    /// numbers/dates at all) is not a numeric/date series.
    /// </summary>
    private static bool TryCreateNumericOrDateLineSeries(
        IReadOnlyList<ScalarValue> seedValues,
        out Func<int, ScalarValue> lineSeries)
    {
        Func<double, ScalarValue> createValue;
        double[] numbers;
        bool isDate;
        if (seedValues.All(value => value is NumberValue))
        {
            createValue = serial => new NumberValue(serial);
            numbers = seedValues.Select(value => ((NumberValue)value).Value).ToArray();
            isDate = false;
        }
        else if (seedValues.All(value => value is DateTimeValue))
        {
            createValue = serial => new DateTimeValue(serial);
            numbers = seedValues.Select(value => ((DateTimeValue)value).Value).ToArray();
            isDate = true;
        }
        else
        {
            lineSeries = null!;
            return false;
        }

        double anchor;
        double step;
        if (numbers.Length >= 2)
        {
            // Fit the regression line through all seed points and anchor on the fitted line's
            // value at the seed run's last index (offset 0), matching AutofillCommand's own
            // FitScalarLine so a Fill ▸ Series ▸ AutoFill continuation agrees with a fill-handle
            // drag over the same seed cells. This reduces to the plain last-value anchor whenever
            // the seed run is already perfectly linear, since the fitted line then passes exactly
            // through every sampled point.
            step = ComputeLeastSquaresSlope(numbers);
            var meanX = (numbers.Length - 1) / 2.0;
            var intercept = numbers.Average() - step * meanX;
            anchor = intercept + step * (numbers.Length - 1);
        }
        else if (isDate)
        {
            step = 1d;
            anchor = numbers[0];
        }
        else
        {
            // Lone plain-number seed: Excel's fill handle default for a single numeric cell is a
            // COPY, not an incrementing series (Ctrl would force the series instead, but AutoFill
            // has no Ctrl-equivalent toggle here) -- see AutofillCommand.WantsSingleCellSeriesDefault.
            step = 0d;
            anchor = numbers[0];
        }

        lineSeries = offset => createValue(anchor + step * offset);
        return true;
    }

    /// <summary>
    /// Fits a straight line (least-squares) through <paramref name="numbers"/> (treated as
    /// y-values at evenly spaced x = 0, 1, 2, ...) and returns its slope, mirroring
    /// <see cref="AutofillCommand"/>'s own ComputeLinearFitSlope so AutoFill's numeric/date
    /// continuation agrees with a fill-handle drag over the same seed cells. For exactly two
    /// values this reduces to the plain two-point slope (numbers[1] - numbers[0]).
    /// </summary>
    private static double ComputeLeastSquaresSlope(IReadOnlyList<double> numbers)
    {
        var n = numbers.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += numbers[i];
            sumXY += i * numbers[i];
            sumXX += (double)i * i;
        }

        var denominator = n * sumXX - sumX * sumX;
        if (denominator == 0)
            return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }

    /// <summary>
    /// Groups a range's cells into its independent AutoFill series lines: one line per column
    /// (top-to-bottom) for "Series in Columns", one per row (left-to-right) for "Series in Rows" --
    /// the same per-line split <see cref="EnumerateSeriesAddresses"/>'s flat, line-major
    /// enumeration already implies, but returned as grouped lines so <see cref="BuildAutoFillLineEdits"/>
    /// can scan each line's own leading seed run in isolation.
    /// </summary>
    private static IEnumerable<IReadOnlyList<CellAddress>> EnumerateSeriesLines(SheetId sheetId, GridRange range, FillSeriesDirection seriesIn)
    {
        if (seriesIn == FillSeriesDirection.Columns)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                var line = new List<CellAddress>();
                for (var row = range.Start.Row; row <= range.End.Row; row++)
                    line.Add(new CellAddress(sheetId, row, col));
                yield return line;
            }

            yield break;
        }

        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var line = new List<CellAddress>();
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                line.Add(new CellAddress(sheetId, row, col));
            yield return line;
        }
    }

    private static IEnumerable<CellAddress> EnumerateSeriesAddresses(SheetId sheetId, GridRange range, FillSeriesDirection seriesIn)
    {
        if (seriesIn == FillSeriesDirection.Columns)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                for (var row = range.Start.Row; row <= range.End.Row; row++)
                    yield return new CellAddress(sheetId, row, col);
            }

            yield break;
        }

        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                yield return new CellAddress(sheetId, row, col);
        }
    }

    /// <summary>
    /// True when <paramref name="address"/> is the leading cell of its own series line: the top cell of a
    /// column when filling "Series in Columns", or the leading cell of a row when filling "Series in Rows".
    /// </summary>
    private static bool IsSeriesLineStart(CellAddress address, GridRange range, FillSeriesDirection seriesIn) =>
        seriesIn == FillSeriesDirection.Columns
            ? address.Row == range.Start.Row
            : address.Col == range.Start.Col;

    /// <summary>
    /// Linear and Growth series treat a date's underlying serial number as a plain numeric seed,
    /// matching Excel: whether a cell holds 1/1/2026 (a <see cref="DateTimeValue"/>, because it has
    /// a date number format) or the equivalent bare number 46023 (a <see cref="NumberValue"/>) makes
    /// no difference to these two series types -- only the dedicated Date series type
    /// (<see cref="BuildDateSeriesEdits"/>) treats dates specially (month/year clamping etc.). Any
    /// other seed shape (text, blank, boolean, error) still does not reseed a line.
    /// </summary>
    private static bool TryGetLinearOrGrowthSeriesSeed(ScalarValue? value, out double seed)
    {
        switch (value)
        {
            case NumberValue number:
                seed = number.Value;
                return true;
            case DateTimeValue dateTime:
                seed = dateTime.Value;
                return true;
            default:
                seed = 0;
                return false;
        }
    }

    private static bool IsPastStopValue(double value, double step, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return step < 0 ? value < stop : value > stop;
    }

    private static bool IsPastStopValue(double value, bool ascending, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return ascending ? value > stop : value < stop;
    }

    /// <summary>
    /// Whether a growth series' terms trend upward, derived from the STEP's effect on the seed
    /// (comparing the first computed term against the seed) rather than from comparing the seed
    /// to the user's Stop Value -- e.g. seed=10/step=3 (10 -&gt; 30) or seed=-10/step=0.5
    /// (-10 -&gt; -5) are ascending; seed=10/step=0.5 (10 -&gt; 5) or seed=-10/step=3
    /// (-10 -&gt; -30) are descending. Mirrors BuildLinearSeriesEdits' step-sign-derived
    /// direction: a mismatched Stop Value must clamp immediately rather than run away because
    /// the direction was inferred from start-vs-stop instead of from the step itself.
    /// </summary>
    private static bool IsGrowthAscending(double seed, double step) => seed * step >= seed;

    /// <summary>
    /// Computes the next date serial in a Fill ▸ Series ▸ Date sequence. Day and Weekday units have no
    /// per-target clamping, so they safely chain off <paramref name="previousValue"/>. Month and Year units
    /// clamp the day-of-month when the target month is shorter than the seed's day (e.g. 30-Jan + 1 month =
    /// 28-Feb), and Excel always measures that clamp against the ORIGINAL seed date, not the previous
    /// (already-clamped) result — otherwise a short month like February permanently truncates every later
    /// date in the series. <paramref name="stepIndex"/> is the 1-based count of calendar-unit steps from the
    /// seed to this target, so Month/Year compute <c>seed.AddMonths(step * stepIndex)</c> directly.
    /// </summary>
    private static double NextDateSerial(double seedValue, double previousValue, double step, FillSeriesDateUnit dateUnit, bool preserveEndOfMonth, int stepIndex)
    {
        if (dateUnit == FillSeriesDateUnit.Day)
            return previousValue + step;

        var wholeStep = (int)Math.Truncate(step);
        if (wholeStep == 0)
            return previousValue;

        // Month and Year re-anchor from the seed and multiply, so the offset grows with the row
        // count: filling a few thousand rows by year, or a whole column by month, walks past
        // year 9999 long before the selection runs out. DateTime.AddMonths/AddYears throw there,
        // and the multiply itself overflows int on a large step, so both are computed in long and
        // range-checked. Out of range yields NaN, which stops the line (see BuildDateSeriesEdits)
        // the way Excel stops the series rather than failing the whole fill.
        var offset = (long)wholeStep * stepIndex;

        return dateUnit switch
        {
            FillSeriesDateUnit.Weekday => AddWeekdays(previousValue, wholeStep),
            FillSeriesDateUnit.Month => AddMonths(seedValue, offset, preserveEndOfMonth),
            FillSeriesDateUnit.Year => AddYears(seedValue, offset, preserveEndOfMonth),
            _ => previousValue + step,
        };
    }

    /// <summary>
    /// Adds whole months, returning NaN when the result would leave the calendar range.
    /// <paramref name="value"/> and the return value are genuine Excel serials (the same convention
    /// as <see cref="DateTimeValue.Value"/>), which differ from .NET OADates by one day for any date
    /// before 1 March 1900 (Excel keeps the phantom 29 February 1900; OADate does not) -- so the
    /// conversion in both directions must go through <see cref="DateTimeValue.ToDateTime"/> /
    /// <see cref="DateTimeValue.FromDateTime"/> rather than a bare <c>DateTime.FromOADate</c>/
    /// <c>.ToOADate()</c>, matching the seed conversion three lines above this method's only caller
    /// (<c>lineSeed.ToDateTime()</c> in <see cref="BuildDateSeriesEdits"/>).
    /// </summary>
    private static double AddMonths(double value, long months, bool preserveEndOfMonth)
    {
        var date = new DateTimeValue(value).ToDateTime();
        var totalMonths = ((long)date.Year * 12) + (date.Month - 1) + months;
        var year = totalMonths / 12;
        var month = totalMonths % 12;
        if (month < 0)
        {
            month += 12;
            year--;
        }

        if (year < DateTime.MinValue.Year || year > DateTime.MaxValue.Year)
            return double.NaN;

        var day = Math.Min(date.Day, DateTime.DaysInMonth((int)year, (int)month + 1));
        var shifted = new DateTime((int)year, (int)month + 1, day, date.Hour, date.Minute, date.Second, date.Millisecond, date.Kind);
        return DateTimeValue.FromDateTime(PreserveEndOfMonth(shifted, preserveEndOfMonth)).Value;
    }

    /// <summary>
    /// Adds whole years, returning NaN when the result would leave the calendar range. See
    /// <see cref="AddMonths"/> for why both conversions route through <see cref="DateTimeValue"/>
    /// instead of a bare OADate round trip.
    /// </summary>
    private static double AddYears(double value, long years, bool preserveEndOfMonth)
    {
        var date = new DateTimeValue(value).ToDateTime();
        var year = date.Year + years;
        if (year < DateTime.MinValue.Year || year > DateTime.MaxValue.Year)
            return double.NaN;

        var day = Math.Min(date.Day, DateTime.DaysInMonth((int)year, date.Month));
        var shifted = new DateTime((int)year, date.Month, day, date.Hour, date.Minute, date.Second, date.Millisecond, date.Kind);
        return DateTimeValue.FromDateTime(PreserveEndOfMonth(shifted, preserveEndOfMonth)).Value;
    }

    private static DateTime PreserveEndOfMonth(DateTime date, bool preserveEndOfMonth) =>
        preserveEndOfMonth
            ? new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), date.Hour, date.Minute, date.Second, date.Millisecond, date.Kind)
            : date;

    private static double AddWeekdays(double value, int weekdays)
    {
        var date = DateTime.FromOADate(value);
        var direction = Math.Sign(weekdays);
        for (var remaining = Math.Abs(weekdays); remaining > 0;)
        {
            date = date.AddDays(direction);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            remaining--;
        }

        return date.ToOADate();
    }

    private static bool IsLastDayOfMonth(DateTime date) =>
        date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    private static bool TryParseFiniteDouble(string? input, CultureInfo culture, out double value)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        return double.TryParse((input ?? string.Empty).Trim(), styles, culture, out value) &&
               double.IsFinite(value);
    }
}
