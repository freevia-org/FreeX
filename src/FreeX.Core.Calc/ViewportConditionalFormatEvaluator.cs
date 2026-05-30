using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal sealed record CfAggregateCache(
    double Average,
    double Min,
    double Max,
    IReadOnlySet<CellAddress>? TopBottomMatches = null,
    IReadOnlyDictionary<string, int>? ValueCounts = null);

internal sealed record CfEvaluationContext(
    IReadOnlyList<ConditionalFormat> RulesByPriority,
    IReadOnlyList<ConditionalFormat> IconRulesByPriority,
    Dictionary<ConditionalFormat, CfAggregateCache> Aggregates,
    Dictionary<ConditionalFormat, CfFormulaCache> Formulas);

internal sealed record CfFormulaCache(
    FormulaNode Ast,
    Dictionary<(int RowDelta, int ColDelta), FormulaNode> ShiftedAsts);

internal static class ViewportConditionalFormatEvaluator
{
    private static readonly ConditionalFormat[] EmptyRules = [];
    private static readonly Dictionary<ConditionalFormat, CfAggregateCache> EmptyAggregates = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<ConditionalFormat, CfFormulaCache> EmptyFormulas = new(ReferenceEqualityComparer.Instance);
    private static readonly CfEvaluationContext EmptyContext = new(
        EmptyRules,
        EmptyRules,
        EmptyAggregates,
        EmptyFormulas);

    public static CfEvaluationContext BuildContext(Sheet sheet)
    {
        if (sheet.ConditionalFormats.Count == 0)
            return EmptyContext;

        var rulesByPriority = sheet.ConditionalFormats
            .OrderBy(cf => cf.Priority)
            .ToArray();
        var iconRulesByPriority = rulesByPriority
            .Where(cf => cf.RuleType == CfRuleType.IconSet)
            .ToArray();

        return new CfEvaluationContext(
            rulesByPriority,
            iconRulesByPriority,
            PrecomputeAggregates(sheet),
            PrecomputeFormulaCaches(sheet));
    }

    public static CellStyle? Evaluate(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        Func<ConditionalFormat, Sheet, CellAddress, Workbook, CfEvaluationContext, bool> matchesFormula)
    {
        if (cfContext.RulesByPriority.Count == 0)
            return null;

        foreach (var cf in cfContext.RulesByPriority)
        {
            if (!cf.AppliesTo.Contains(addr))
                continue;

            if (cf.RuleType == CfRuleType.ColorScale)
                return ComputeColorScaleStyle(cf, value, cfContext.Aggregates);
            if (cf.RuleType == CfRuleType.DataBar)
                return new CellStyle { FillColor = cf.DataBarColor.ToCellColor() };

            bool conditionMet = cf.RuleType switch
            {
                CfRuleType.CellValue => MatchesCellValue(cf, value),
                CfRuleType.AboveAverage => MatchesAboveAverage(cf, value, cfContext.Aggregates),
                CfRuleType.Formula => matchesFormula(cf, sheet, addr, workbook, cfContext),
                CfRuleType.Top10 => MatchesTopBottom(cf, addr, cfContext.Aggregates),
                CfRuleType.DuplicateValues => MatchesDuplicateState(cf, value, cfContext.Aggregates, duplicate: true),
                CfRuleType.UniqueValues => MatchesDuplicateState(cf, value, cfContext.Aggregates, duplicate: false),
                CfRuleType.ContainsText => MatchesTextRule(cf, value, TextRuleMatchKind.Contains),
                CfRuleType.NotContainsText => MatchesTextRule(cf, value, TextRuleMatchKind.NotContains),
                CfRuleType.BeginsWith => MatchesTextRule(cf, value, TextRuleMatchKind.BeginsWith),
                CfRuleType.EndsWith => MatchesTextRule(cf, value, TextRuleMatchKind.EndsWith),
                CfRuleType.DateOccurring => MatchesDateOccurring(cf, value, DateTime.Today),
                CfRuleType.Blanks => IsBlankValue(value),
                CfRuleType.NoBlanks => !IsBlankValue(value),
                CfRuleType.Errors => value is ErrorValue,
                CfRuleType.NoErrors => value is not ErrorValue,
                _ => false
            };

            if (conditionMet)
                return cf.FormatIfTrue;
        }

        return null;
    }

    public static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle)
    {
        var result = (baseStyle ?? CellStyle.Default).Clone();

        if (cfStyle.FillColor.HasValue)
            result.FillColor = cfStyle.FillColor;
        if (cfStyle.FillPatternStyle != CellFillPatternStyle.None)
            result.FillPatternStyle = cfStyle.FillPatternStyle;
        if (cfStyle.FillPatternColor.HasValue)
            result.FillPatternColor = cfStyle.FillPatternColor;

        if (cfStyle.Bold)
            result.Bold = true;
        if (cfStyle.Italic)
            result.Italic = true;
        if (cfStyle.Underline)
            result.Underline = true;
        if (cfStyle.FontColor != CellColor.Black)
            result.FontColor = cfStyle.FontColor;

        return result;
    }

    private static Dictionary<ConditionalFormat, CfFormulaCache> PrecomputeFormulaCaches(Sheet sheet)
    {
        var result = new Dictionary<ConditionalFormat, CfFormulaCache>(ReferenceEqualityComparer.Instance);
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.Formula || string.IsNullOrWhiteSpace(cf.FormulaText))
                continue;

            try
            {
                var ast = new Parser(new Lexer("=" + cf.FormulaText).Tokenize()).Parse();
                result[cf] = new CfFormulaCache(ast, []);
            }
            catch
            {
                // Preserve formula CF error handling: invalid formulas do not match.
            }
        }

        return result;
    }

    public static bool TryGetDouble(ScalarValue value, out double result)
    {
        if (value is NumberValue nv) { result = nv.Value; return true; }
        if (value is DateTimeValue dv) { result = dv.Value; return true; }
        result = 0;
        return false;
    }

    public static bool TryParseDouble(string? text, out double result)
    {
        if (text is null) { result = 0; return false; }
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private static Dictionary<ConditionalFormat, CfAggregateCache> PrecomputeAggregates(Sheet sheet)
    {
        var result = new Dictionary<ConditionalFormat, CfAggregateCache>(ReferenceEqualityComparer.Instance);
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType is not (
                CfRuleType.AboveAverage or
                CfRuleType.ColorScale or
                CfRuleType.IconSet or
                CfRuleType.Top10 or
                CfRuleType.DuplicateValues or
                CfRuleType.UniqueValues))
                continue;

            double sum = 0, min = double.MaxValue, max = double.MinValue;
            int count = 0;
            List<(CellAddress Address, double Value)>? rankedValues =
                cf.RuleType == CfRuleType.Top10 ? [] : null;
            Dictionary<string, int>? valueCounts =
                cf.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : null;
            foreach (var (a, v) in EnumerateAggregateValues(sheet, cf.AppliesTo))
            {
                if (valueCounts is not null)
                {
                    var key = NormalizeDisplayValue(v);
                    valueCounts[key] = valueCounts.GetValueOrDefault(key) + 1;
                }

                if (TryGetDouble(v, out double x))
                {
                    sum += x;
                    if (x < min) min = x;
                    if (x > max) max = x;
                    count++;
                    rankedValues?.Add((a, x));
                }
            }

            var topBottomMatches = ResolveTopBottomMatches(cf, rankedValues);
            if (count > 0 || valueCounts?.Count > 0 || topBottomMatches is not null)
                result[cf] = new CfAggregateCache(
                    count > 0 ? sum / count : 0,
                    count > 0 ? min : 0,
                    count > 0 ? max : 0,
                    topBottomMatches,
                    valueCounts?.Count > 0 ? valueCounts : null);
        }
        return result;
    }

    private static IReadOnlySet<CellAddress>? ResolveTopBottomMatches(
        ConditionalFormat cf,
        IReadOnlyList<(CellAddress Address, double Value)>? rankedValues)
    {
        if (cf.RuleType != CfRuleType.Top10 || rankedValues is null || rankedValues.Count == 0)
            return null;

        var take = Math.Clamp(
            cf.TopBottomPercent
                ? (int)Math.Ceiling(rankedValues.Count * Math.Max(1, cf.TopBottomRank) / 100d)
                : cf.TopBottomRank,
            1,
            rankedValues.Count);
        var ordered = cf.AboveAverage
            ? rankedValues.OrderByDescending(item => item.Value)
            : rankedValues.OrderBy(item => item.Value);
        return ordered.Take(take)
            .Select(item => item.Address)
            .ToHashSet();
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

    private static bool MatchesCellValue(ConditionalFormat cf, ScalarValue value)
    {
        if (TryGetDouble(value, out double d))
        {
            if (!TryParseDouble(cf.Value1, out double v1)) return false;

            return cf.Operator switch
            {
                CfOperator.Equal => d == v1,
                CfOperator.NotEqual => d != v1,
                CfOperator.GreaterThan => d > v1,
                CfOperator.GreaterThanOrEqual => d >= v1,
                CfOperator.LessThan => d < v1,
                CfOperator.LessThanOrEqual => d <= v1,
                CfOperator.Between => TryParseDouble(cf.Value2, out double v2) && d >= v1 && d <= v2,
                CfOperator.NotBetween => TryParseDouble(cf.Value2, out double v2b) && !(d >= v1 && d <= v2b),
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

    private static bool MatchesAboveAverage(
        ConditionalFormat cf,
        ScalarValue value,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache)
    {
        if (!TryGetDouble(value, out double cellVal)) return false;
        if (!cfCache.TryGetValue(cf, out var cache)) return false;
        return cf.AboveAverage ? cellVal > cache.Average : cellVal < cache.Average;
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
        if (!cfCache.TryGetValue(cf, out var cache) || cache.ValueCounts is null)
            return false;

        var occurrences = cache.ValueCounts.GetValueOrDefault(NormalizeDisplayValue(value));
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

    private static CellStyle? ComputeColorScaleStyle(
        ConditionalFormat cf,
        ScalarValue value,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache)
    {
        if (!TryGetDouble(value, out double cellVal)) return null;
        if (!double.IsFinite(cellVal)) return null;
        if (!cfCache.TryGetValue(cf, out var cache)) return null;

        double min = cache.Min, max = cache.Max;
        if (max == min) return new CellStyle { FillColor = cf.MinColor.ToCellColor() };

        double t = (cellVal - min) / (max - min);
        var interpolated = cf.UseThreeColorScale
            ? t <= 0.5
                ? Lerp(cf.MinColor, cf.MidColor, t * 2)
                : Lerp(cf.MidColor, cf.MaxColor, (t - 0.5) * 2)
            : Lerp(cf.MinColor, cf.MaxColor, t);

        return new CellStyle { FillColor = interpolated };
    }

    private static CellColor Lerp(RgbColor a, RgbColor b, double t)
    {
        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new CellColor(r, g, bl);
    }

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
}
