using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public sealed partial class ViewportService
{
    private static readonly FormulaEvaluator _cfEvaluator = new();

    // ── Per-frame CF aggregate cache ──────────────────────────────────────────

    private sealed record CfAggregateCache(
        double Average,
        double Min,
        double Max,
        IReadOnlySet<CellAddress>? TopBottomMatches = null,
        IReadOnlyDictionary<string, int>? ValueCounts = null);

    private sealed record CfEvaluationContext(
        IReadOnlyList<ConditionalFormat> RulesByPriority,
        IReadOnlyList<ConditionalFormat> IconRulesByPriority,
        Dictionary<ConditionalFormat, CfAggregateCache> Aggregates);

    private static CfEvaluationContext BuildConditionalFormatContext(Sheet sheet)
    {
        if (sheet.ConditionalFormats.Count == 0)
        {
            return new CfEvaluationContext(
                [],
                [],
                new Dictionary<ConditionalFormat, CfAggregateCache>(ReferenceEqualityComparer.Instance));
        }

        var rulesByPriority = sheet.ConditionalFormats
            .OrderBy(cf => cf.Priority)
            .ToArray();
        var iconRulesByPriority = rulesByPriority
            .Where(cf => cf.RuleType == CfRuleType.IconSet)
            .ToArray();

        return new CfEvaluationContext(
            rulesByPriority,
            iconRulesByPriority,
            PrecomputeCfAggregates(sheet));
    }

    /// <summary>
    /// Scans every AboveAverage and ColorScale CF rule once and stores the
    /// aggregate statistics (average, min, max) keyed by rule identity.
    /// Called once per <see cref="GetViewport"/> call to avoid O(cells × range) scans.
    /// </summary>
    private static Dictionary<ConditionalFormat, CfAggregateCache> PrecomputeCfAggregates(Sheet sheet)
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
            var rankedValues = new List<(CellAddress Address, double Value)>();
            var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (a, v) in EnumerateConditionalFormatAggregateValues(sheet, cf.AppliesTo))
            {
                if (cf.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues)
                {
                    var key = NormalizeCfDisplayValue(v);
                    valueCounts[key] = valueCounts.GetValueOrDefault(key) + 1;
                }

                if (TryGetDouble(v, out double x))
                {
                    sum += x;
                    if (x < min) min = x;
                    if (x > max) max = x;
                    count++;
                    if (cf.RuleType == CfRuleType.Top10)
                        rankedValues.Add((a, x));
                }
            }

            IReadOnlySet<CellAddress>? topBottomMatches = null;
            if (cf.RuleType == CfRuleType.Top10 && rankedValues.Count > 0)
            {
                var take = Math.Clamp(
                    cf.TopBottomPercent
                        ? (int)Math.Ceiling(rankedValues.Count * Math.Max(1, cf.TopBottomRank) / 100d)
                        : cf.TopBottomRank,
                    1,
                    rankedValues.Count);
                var ordered = cf.AboveAverage
                    ? rankedValues.OrderByDescending(item => item.Value)
                    : rankedValues.OrderBy(item => item.Value);
                topBottomMatches = ordered.Take(take)
                    .Select(item => item.Address)
                    .ToHashSet();
            }

            if (count > 0 || valueCounts.Count > 0 || topBottomMatches is not null)
                result[cf] = new CfAggregateCache(
                    count > 0 ? sum / count : 0,
                    count > 0 ? min : 0,
                    count > 0 ? max : 0,
                    topBottomMatches,
                    valueCounts.Count > 0 ? valueCounts : null);
        }
        return result;
    }

    private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateConditionalFormatAggregateValues(
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

    private static CellStyle? EvaluateConditionalFormats(
        Sheet sheet, CellAddress addr, ScalarValue value, Workbook workbook,
        CfEvaluationContext cfContext)
    {
        if (cfContext.RulesByPriority.Count == 0)
            return null;

        foreach (var cf in cfContext.RulesByPriority)
        {
            if (!cf.AppliesTo.Contains(addr))
                continue;

            // ColorScale and DataBar always apply when in range — return immediately.
            if (cf.RuleType == CfRuleType.ColorScale)
                return ComputeColorScaleStyle(cf, addr, value, cfContext.Aggregates);
            if (cf.RuleType == CfRuleType.DataBar)
                return new CellStyle { FillColor = cf.DataBarColor.ToCellColor() };

            bool conditionMet = cf.RuleType switch
            {
                CfRuleType.CellValue    => MatchesCellValue(cf, value),
                CfRuleType.AboveAverage => MatchesAboveAverage(cf, addr, value, cfContext.Aggregates),
                CfRuleType.Formula      => MatchesFormula(cf, sheet, addr, workbook),
                CfRuleType.Top10        => MatchesTopBottom(cf, addr, cfContext.Aggregates),
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
                _                       => false
            };

            if (conditionMet)
                return cf.FormatIfTrue; // may be null if rule has no visible format

            // StopIfTrue stops further evaluation only when the condition is true.
            // If condition was false, continue regardless of StopIfTrue.
        }

        return null;
    }

    private static ConditionalFormatIcon? EvaluateConditionalIcon(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        CfEvaluationContext cfContext)
    {
        foreach (var rule in cfContext.IconRulesByPriority)
        {
            if (!rule.AppliesTo.Contains(addr))
                continue;
            if (!TryGetDouble(value, out var cellValue) || !cfContext.Aggregates.TryGetValue(rule, out var cache))
                return null;

            var style = string.IsNullOrWhiteSpace(rule.IconSetStyle) ? "3TrafficLights1" : rule.IconSetStyle!;
            var iconCount = GetIconSetCount(style);
            var iconIndex = ResolveIconSetIndex(rule, cellValue, cache.Min, cache.Max, iconCount);
            if (rule.IconSetReverse)
                iconIndex = iconCount - 1 - iconIndex;

            return new ConditionalFormatIcon(style, iconIndex, iconCount, rule.IconSetShowValue);
        }

        return null;
    }

    private static int ResolveIconSetIndex(ConditionalFormat rule, double value, double min, double max, int iconCount)
    {
        if (TryResolveIconSetThresholds(rule, min, max, iconCount, out var thresholds))
        {
            var index = 0;
            foreach (var threshold in thresholds)
            {
                if (value >= threshold)
                    index++;
            }

            return Math.Clamp(index, 0, iconCount - 1);
        }

        return ResolveInterpolatedIconSetIndex(value, min, max, iconCount);
    }

    private static int ResolveInterpolatedIconSetIndex(double value, double min, double max, int iconCount)
    {
        if (!double.IsFinite(value) || !double.IsFinite(min) || !double.IsFinite(max))
            return 0;
        if (max <= min)
            return iconCount - 1;

        var t = Math.Clamp((value - min) / (max - min), 0d, 1d);
        return Math.Clamp((int)Math.Floor(t * iconCount), 0, iconCount - 1);
    }

    private static bool TryResolveIconSetThresholds(
        ConditionalFormat rule,
        double min,
        double max,
        int iconCount,
        out double[] thresholds)
    {
        thresholds = [];
        if (rule.IconSetThresholds.Count < iconCount - 1)
            return false;

        var resolved = new List<double>(iconCount - 1);
        foreach (var threshold in rule.IconSetThresholds.Take(iconCount - 1))
        {
            switch (threshold.Type)
            {
                case CfThresholdType.Number:
                    if (!TryParseDouble(threshold.Value, out var number))
                        return false;
                    resolved.Add(number);
                    break;
                case CfThresholdType.Percent:
                    if (!TryParseDouble(threshold.Value, out var percent))
                        return false;
                    resolved.Add(min + (max - min) * (percent / 100d));
                    break;
                default:
                    return false;
            }
        }

        thresholds = resolved.ToArray();
        return thresholds.Length == iconCount - 1;
    }

    private static int GetIconSetCount(string style) =>
        style.Length > 0 && char.IsDigit(style[0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;

    // ── Formula CF evaluation ─────────────────────────────────────────────────

    private static bool MatchesFormula(ConditionalFormat cf, Sheet sheet, CellAddress addr, Workbook workbook)
    {
        if (string.IsNullOrWhiteSpace(cf.FormulaText)) return false;
        try
        {
            // Shift relative references from the CF range's top-left to the current cell.
            int dr = (int)addr.Row - (int)cf.AppliesTo.Start.Row;
            int dc = (int)addr.Col - (int)cf.AppliesTo.Start.Col;
            var formulaText = dr == 0 && dc == 0
                ? cf.FormulaText
                : ShiftCfFormula(cf.FormulaText, dr, dc);

            var result = _cfEvaluator.Evaluate("=" + formulaText, sheet, workbook);
            return result switch
            {
                BoolValue bv   => bv.Value,
                NumberValue nv => nv.Value != 0,
                _              => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static string ShiftCfFormula(string formulaText, int dr, int dc)
    {
        try
        {
            var ast = new Parser(new Lexer("=" + formulaText).Tokenize()).Parse();
            var shifted = ShiftAst(ast, dr, dc);
            return FormulaSerializer.Serialize(shifted);
        }
        catch
        {
            return formulaText;
        }
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr   => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr  => rr with
            {
                Start = ShiftCellRef(rr.Start, dr, dc),
                End   = ShiftCellRef(rr.End,   dr, dc)
            },
            BinaryOpNode bin => bin with
            {
                Left  = ShiftAst(bin.Left,  dr, dc),
                Right = ShiftAst(bin.Right, dr, dc)
            },
            UnaryOpNode un   => un with { Operand = ShiftAst(un.Operand, dr, dc) },
            FunctionCallNode fn => fn with
            {
                Arguments = fn.Arguments.Select(a => ShiftAst(a, dr, dc)).ToList()
            },
            _ => node
        };
    }

    private static CellRefNode ShiftCellRef(CellRefNode cr, int dr, int dc)
    {
        uint newRow = cr.IsRowAbsolute ? cr.Row
            : (uint)Math.Max(1, (int)cr.Row + dr);
        uint newColNum = cr.IsColAbsolute ? cr.ColumnNumber
            : (uint)Math.Max(1, (int)cr.ColumnNumber + dc);
        var newColName = cr.IsColAbsolute ? cr.ColumnName
            : CellAddress.NumberToColumnName(newColNum);
        return cr with { Row = newRow, ColumnName = newColName };
    }

    // ── CellValue matching ────────────────────────────────────────────────────

    private static bool MatchesCellValue(ConditionalFormat cf, ScalarValue value)
    {
        // Attempt numeric comparison first, fall back to string
        if (TryGetDouble(value, out double d))
        {
            if (!TryParseDouble(cf.Value1, out double v1)) return false;

            return cf.Operator switch
            {
                CfOperator.Equal              => d == v1,
                CfOperator.NotEqual           => d != v1,
                CfOperator.GreaterThan        => d > v1,
                CfOperator.GreaterThanOrEqual => d >= v1,
                CfOperator.LessThan           => d < v1,
                CfOperator.LessThanOrEqual    => d <= v1,
                CfOperator.Between            => TryParseDouble(cf.Value2, out double v2) && d >= v1 && d <= v2,
                CfOperator.NotBetween         => TryParseDouble(cf.Value2, out double v2b) && !(d >= v1 && d <= v2b),
                _                             => false
            };
        }
        else
        {
            // String fallback — only Equal / NotEqual make sense
            var s = GetString(value);
            return cf.Operator switch
            {
                CfOperator.Equal    => string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
                CfOperator.NotEqual => !string.Equals(s, cf.Value1, StringComparison.OrdinalIgnoreCase),
                _                  => false
            };
        }
    }

    // ── AboveAverage matching ─────────────────────────────────────────────────

    private static bool MatchesAboveAverage(
        ConditionalFormat cf, CellAddress addr, ScalarValue value,
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

        var occurrences = cache.ValueCounts.GetValueOrDefault(NormalizeCfDisplayValue(value));
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

    // ── ColorScale ────────────────────────────────────────────────────────────

    private static CellStyle? ComputeColorScaleStyle(
        ConditionalFormat cf, CellAddress addr, ScalarValue value,
        Dictionary<ConditionalFormat, CfAggregateCache> cfCache)
    {
        if (!TryGetDouble(value, out double cellVal)) return null;
        if (!double.IsFinite(cellVal)) return null;
        if (!cfCache.TryGetValue(cf, out var cache)) return null;

        double min = cache.Min, max = cache.Max;
        if (max == min) return new CellStyle { FillColor = cf.MinColor.ToCellColor() };

        double t = (cellVal - min) / (max - min); // 0..1

        CellColor interpolated;
        if (cf.UseThreeColorScale)
        {
            // Two-segment interpolation through MidColor at t = 0.5
            interpolated = t <= 0.5
                ? Lerp(cf.MinColor, cf.MidColor, t * 2)
                : Lerp(cf.MidColor, cf.MaxColor, (t - 0.5) * 2);
        }
        else
        {
            interpolated = Lerp(cf.MinColor, cf.MaxColor, t);
        }

        return new CellStyle { FillColor = interpolated };
    }

    private static CellColor Lerp(RgbColor a, RgbColor b, double t)
    {
        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new CellColor(r, g, bl);
    }

    // ── Style merging ─────────────────────────────────────────────────────────

    /// <summary>
    /// Merges a CF override style on top of a base style.
    /// CF properties override base only when they represent an actual override
    /// (non-null fill, non-default font properties set by the CF author).
    /// </summary>
    private static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle)
    {
        var result = (baseStyle ?? CellStyle.Default).Clone();

        if (cfStyle.FillColor.HasValue)
            result.FillColor = cfStyle.FillColor;
        if (cfStyle.FillPatternStyle != CellFillPatternStyle.None)
            result.FillPatternStyle = cfStyle.FillPatternStyle;
        if (cfStyle.FillPatternColor.HasValue)
            result.FillPatternColor = cfStyle.FillPatternColor;

        // For font properties we treat any non-default CF value as an explicit override
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

    // ── Value helpers ─────────────────────────────────────────────────────────

    private static bool TryGetDouble(ScalarValue value, out double result)
    {
        if (value is NumberValue nv) { result = nv.Value; return true; }
        if (value is DateTimeValue dv) { result = dv.Value; return true; }
        result = 0;
        return false;
    }

    private static bool TryParseDouble(string? text, out double result)
    {
        if (text is null) { result = 0; return false; }
        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out result);
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

    private static string NormalizeCfDisplayValue(ScalarValue value) =>
        GetString(value).Trim();
}
