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

        // Excel highlights every cell whose value ranks within the top/bottom N, ties
        // included -- so once the Nth-ranked value is known, extend the cutoff to cover
        // any later entries that tie its value (more than N cells can end up matched).
        var cutoffValue = rankedValues[take - 1].Value;
        var effectiveTake = take;
        while (effectiveTake < rankedValues.Count && rankedValues[effectiveTake].Value == cutoffValue)
            effectiveTake++;

        var result = new HashSet<CellAddress>(effectiveTake);
        for (var i = 0; i < effectiveTake; i++)
            result.Add(rankedValues[i].Address);

        return result;
    }

    private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateAllAggregateValues(
        Sheet sheet,
        ConditionalFormat cf)
    {
        // A rule's sqref can list multiple ranges that overlap each other (e.g. "A1:B2 B2:C3"),
        // and Excel treats the covered cell set as a set — each cell counted once regardless of
        // how many of the rule's ranges include it. Without de-duplication a cell in the overlap
        // is visited once per covering range, skewing sum/average/stdDev/count and distorting the
        // Top10 ranking and percentile/percent thresholds. Single-range rules (the common case)
        // never allocate the tracking set.
        HashSet<CellAddress>? seen = null;
        var multiRange = cf.AllRanges.Count() > 1;
        foreach (var range in cf.AllRanges)
        {
            foreach (var item in EnumerateAggregateValues(sheet, range))
            {
                if (multiRange)
                {
                    seen ??= new HashSet<CellAddress>();
                    if (!seen.Add(item.Address))
                        continue;
                }

                yield return item;
            }
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
        if (cf.Operator is not (CfOperator.Equal or CfOperator.NotEqual))
            return false;

        // Excel never treats a text cell value as equal to a numeric CellIs comparand -- e.g.
        // typing ="5"=5 evaluates to FALSE even though the text and the number "look the same".
        // Only compare as strings when the comparand itself is genuinely textual (a quoted
        // literal, or a formula/cell-reference that resolves to text); a numeric comparand always
        // fails Equal and always satisfies NotEqual against a text cell.
        if (IsNumericCellValueComparand(cf, sheet, workbook, addr, cfContext))
            return cf.Operator == CfOperator.NotEqual;

        var threshold = ResolveCellValueTextThreshold(cf, sheet, workbook, addr, cfContext);
        var isEqual = threshold is not null && string.Equals(s, threshold, StringComparison.OrdinalIgnoreCase);
        return cf.Operator == CfOperator.Equal ? isEqual : !isEqual;
    }

    /// <summary>
    /// Determines whether a CellIs Equal/NotEqual rule's Value1 comparand resolves to a genuine
    /// number rather than text, so <see cref="MatchesCellValue"/> can apply Excel's "text never
    /// equals a number" rule instead of falling into a coincidental case-insensitive string match.
    /// </summary>
    private static bool IsNumericCellValueComparand(
        ConditionalFormat cf,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        CfEvaluationContext cfContext)
    {
        if (TryResolveCellValueScalarThreshold(cf, CfThresholdFormulaSlot.CellValue1, sheet, workbook, addr, cfContext, out var scalar))
            return TryGetDouble(scalar, out _);

        // No parsed formula cache entry: Value1 is either blank, or a bare (unquoted) literal that
        // parsed directly as a double -- TryAddCellValueFormulaCache deliberately skips caching a
        // formula in exactly that case, since a bare numeric literal is Excel's normal encoding for
        // a numeric CellIs comparand (e.g. Value1="5" with no surrounding quotes).
        return cf.Value1 is { } raw &&
               !(raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') &&
               TryParseDouble(raw, out _);
    }

    /// <summary>
    /// Resolves the text comparison threshold for a CellIs "equal to"/"not equal to" rule. Excel
    /// stores a literal text comparand as a quoted formula string (e.g. <c>"abc"</c>) and a cell
    /// reference/formula comparand as bare formula text (e.g. <c>$B$1</c>); both are parsed into
    /// the same threshold-formula cache used by the numeric branch above
    /// (<see cref="TryResolveCellValueScalarThreshold"/>), so evaluate through that cache here too
    /// instead of comparing the cell's display text against the raw, still-quoted formula source.
    /// </summary>
    private static string? ResolveCellValueTextThreshold(
        ConditionalFormat cf,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        CfEvaluationContext cfContext)
    {
        if (TryResolveCellValueScalarThreshold(cf, CfThresholdFormulaSlot.CellValue1, sheet, workbook, addr, cfContext, out var scalar))
            return GetString(scalar);

        // No parsed formula cache entry (e.g. Value1 is null/blank) — fall back to the raw text,
        // unwrapping an Excel quoted-string literal like "abc" to its literal content.
        return UnquoteLiteral(cf.Value1);
    }

    private static string? UnquoteLiteral(string? text)
    {
        if (text is null)
            return null;

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text.Substring(1, text.Length - 2).Replace("\"\"", "\"");

        return text;
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

        // Excel's Contains/BeginsWith/EndsWith rules are effectively ISERROR-gated (e.g.
        // Contains is NOT(ISERROR(SEARCH(...)))): an error value propagates through SEARCH so
        // ISERROR is TRUE and the rule never fires. NotContains is the complement, so an error
        // cell always satisfies it. Guard before GetString turns the error's code text (e.g.
        // "#DIV/0!") into a spurious substring match target.
        if (value is ErrorValue)
            return kind == TextRuleMatchKind.NotContains;

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
        // Like every other rule matcher in this file, accept both NumberValue and DateTimeValue
        // (via TryGetDouble): date arithmetic (e.g. =A1+1) always decays to a plain NumberValue
        // holding the OADate serial, and Excel highlights it the same as a literal date cell.
        if (!TryGetDouble(value, out double serial))
            return false;

        DateTime date;
        try
        {
            date = DateTime.FromOADate(serial).Date;
        }
        catch (ArgumentException)
        {
            return false;
        }

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
        // Excel's cfRule timePeriod week formulas are WEEKDAY()-based with the default
        // (Sunday=1) return type, so "this/last/next week" spans Sunday..Saturday, not
        // the ISO Monday-start week.
        var offset = (int)date.DayOfWeek - (int)DayOfWeek.Sunday;
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
        // TryToDateTime, not ToDateTime: an out-of-range serial (loaded file, date arithmetic)
        // must not crash evaluating a "Duplicate Values"/"Contains Text"/date-timePeriod
        // conditional format rule over the viewport. Fall back to the raw serial text, matching
        // FilterValueFormatter.ToText's established fallback for the same situation.
        DateTimeValue d => d.TryToDateTime(out var dt)
            ? dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
    private static double NormalizeZero(double value) => value == 0 ? 0.0 : value;

    private static string GetDuplicateValueKey(ScalarValue value) => value switch
    {
        // Numbers and dates share the "N" bucket keyed by the raw serial value (Excel stores
        // dates as numeric serials internally, so a date and the equal-valued number ARE the
        // same value) rather than by GetString's formatted display text, which differs between
        // the two (a plain number vs. "yyyy-MM-dd") even for the same underlying value.
        // Negative zero is normalized to positive zero first: ToString("R") renders -0.0 as
        // "-0", which would otherwise key it separately from 0 even though Excel treats them
        // as the same duplicate-detection value.
        NumberValue n => "N:" + NormalizeZero(n.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => "N:" + NormalizeZero(d.Value).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        BoolValue => "B:" + NormalizeDisplayValue(value),
        TextValue => "T:" + NormalizeDisplayValue(value),
        ErrorValue => "E:" + NormalizeDisplayValue(value),
        _ => "?:" + NormalizeDisplayValue(value)
    };
}
