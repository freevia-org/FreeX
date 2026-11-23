using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal static partial class ViewportConditionalFormatEvaluator
{
    private static Dictionary<ConditionalFormat, CfAggregateCache> PrecomputeAggregates(
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell>? occupiedCells)
    {
        Dictionary<ConditionalFormat, CfAggregateCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (!RequiresAggregateCache(cf))
                continue;

            var retainSortedValues = RequiresSortedNumericValues(cf);
            var statistics = new ConditionalFormatEvaluationMath.StatisticsAccumulator(retainSortedValues);
            List<(CellAddress Address, double Value, int Index)>? rankedValues =
                cf.RuleType == CfRuleType.Top10 ? [] : null;
            Dictionary<string, int>? valueCounts =
                cf.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : null;
            long populatedCells = 0;
            var hasError = false;
            foreach (var (a, v) in EnumerateAllAggregateValues(sheet, cf, occupiedCells))
            {
                if (v is ErrorValue)
                    hasError = true;

                if (valueCounts is not null && v is not BlankValue)
                {
                    populatedCells++;
                    var key = GetDuplicateValueKey(v);
                    valueCounts[key] = valueCounts.GetValueOrDefault(key) + 1;
                }

                if (TryGetDouble(v, out double x))
                {
                    rankedValues?.Add((a, x, statistics.Count));
                    statistics.Add(x);
                }
            }

            if (valueCounts is not null)
            {
                // Excel-parity round 6: a text cell holding "" is an ordinary value for Duplicate/Unique
                // Values (it is a duplicate of any other empty cell in the range, and unique when it is the
                // only one), while a truly empty cell is never highlighted itself but still counts as an
                // "empty" occurrence. Account for the empty cells that have no entry of their own.
                long rangeCells = 0;
                foreach (var range in cf.AllRanges)
                    rangeCells += range.CellCount;
                var emptyCells = rangeCells - populatedCells;
                if (emptyCells > 0)
                    valueCounts[GetDuplicateValueKey(new TextValue(string.Empty))] = valueCounts.GetValueOrDefault(GetDuplicateValueKey(new TextValue(string.Empty))) + (int)Math.Min(emptyCells, int.MaxValue / 2);
            }

            var topBottomMatches = ResolveTopBottomMatches(cf, rankedValues);
            var aggregate = statistics.Build();
            if (aggregate.Count > 0 || valueCounts?.Count > 0 || topBottomMatches is not null)
            {
                (result ??= new Dictionary<ConditionalFormat, CfAggregateCache>(ReferenceEqualityComparer.Instance))[cf] = new CfAggregateCache(
                    aggregate.Count,
                    aggregate.Average,
                    aggregate.Min,
                    aggregate.Max,
                    aggregate.SortedValues,
                    topBottomMatches,
                    valueCounts?.Count > 0 ? valueCounts : null,
                    aggregate.StdDev,
                    hasError);
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
        var iconCount = ConditionalFormatEvaluationMath.GetIconSetCount(cf.IconSetStyle);
        var thresholdCount = iconCount - 1;
        var thresholdStartIndex = ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(cf.IconSetThresholds.Count, iconCount);
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

        var iconCount = ConditionalFormatEvaluationMath.GetIconSetCount(cf.IconSetStyle);
        var thresholdStartIndex = ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(cf.IconSetThresholds.Count, iconCount);
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
                // Excel-parity round 6: "Top N%" keeps floor(count * N / 100) cells (at least one), not the ceiling.
                ? (int)Math.Floor(rankedValues.Count * Math.Max(1, cf.TopBottomRank) / 100d)
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
        ConditionalFormat cf,
        IReadOnlyDictionary<(uint Row, uint Col), Cell>? occupiedCells)
    {
        if (occupiedCells is not null)
        {
            foreach (var ((row, col), cell) in occupiedCells)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (cf.Contains(address))
                    yield return (address, cell.Value);
            }

            yield break;
        }

        // A rule's sqref can list multiple ranges that overlap each other (e.g. "A1:B2 B2:C3"),
        // and Excel treats the covered cell set as a set — each cell counted once regardless of
        // how many of the rule's ranges include it. Without de-duplication a cell in the overlap
        // is visited once per covering range, skewing sum/average/stdDev/count and distorting the
        // Top10 ranking and percentile/percent thresholds. Single-range rules (the common case)
        // never allocate the tracking set.
        HashSet<CellAddress>? seen = null;
        var additionalRanges = cf.AdditionalRanges;
        var multiRange = additionalRanges is { Count: > 0 };
        var additionalRangeCount = additionalRanges?.Count ?? 0;
        for (var rangeIndex = -1; rangeIndex < additionalRangeCount; rangeIndex++)
        {
            var range = rangeIndex < 0
                ? cf.AppliesTo
                : additionalRanges![rangeIndex];
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
        // Excel-parity round 6 (measured against Range.DisplayFormat): a "Cell Value" rule is a plain
        // worksheet comparison of the cell against its operand(s) -- numbers < text < logicals, a blank
        // cell takes the type of the operand it is compared with (0, "" or FALSE), text compares
        // case-insensitively, and an error cell never matches (not even "not equal to").
        if (value is ErrorValue)
            return false;

        if (!TryResolveCellValueOperand(cf, cf.Value1, CfThresholdFormulaSlot.CellValue1, sheet, workbook, addr, cfContext, out var operand1))
            return false;

        switch (cf.Operator)
        {
            case CfOperator.Equal:
                return CompareCellValueOperands(value, operand1) == 0;
            case CfOperator.NotEqual:
                return CompareCellValueOperands(value, operand1) != 0;
            case CfOperator.GreaterThan:
                return CompareCellValueOperands(value, operand1) > 0;
            case CfOperator.GreaterThanOrEqual:
                return CompareCellValueOperands(value, operand1) >= 0;
            case CfOperator.LessThan:
                return CompareCellValueOperands(value, operand1) < 0;
            case CfOperator.LessThanOrEqual:
                return CompareCellValueOperands(value, operand1) <= 0;
            case CfOperator.Between:
            case CfOperator.NotBetween:
            {
                if (!TryResolveCellValueOperand(cf, cf.Value2, CfThresholdFormulaSlot.CellValue2, sheet, workbook, addr, cfContext, out var operand2))
                    return false;

                // Excel treats the two operands as an unordered pair: "between 10 and 8" matches 8..10.
                var swap = CompareCellValueOperands(operand1, operand2) > 0;
                var low = swap ? operand2 : operand1;
                var high = swap ? operand1 : operand2;
                var inside = CompareCellValueOperands(value, low) >= 0 && CompareCellValueOperands(value, high) <= 0;
                return cf.Operator == CfOperator.Between ? inside : !inside;
            }
            default:
                return false;
        }
    }

    private static bool TryResolveCellValueOperand(
        ConditionalFormat cf,
        string? text,
        CfThresholdFormulaSlot slot,
        Sheet sheet,
        Workbook workbook,
        CellAddress currentCell,
        CfEvaluationContext cfContext,
        out ScalarValue operand)
    {
        if (TryParseDouble(text, out var number))
        {
            operand = new NumberValue(number);
            return true;
        }

        if (TryResolveCellValueScalarThreshold(cf, slot, sheet, workbook, currentCell, cfContext, out var scalar))
        {
            operand = scalar;
            return scalar is not RangeValue;
        }

        operand = BlankValue.Instance;
        // A parsed formula that failed to evaluate (error result) never matches.
        if (GetThresholdFormula(cfContext, cf, slot) is not null || text is null)
            return false;

        operand = new TextValue(UnquoteLiteral(text) ?? string.Empty);
        return true;
    }

    private static int CompareCellValueOperands(ScalarValue left, ScalarValue right)
    {
        if (left is BlankValue && right is not BlankValue)
            left = CoerceBlankLike(right);
        else if (right is BlankValue && left is not BlankValue)
            right = CoerceBlankLike(left);

        var leftIsNumber = TryGetDouble(left, out var leftNumber) && left is NumberValue or DateTimeValue;
        var rightIsNumber = TryGetDouble(right, out var rightNumber) && right is NumberValue or DateTimeValue;
        if (leftIsNumber && rightIsNumber)
            return leftNumber.CompareTo(rightNumber);

        if (left is TextValue leftText && right is TextValue rightText)
            return string.Compare(leftText.Value, rightText.Value, StringComparison.OrdinalIgnoreCase);

        if (left is BoolValue leftBool && right is BoolValue rightBool)
            return leftBool.Value.CompareTo(rightBool.Value);

        return CellValueTypeOrder(left).CompareTo(CellValueTypeOrder(right));
    }

    private static ScalarValue CoerceBlankLike(ScalarValue other) => other switch
    {
        TextValue => new TextValue(string.Empty),
        BoolValue => new BoolValue(false),
        _ => new NumberValue(0)
    };

    // Excel's cross-type ordering for comparisons: numbers (and dates) < text < logicals.
    private static int CellValueTypeOrder(ScalarValue value) => value switch
    {
        NumberValue or DateTimeValue => 0,
        TextValue => 1,
        BoolValue => 2,
        _ => 3
    };

    private static string? UnquoteLiteral(string? text)
    {
        if (text is null)
            return null;

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text.Substring(1, text.Length - 2).Replace("\"\"", "\"");

        return text;
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

        // Excel-parity round 6: the plain average skips error cells, but the standard-deviation band of a
        // range that contains an error value is itself an error, so a "N standard deviations" rule
        // highlights nothing in that range.
        if (cache.HasError && cf.StdDevCount.HasValue) return false;

        // "N standard deviations above/below average" band: threshold is mean ± N*stdDev
        // instead of the plain mean. Falls back to the plain average when stdDev is
        // unavailable (e.g. fewer than 2 numeric points in the range).
        return ConditionalFormatEvaluationMath.MatchesAboveAverage(
            cellVal,
            ToEvaluationStatistics(cache),
            cf.AboveAverage,
            cf.EqualAverage,
            cf.StdDevCount);
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
        // A truly empty cell is never highlighted as a duplicate or unique value (matches Excel behavior);
        // a text "" cell is a normal value that shares its key with the empty cells.
        if (value is BlankValue)
            return false;

        if (!cfCache.TryGetValue(cf, out var cache) || cache.ValueCounts is null)
            return false;

        var occurrences = cache.ValueCounts.GetValueOrDefault(GetDuplicateValueKey(value));
        return duplicate ? occurrences > 1 : occurrences == 1;
    }

    private enum TextRuleMatchKind { Contains, NotContains, BeginsWith, EndsWith }

    private static bool MatchesTextRule(ConditionalFormat cf, ScalarValue value, TextRuleMatchKind kind)
    {
        // Excel-parity round 6 (measured against Range.DisplayFormat): Excel builds these rules from
        // formulas, and an empty or wildcard search text is legal in them --
        //   Contains     NOT(ISERROR(SEARCH(text,cell)))   -- SEARCH is case-insensitive, supports ? * ~
        //                wildcards, searches the cell's text (numbers/booleans included), and SEARCH("", x)
        //                finds position 1 except in an empty cell (#VALUE!)
        //   NotContains  ISERROR(SEARCH(text,cell))
        //   BeginsWith   LEFT(cell,LEN(text))=text
        //   EndsWith     RIGHT(cell,LEN(text))=text
        // An error cell makes SEARCH/LEFT/RIGHT return an error: Contains/Begins/Ends never fire and
        // NotContains always does.
        var search = cf.TextRuleText ?? string.Empty;
        if (value is ErrorValue)
            return kind == TextRuleMatchKind.NotContains;

        var text = GetString(value);
        switch (kind)
        {
            case TextRuleMatchKind.Contains:
                return SearchFindsText(search, text);
            case TextRuleMatchKind.NotContains:
                return !SearchFindsText(search, text);
            case TextRuleMatchKind.BeginsWith:
                return text.Length >= search.Length && text.StartsWith(search, StringComparison.OrdinalIgnoreCase);
            case TextRuleMatchKind.EndsWith:
                return text.Length >= search.Length && text.EndsWith(search, StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    // SEARCH(find, within) succeeds: case-insensitive, ? matches one character, * any run, ~ escapes.
    private static bool SearchFindsText(string find, string within)
    {
        // SEARCH over an empty cell/string is #VALUE! whatever the pattern (even "" or "*").
        if (within.Length == 0)
            return false;

        if (find.Length == 0)
            return true;

        if (find.IndexOfAny(['?', '*', '~']) < 0)
            return within.Contains(find, StringComparison.OrdinalIgnoreCase);

        var pattern = new System.Text.StringBuilder();
        for (var i = 0; i < find.Length; i++)
        {
            var c = find[i];
            if (c == '~' && i + 1 < find.Length && find[i + 1] is '?' or '*' or '~')
            {
                pattern.Append(System.Text.RegularExpressions.Regex.Escape(find[i + 1].ToString()));
                i++;
            }
            else if (c == '?')
            {
                pattern.Append('.');
            }
            else if (c == '*')
            {
                pattern.Append(".*");
            }
            else
            {
                pattern.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
            }
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            within,
            pattern.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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

    private static bool IsPlainNumberText(string text) =>
        text.Length > 0 &&
        text.Length == text.Trim().Length &&
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed);

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
        // Excel-parity round 6 (measured with Range.DisplayFormat): duplicate detection compares what the
        // cells display, so the text "1" and the number 1 are the same value, while surrounding spaces
        // are significant ("a " is not "a").
        TextValue t when IsPlainNumberText(t.Value) => "N:" + NormalizeZero(double.Parse(t.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        TextValue t => "T:" + t.Value,
        ErrorValue => "E:" + NormalizeDisplayValue(value),
        _ => "?:" + NormalizeDisplayValue(value)
    };
}
