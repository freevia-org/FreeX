using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal static partial class ViewportConditionalFormatEvaluator
{
    private static Dictionary<ConditionalFormat, CfAggregateCache> PrecomputeAggregates(Sheet sheet)
    {
        Dictionary<ConditionalFormat, CfAggregateCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (!RequiresAggregateCache(cf))
                continue;

            double sum = 0, sumSq = 0, min = double.MaxValue, max = double.MinValue;
            int count = 0;
            List<(CellAddress Address, double Value, int Index)>? rankedValues =
                cf.RuleType == CfRuleType.Top10 ? [] : null;
            Dictionary<string, int>? valueCounts =
                cf.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : null;
            List<double>? numericValues = RequiresSortedNumericValues(cf) ? [] : null;
            foreach (var (a, v) in EnumerateAllAggregateValues(sheet, cf))
            {
                if (valueCounts is not null && !IsBlankValue(v))
                {
                    var key = GetDuplicateValueKey(v);
                    valueCounts[key] = valueCounts.GetValueOrDefault(key) + 1;
                }

                if (TryGetDouble(v, out double x))
                {
                    sum += x;
                    sumSq += x * x;
                    if (x < min) min = x;
                    if (x > max) max = x;
                    rankedValues?.Add((a, x, count));
                    numericValues?.Add(x);
                    count++;
                }
            }

            var topBottomMatches = ResolveTopBottomMatches(cf, rankedValues);
            numericValues?.Sort();
            if (count > 0 || valueCounts?.Count > 0 || topBottomMatches is not null)
            {
                var average = count > 0 ? sum / count : 0;
                // Sample standard deviation (STDEV semantics), matching Excel's "N standard
                // deviations above/below average" conditional format rule. Needs at least 2
                // points; otherwise there is no variance to speak of.
                var stdDev = count > 1
                    ? Math.Sqrt(Math.Max(0, (sumSq - count * average * average) / (count - 1)))
                    : 0;
                (result ??= new Dictionary<ConditionalFormat, CfAggregateCache>(ReferenceEqualityComparer.Instance))[cf] = new CfAggregateCache(
                    average,
                    count > 0 ? min : 0,
                    count > 0 ? max : 0,
                    numericValues,
                    topBottomMatches,
                    valueCounts?.Count > 0 ? valueCounts : null,
                    stdDev);
            }
        }
        return result ?? EmptyAggregates;
    }

    private static bool RequiresAggregateCache(ConditionalFormat cf) =>
        cf.RuleType switch
        {
            CfRuleType.AboveAverage or
            CfRuleType.DataBar or
            CfRuleType.ColorScale or
            CfRuleType.Top10 or
            CfRuleType.DuplicateValues or
            CfRuleType.UniqueValues => true,
            CfRuleType.IconSet => RequiresIconSetAggregateCache(cf),
            _ => false
        };

    private static bool RequiresIconSetAggregateCache(ConditionalFormat cf)
    {
        var iconCount = GetIconSetCount(cf.IconSetStyle);
        var thresholdCount = iconCount - 1;
        var thresholdStartIndex = GetIconSetThresholdStartIndex(cf, iconCount);
        if (cf.IconSetThresholds.Count - thresholdStartIndex < thresholdCount)
            return true;

        for (var i = 0; i < thresholdCount; i++)
        {
            var threshold = cf.IconSetThresholds[thresholdStartIndex + i];
            if (RequiresAggregateThreshold(threshold.Type) ||
                (threshold.Type == CfThresholdType.Number && !TryParseDouble(threshold.Value, out _)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresSortedNumericValues(ConditionalFormat cf)
    {
        if (cf.RuleType == CfRuleType.ColorScale)
        {
            return cf.MinThresholdType == CfThresholdType.Percentile ||
                   cf.MaxThresholdType == CfThresholdType.Percentile ||
                   (cf.UseThreeColorScale && cf.MidThresholdType == CfThresholdType.Percentile);
        }

        if (cf.RuleType == CfRuleType.DataBar)
        {
            return cf.DataBarMinThresholdType == CfThresholdType.Percentile ||
                   cf.DataBarMaxThresholdType == CfThresholdType.Percentile;
        }

        if (cf.RuleType != CfRuleType.IconSet)
            return false;

        var iconCount = GetIconSetCount(cf.IconSetStyle);
        var thresholdStartIndex = GetIconSetThresholdStartIndex(cf, iconCount);
        var thresholdCount = Math.Min(iconCount - 1, cf.IconSetThresholds.Count - thresholdStartIndex);
        for (var i = 0; i < thresholdCount; i++)
        {
            if (cf.IconSetThresholds[thresholdStartIndex + i].Type == CfThresholdType.Percentile)
                return true;
        }

        return false;
    }

    private static bool RequiresAggregateThreshold(CfThresholdType type) =>
        type is CfThresholdType.Min
            or CfThresholdType.Max
            or CfThresholdType.Percent
            or CfThresholdType.Percentile
            or CfThresholdType.Formula;

    private static IReadOnlySet<CellAddress>? ResolveTopBottomMatches(
        ConditionalFormat cf,
        List<(CellAddress Address, double Value, int Index)>? rankedValues)
    {
        if (cf.RuleType != CfRuleType.Top10 || rankedValues is null || rankedValues.Count == 0)
            return null;

        var take = Math.Clamp(
            cf.TopBottomPercent
                ? (int)Math.Ceiling(rankedValues.Count * Math.Max(1, cf.TopBottomRank) / 100d)
                : cf.TopBottomRank,
            1,
            rankedValues.Count);
        rankedValues.Sort(cf.AboveAverage
            ? static (left, right) =>
            {
                var valueOrder = right.Value.CompareTo(left.Value);
                return valueOrder != 0 ? valueOrder : left.Index.CompareTo(right.Index);
            }
            : static (left, right) =>
            {
                var valueOrder = left.Value.CompareTo(right.Value);
                return valueOrder != 0 ? valueOrder : left.Index.CompareTo(right.Index);
            });

        var result = new HashSet<CellAddress>(take);
        for (var i = 0; i < take; i++)
            result.Add(rankedValues[i].Address);

        return result;
    }

    private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateAllAggregateValues(
        Sheet sheet,
        ConditionalFormat cf)
    {
        foreach (var range in cf.AllRanges)
        {
            foreach (var item in EnumerateAggregateValues(sheet, range))
                yield return item;
        }
    }

    private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateAggregateValues(
        Sheet sheet,
        GridRange range)
    {
        const long denseScanLimit = 10_000;
        if (range.CellCount <= denseScanLimit)
        {
            foreach (var address in range.AllCells())
                yield return (address, sheet.GetValue(address));
            yield break;
        }

        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            if (range.Contains(address))
                yield return (address, cell.Value);
        }
    }

    private static bool MatchesCellValue(
        ConditionalFormat cf,
        ScalarValue value,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        CfEvaluationContext cfContext)
    {
        if (TryGetDouble(value, out double d))
        {
            if (!TryResolveCellValueNumericThreshold(
                    cf,
                    cf.Value1,
                    CfThresholdFormulaSlot.CellValue1,
                    sheet,
                    workbook,
                    addr,
                    cfContext,
                    out double v1))
            {
                return false;
            }

            return cf.Operator switch
            {
                CfOperator.Equal => d == v1,
                CfOperator.NotEqual => d != v1,
                CfOperator.GreaterThan => d > v1,
                CfOperator.GreaterThanOrEqual => d >= v1,
                CfOperator.LessThan => d < v1,
                CfOperator.LessThanOrEqual => d <= v1,
                CfOperator.Between => TryResolveCellValueNumericThreshold(
                    cf,
                    cf.Value2,
                    CfThresholdFormulaSlot.CellValue2,
                    sheet,
                    workbook,
                    addr,
                    cfContext,
                    out double v2) && d >= v1 && d <= v2,
                CfOperator.NotBetween => TryResolveCellValueNumericThreshold(
                    cf,
                    cf.Value2,
                    CfThresholdFormulaSlot.CellValue2,
                    sheet,
                    workbook,
                    addr,
                    cfContext,
                    out double v2b) && !(d >= v1 && d <= v2b),
                _ => false
            };
        }

        var s = GetString(value);
        return cf.Operator switch
        {
            CfOperator.Equal => string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
            CfOperator.NotEqual => !string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryResolveCellValueNumericThreshold(
        ConditionalFormat cf,
        string? text,
        CfThresholdFormulaSlot slot,
        Sheet sheet,
        Workbook workbook,
        CellAddress currentCell,
        CfEvaluationContext cfContext,
        out double value)
    {
        if (TryParseDouble(text, out value))
            return true;

        if (GetStaticThresholdFormulaValue(cfContext, cf, slot) is { } staticValue && double.IsFinite(staticValue))
        {
            value = staticValue;
            return true;
        }

        if (!TryResolveCellValueScalarThreshold(cf, slot, sheet, workbook, currentCell, cfContext, out var scalar) ||
            !TryGetDouble(scalar, out value) ||
            !double.IsFinite(value))
        {
            value = 0;
            return false;
        }

        return true;
    }

    private static bool TryResolveCellValueScalarThreshold(
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        Sheet sheet,
        Workbook workbook,
        CellAddress currentCell,
        CfEvaluationContext cfContext,
        out ScalarValue value)
    {
        if (GetStaticThresholdFormulaValue(cfContext, cf, slot) is { } staticValue && double.IsFinite(staticValue))
        {
            value = new NumberValue(staticValue);
            return true;
        }

        var formulaAst = GetThresholdFormula(cfContext, cf, slot);
        if (formulaAst is null)
        {
            value = BlankValue.Instance;
            return false;
        }

        try
        {
            var shiftedAst = GetShiftedConditionalFormatFormula(formulaAst, cf.AppliesTo.Start, currentCell);
            value = ThresholdFormulaEvaluator.Evaluate(shiftedAst, sheet, workbook, currentCell);
            return value is not ErrorValue;
        }
        catch
        {
            value = BlankValue.Instance;
            return false;
        }
    }

    private static bool MatchesAboveAverage(
        ConditionalFormat cf,
        ScalarValue value,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache)
    {
        if (!TryGetDouble(value, out double cellVal)) return false;
        if (!cfCache.TryGetValue(cf, out var cache)) return false;

        // "N standard deviations above/below average" band: threshold is mean ± N*stdDev
        // instead of the plain mean. Falls back to the plain average when stdDev is
        // unavailable (e.g. fewer than 2 numeric points in the range).
        var threshold = cache.Average;
        if (cf.StdDevCount is { } n && n > 0)
            threshold = cf.AboveAverage
                ? cache.Average + n * cache.StdDev
                : cache.Average - n * cache.StdDev;

        return cf.AboveAverage
            ? (cf.EqualAverage ? cellVal >= threshold : cellVal > threshold)
            : (cf.EqualAverage ? cellVal <= threshold : cellVal < threshold);
    }

    private static bool MatchesTopBottom(
        ConditionalFormat cf,
        CellAddress addr,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache) =>
        cfCache.TryGetValue(cf, out var cache) &&
        cache.TopBottomMatches?.Contains(addr) == true;

    private static bool MatchesDuplicateState(
        ConditionalFormat cf,
        ScalarValue value,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache,
        bool duplicate)
    {
        // Blanks are never considered duplicates or unique values (matches Excel behavior).
        if (IsBlankValue(value))
            return false;

        if (!cfCache.TryGetValue(cf, out var cache) || cache.ValueCounts is null)
            return false;

        var occurrences = cache.ValueCounts.GetValueOrDefault(GetDuplicateValueKey(value));
        return duplicate ? occurrences > 1 : occurrences == 1;
    }

    private enum TextRuleMatchKind { Contains, NotContains, BeginsWith, EndsWith }

    private static bool MatchesTextRule(ConditionalFormat cf, ScalarValue value, TextRuleMatchKind kind)
    {
        if (string.IsNullOrEmpty(cf.TextRuleText))
            return false;

        var text = GetString(value);
        return kind switch
        {
            TextRuleMatchKind.Contains => text.Contains(cf.TextRuleText, StringComparison.OrdinalIgnoreCase),
            TextRuleMatchKind.NotContains => !text.Contains(cf.TextRuleText, StringComparison.OrdinalIgnoreCase),
            TextRuleMatchKind.BeginsWith => text.StartsWith(cf.TextRuleText, StringComparison.OrdinalIgnoreCase),
            TextRuleMatchKind.EndsWith => text.EndsWith(cf.TextRuleText, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool MatchesDateOccurring(ConditionalFormat cf, ScalarValue value, DateTime today)
    {
        if (value is not DateTimeValue dateValue)
            return false;

        var date = dateValue.ToDateTime().Date;
        today = today.Date;

        return (cf.DateOccurringPeriod ?? "today") switch
        {
            "yesterday" => date == today.AddDays(-1),
            "today" => date == today,
            "tomorrow" => date == today.AddDays(1),
            "last7Days" => date >= today.AddDays(-6) && date <= today,
            "lastWeek" => IsWithinWeek(date, StartOfWeek(today).AddDays(-7)),
            "thisWeek" => IsWithinWeek(date, StartOfWeek(today)),
            "nextWeek" => IsWithinWeek(date, StartOfWeek(today).AddDays(7)),
            "lastMonth" => MatchesMonth(date, today.AddMonths(-1)),
            "thisMonth" => MatchesMonth(date, today),
            "nextMonth" => MatchesMonth(date, today.AddMonths(1)),
            _ => date == today
        };
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.AddDays(-offset).Date;
    }

    private static bool IsWithinWeek(DateTime date, DateTime weekStart) =>
        date >= weekStart && date < weekStart.AddDays(7);

    private static bool MatchesMonth(DateTime date, DateTime target) =>
        date.Year == target.Year && date.Month == target.Month;

    private static string GetString(ScalarValue value) => value switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => d.ToDateTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        ErrorValue e => e.Code,
        _ => ""
    };

    private static bool IsBlankValue(ScalarValue value) =>
        value is BlankValue || value is TextValue { Value.Length: 0 };

    private static string NormalizeDisplayValue(ScalarValue value) =>
        GetString(value).Trim();

    /// <summary>
    /// Key used to bucket cell values for Duplicate/Unique Values occurrence counting.
    /// Excel keys duplicate detection by the underlying value AND type: a numeric 1 and the
    /// text "1" are different values, and a boolean TRUE is different from the text "TRUE" -
    /// even though they render the same display string. Dates and numbers share a bucket
    /// (Excel stores dates as numeric serials internally, so a date and the equal-valued
    /// number ARE the same value), but everything else is tagged by its value kind so a
    /// type-erased display string can never collide across kinds.
    /// </summary>
    private static string GetDuplicateValueKey(ScalarValue value) => value switch
    {
        // Numbers and dates share the "N" bucket keyed by the raw serial value (Excel stores
        // dates as numeric serials internally, so a date and the equal-valued number ARE the
        // same value) rather than by GetString's formatted display text, which differs between
        // the two (a plain number vs. "yyyy-MM-dd") even for the same underlying value.
        NumberValue n => "N:" + n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => "N:" + d.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        BoolValue => "B:" + NormalizeDisplayValue(value),
        TextValue => "T:" + NormalizeDisplayValue(value),
        ErrorValue => "E:" + NormalizeDisplayValue(value),
        _ => "?:" + NormalizeDisplayValue(value)
    };
}
