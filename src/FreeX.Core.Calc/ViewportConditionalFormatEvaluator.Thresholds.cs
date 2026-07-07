using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal static partial class ViewportConditionalFormatEvaluator
{
    private static Dictionary<CfThresholdFormulaKey, FormulaNode> PrecomputeThresholdFormulaCaches(Sheet sheet)
    {
        Dictionary<CfThresholdFormulaKey, FormulaNode>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType == CfRuleType.CellValue)
            {
                TryAddCellValueFormulaCache(ref result, cf, CfThresholdFormulaSlot.CellValue1, cf.Value1);
                if (cf.Operator is CfOperator.Between or CfOperator.NotBetween)
                    TryAddCellValueFormulaCache(ref result, cf, CfThresholdFormulaSlot.CellValue2, cf.Value2);
                continue;
            }

            if (cf.RuleType == CfRuleType.ColorScale)
            {
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.ColorScaleMin, -1, cf.MinThresholdType, cf.MinThresholdValue);
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.ColorScaleMid, -1, cf.MidThresholdType, cf.MidThresholdValue);
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.ColorScaleMax, -1, cf.MaxThresholdType, cf.MaxThresholdValue);
                continue;
            }

            if (cf.RuleType == CfRuleType.DataBar)
            {
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.DataBarMin, -1, cf.DataBarMinThresholdType, cf.DataBarMinThresholdValue);
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.DataBarMax, -1, cf.DataBarMaxThresholdType, cf.DataBarMaxThresholdValue);
                continue;
            }

            if (cf.RuleType != CfRuleType.IconSet)
                continue;

            for (var i = 0; i < cf.IconSetThresholds.Count; i++)
            {
                var threshold = cf.IconSetThresholds[i];
                TryAddThresholdFormulaCache(ref result, cf, CfThresholdFormulaSlot.IconSet, i, threshold.Type, threshold.Value);
            }
        }

        return result ?? EmptyThresholdFormulas;
    }

    private static void TryAddCellValueFormulaCache(
        ref Dictionary<CfThresholdFormulaKey, FormulaNode>? result,
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || TryParseDouble(text, out _))
            return;

        try
        {
            result ??= [];
            result[new CfThresholdFormulaKey(cf, slot)] = ParseFormulaText(text);
        }
        catch
        {
            // Preserve literal text CF behavior: unparseable comparison values remain literal strings.
        }
    }

    private static Dictionary<CfThresholdFormulaKey, double> PrecomputeStaticThresholdFormulaValues(
        Sheet sheet,
        Workbook workbook,
        Dictionary<CfThresholdFormulaKey, FormulaNode> thresholdFormulas)
    {
        if (thresholdFormulas.Count == 0)
            return EmptyStaticThresholdFormulaValues;

        Dictionary<CfThresholdFormulaKey, double>? result = null;
        foreach (var (key, ast) in thresholdFormulas)
        {
            if (HasRelativeReferences(ast) || IsCurrentCellSensitive(ast))
                continue;

            if (TryEvaluateThresholdFormula(ast, sheet, workbook, key.Rule.AppliesTo.Start, out var value))
                (result ??= [])[key] = value;
        }

        return result ?? EmptyStaticThresholdFormulaValues;
    }

    private static bool IsCurrentCellSensitive(FormulaNode node)
    {
        return node switch
        {
            StructuredReferenceNode or StructuredCurrentRowReferenceNode => true,
            RangeRefNode range => IsCurrentCellSensitive(range.Start) || IsCurrentCellSensitive(range.End),
            FullColumnRangeRefNode or FullRowRangeRefNode => false,
            BinaryOpNode binary => IsCurrentCellSensitive(binary.Left) || IsCurrentCellSensitive(binary.Right),
            UnaryOpNode unary => IsCurrentCellSensitive(unary.Operand),
            FunctionCallNode function => IsCurrentCellSensitiveFunction(function) || IsCurrentCellSensitive(function.Arguments),
            _ => false
        };
    }

    private static bool IsCurrentCellSensitive(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (IsCurrentCellSensitive(nodes[i]))
                return true;
        }

        return false;
    }

    private static bool IsCurrentCellSensitiveFunction(FunctionCallNode function)
    {
        if (function.FunctionName is "NOW" or "TODAY" or "RAND" or "RANDBETWEEN" or "RANDARRAY" or "INDIRECT" or "OFFSET" or "CELL" or "INFO")
            return true;

        if (function.Arguments.Count == 0 &&
            function.FunctionName is "ROW" or "COLUMN")
        {
            return true;
        }

        return false;
    }

    private static Dictionary<ConditionalFormat, CfIconSetThresholdCache> PrecomputeIconSetThresholdCaches(
        Sheet sheet,
        Dictionary<ConditionalFormat, CfAggregateCache> aggregates,
        Dictionary<CfThresholdFormulaKey, double> staticThresholdFormulaValues)
    {
        Dictionary<ConditionalFormat, CfIconSetThresholdCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.IconSet ||
                !TryGetIconSetAggregateCache(cf, aggregates, out var cache))
                continue;

            var thresholdCount = GetIconSetCount(cf.IconSetStyle) - 1;
            var thresholdStartIndex = GetIconSetThresholdStartIndex(cf, GetIconSetCount(cf.IconSetStyle));
            if (cf.IconSetThresholds.Count - thresholdStartIndex < thresholdCount)
                continue;

            var values = new double[thresholdCount];
            var comparisons = new bool[thresholdCount];
            var resolved = true;
            for (var i = 0; i < thresholdCount; i++)
            {
                var sourceIndex = thresholdStartIndex + i;
                var threshold = cf.IconSetThresholds[sourceIndex];
                if (threshold.Type == CfThresholdType.Formula)
                {
                    if (!staticThresholdFormulaValues.TryGetValue(
                            new CfThresholdFormulaKey(cf, CfThresholdFormulaSlot.IconSet, sourceIndex),
                            out values[i]) ||
                        !double.IsFinite(values[i]))
                    {
                        resolved = false;
                        break;
                    }
                }
                else if (!TryResolveStaticThreshold(threshold.Type, threshold.Value, cache, out values[i]))
                {
                    resolved = false;
                    break;
                }

                comparisons[i] = threshold.GreaterThanOrEqual ?? true;
            }

            if (!resolved)
                continue;

            result ??= new Dictionary<ConditionalFormat, CfIconSetThresholdCache>(ReferenceEqualityComparer.Instance);
            result[cf] = new CfIconSetThresholdCache(values, comparisons);
        }

        return result ?? EmptyIconSetThresholds;
    }

    private static Dictionary<ConditionalFormat, CfColorScaleThresholdCache> PrecomputeColorScaleThresholdCaches(
        Sheet sheet,
        Dictionary<ConditionalFormat, CfAggregateCache> aggregates,
        Dictionary<CfThresholdFormulaKey, double> staticThresholdFormulaValues)
    {
        Dictionary<ConditionalFormat, CfColorScaleThresholdCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.ColorScale ||
                !aggregates.TryGetValue(cf, out var cache))
            {
                continue;
            }

            if (!TryResolveStaticOrFormulaThreshold(
                    cf,
                    CfThresholdFormulaSlot.ColorScaleMin,
                    cf.MinThresholdType,
                    cf.MinThresholdValue,
                    cache,
                    staticThresholdFormulaValues,
                    out var min) ||
                !TryResolveStaticOrFormulaThreshold(
                    cf,
                    CfThresholdFormulaSlot.ColorScaleMax,
                    cf.MaxThresholdType,
                    cf.MaxThresholdValue,
                    cache,
                    staticThresholdFormulaValues,
                    out var max))
            {
                continue;
            }

            double? mid = null;
            if (cf.UseThreeColorScale &&
                TryResolveStaticOrFormulaThreshold(
                    cf,
                    CfThresholdFormulaSlot.ColorScaleMid,
                    cf.MidThresholdType,
                    cf.MidThresholdValue,
                    cache,
                    staticThresholdFormulaValues,
                    out var resolvedMid) &&
                resolvedMid > min &&
                resolvedMid < max)
            {
                mid = resolvedMid;
            }

            result ??= new Dictionary<ConditionalFormat, CfColorScaleThresholdCache>(ReferenceEqualityComparer.Instance);
            result[cf] = new CfColorScaleThresholdCache(min, max, mid);
        }

        return result ?? EmptyColorScaleThresholds;
    }

    private static bool TryResolveStaticOrFormulaThreshold(
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        CfThresholdType type,
        string? text,
        CfAggregateCache cache,
        Dictionary<CfThresholdFormulaKey, double> staticThresholdFormulaValues,
        out double value)
    {
        if (type == CfThresholdType.Formula)
        {
            return staticThresholdFormulaValues.TryGetValue(new CfThresholdFormulaKey(cf, slot), out value) &&
                   double.IsFinite(value);
        }

        return TryResolveStaticThreshold(type, text, cache, out value);
    }

    private static bool TryResolveStaticThreshold(
        CfThresholdType type,
        string? text,
        CfAggregateCache? cache,
        out double value)
    {
        value = 0;
        return type switch
        {
            CfThresholdType.Min when cache is not null => Set(cache.Min, out value),
            CfThresholdType.Max when cache is not null => Set(cache.Max, out value),
            CfThresholdType.Number => TryParseDouble(text, out value),
            CfThresholdType.Percent when cache is not null => TryParseDouble(text, out var percent) &&
                                       Set(cache.Min + (cache.Max - cache.Min) * (percent / 100d), out value),
            CfThresholdType.Percentile when cache is not null => TryParseDouble(text, out var percentile) &&
                                          TryResolvePercentile(cache.SortedValues, percentile, out value),
            _ => false
        };

        static bool Set(double input, out double output)
        {
            output = input;
            return double.IsFinite(input);
        }
    }

    private static bool TryGetIconSetAggregateCache(
        ConditionalFormat cf,
        Dictionary<ConditionalFormat, CfAggregateCache> aggregates,
        out CfAggregateCache? cache)
    {
        if (RequiresIconSetAggregateCache(cf))
        {
            var found = aggregates.TryGetValue(cf, out var aggregateCache);
            cache = aggregateCache;
            return found;
        }

        cache = null;
        return true;
    }

    private static void TryAddThresholdFormulaCache(
        ref Dictionary<CfThresholdFormulaKey, FormulaNode>? result,
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        int index,
        CfThresholdType type,
        string? text)
    {
        if (type != CfThresholdType.Formula || string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            result ??= [];
            result[new CfThresholdFormulaKey(cf, slot, index)] = ParseFormulaText(text);
        }
        catch
        {
            // Preserve threshold formula error handling: invalid formulas do not resolve.
        }
    }

    private static FormulaNode ParseFormulaText(string? text)
    {
        return FormulaEvaluator.ParseFormula(text ?? string.Empty);
    }

    internal static FormulaNode? GetThresholdFormula(
        CfEvaluationContext cfContext,
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        int index = -1) =>
        cfContext.ThresholdFormulas.TryGetValue(new CfThresholdFormulaKey(cf, slot, index), out var ast)
            ? ast
            : null;

    internal static double? GetStaticThresholdFormulaValue(
        CfEvaluationContext cfContext,
        ConditionalFormat cf,
        CfThresholdFormulaSlot slot,
        int index = -1) =>
        cfContext.StaticThresholdFormulaValues.TryGetValue(new CfThresholdFormulaKey(cf, slot, index), out var value)
            ? value
            : null;

    internal static int GetIconSetCount(string? style) =>
        !string.IsNullOrWhiteSpace(style) && char.IsDigit(style![0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;

    internal static int GetIconSetThresholdStartIndex(ConditionalFormat cf, int iconCount) =>
        cf.IconSetThresholds.Count >= iconCount ? 1 : 0;

    private static CellStyle? ComputeColorScaleStyle(
        ConditionalFormat cf,
        ScalarValue value,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        CfEvaluationContext cfContext)
    {
        if (!TryGetDouble(value, out double cellVal)) return null;
        if (!double.IsFinite(cellVal)) return null;
        if (!cfContext.Aggregates.TryGetValue(cf, out var cache)) return null;

        double min;
        double max;
        double? mid;
        if (cfContext.ColorScaleThresholds.TryGetValue(cf, out var cachedThresholds))
        {
            min = cachedThresholds.Min;
            max = cachedThresholds.Max;
            mid = cachedThresholds.Mid;
        }
        else if (!TryResolveThreshold(
                     cf.MinThresholdType,
                     cf.MinThresholdValue,
                     cache,
                     sheet,
                     workbook,
                     addr,
                     cf.AppliesTo.Start,
                     GetStaticThresholdFormulaValue(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMin),
                     GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMin),
                     out min) ||
                 !TryResolveThreshold(
                     cf.MaxThresholdType,
                     cf.MaxThresholdValue,
                     cache,
                     sheet,
                     workbook,
                     addr,
                     cf.AppliesTo.Start,
                     GetStaticThresholdFormulaValue(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMax),
                     GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMax),
                     out max))
        {
            return null;
        }
        else
        {
            mid = cf.UseThreeColorScale &&
                  TryResolveThreshold(
                      cf.MidThresholdType,
                      cf.MidThresholdValue,
                      cache,
                      sheet,
                      workbook,
                      addr,
                      cf.AppliesTo.Start,
                      GetStaticThresholdFormulaValue(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMid),
                      GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMid),
                      out var resolvedMid) &&
                  resolvedMid > min &&
                  resolvedMid < max
                ? resolvedMid
                : null;
        }

        if (max <= min) return GetColorScaleStyle(cfContext, cf.MinColor.ToCellColor());

        var interpolated = mid.HasValue
            ? cellVal <= mid.Value
                ? Lerp(cf.MinColor, cf.MidColor, Math.Clamp((cellVal - min) / (mid.Value - min), 0d, 1d))
                : Lerp(cf.MidColor, cf.MaxColor, Math.Clamp((cellVal - mid.Value) / (max - mid.Value), 0d, 1d))
            : Lerp(cf.MinColor, cf.MaxColor, Math.Clamp((cellVal - min) / (max - min), 0d, 1d));

        return GetColorScaleStyle(cfContext, interpolated);
    }

    private static CellStyle GetColorScaleStyle(CfEvaluationContext cfContext, CellColor fillColor) =>
        cfContext.ColorScaleStyles?.Get(fillColor) ?? new CellStyle { FillColor = fillColor };

    internal static ConditionalFormatDataBar? EvaluateDataBar(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        Func<ConditionalFormat, Sheet, CellAddress, Workbook, CfEvaluationContext, bool> matchesFormula)
    {
        for (var i = 0; i < cfContext.RulesByPriority.Count; i++)
        {
            var cf = cfContext.RulesByPriority[i];
            if (cf.RuleType != CfRuleType.DataBar || !cf.AllRanges.Any(r => r.Contains(addr)))
                continue;

            // A higher-priority rule of ANY kind (style, icon set, or data bar) whose condition is
            // met and which is marked Stop If True suppresses this data bar, matching Excel's
            // standard "stop if true hides lower-priority icon sets/data bars" idiom.
            if (IsSuppressedByHigherPriorityStopIfTrue(cf, sheet, addr, value, workbook, cfContext, matchesFormula))
                return null;

            if (!TryGetDouble(value, out var cellValue) ||
                !double.IsFinite(cellValue) ||
                !cfContext.Aggregates.TryGetValue(cf, out var cache))
            {
                continue;
            }

            if (!TryResolveThreshold(
                    cf.DataBarMinThresholdType,
                    cf.DataBarMinThresholdValue,
                    cache,
                    sheet,
                    workbook,
                    addr,
                    cf.AppliesTo.Start,
                    GetStaticThresholdFormulaValue(cfContext, cf, CfThresholdFormulaSlot.DataBarMin),
                    GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.DataBarMin),
                    out var min) ||
                !TryResolveThreshold(
                    cf.DataBarMaxThresholdType,
                    cf.DataBarMaxThresholdValue,
                    cache,
                    sheet,
                    workbook,
                    addr,
                    cf.AppliesTo.Start,
                    GetStaticThresholdFormulaValue(cfContext, cf, CfThresholdFormulaSlot.DataBarMax),
                    GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.DataBarMax),
                    out var max))
            {
                continue;
            }

            // Excel's default ("automatic") data bar minimum/maximum -- represented by cfvo
            // type="min"/"max" in the classic block and its x14 autoMin/autoMax twin, both of
            // which the reader maps onto CfThresholdType.Min/Max for data bars -- is NOT simply
            // "use the range's actual minimum/maximum" the way an icon set or color scale
            // type="min"/"max" threshold is. For data bars specifically, Excel always keeps a
            // zero baseline: the automatic minimum is min(0, actual minimum) and the automatic
            // maximum is max(0, actual maximum). Without this, an all-positive range (e.g.
            // 10/20/30) would resolve min=10, giving the smallest cell a zero-length bar (no bar
            // at all) instead of Excel's ~1/3-length bar. A genuinely explicit numeric/percent/
            // percentile/formula threshold is unaffected since only Min/Max get the zero clamp.
            if (cf.DataBarMinThresholdType == CfThresholdType.Min)
                min = Math.Min(0d, min);
            if (cf.DataBarMaxThresholdType == CfThresholdType.Max)
                max = Math.Max(0d, max);

            if (max <= min)
            {
                continue;
            }

            var minLength = Math.Clamp(cf.DataBarMinLength ?? 0, 0, 100) / 100d;
            var maxLength = Math.Clamp(cf.DataBarMaxLength ?? 100, 0, 100) / 100d;
            if (maxLength < minLength)
                (minLength, maxLength) = (maxLength, minLength);

            // Negative-axis path: when the range straddles zero and axisPosition is not "none",
            // place the axis. "Middle" pins the axis at Excel's fixed 50% position regardless of
            // the min/max skew; "Automatic" (unset) places it proportionally at the zero crossing.
            // Positive bars extend rightward from the axis; negative bars extend leftward from the
            // axis using the negative fill color.
            var axisAtNone = string.Equals(cf.DataBarAxisPosition, "none", StringComparison.OrdinalIgnoreCase);
            var axisAtMiddle = string.Equals(cf.DataBarAxisPosition, "middle", StringComparison.OrdinalIgnoreCase);
            if (!axisAtNone && min < 0 && max > 0)
            {
                var axisFraction = axisAtMiddle ? 0.5d : (0d - min) / (max - min);
                if (cellValue >= 0)
                {
                    var t = Math.Clamp((cellValue - 0d) / (max - 0d), 0d, 1d);
                    var length = (minLength + (maxLength - minLength) * t) * (1d - axisFraction);
                    if (length <= 0)
                    {
                        // This is the single highest-priority Data Bar rule that applies to this
                        // cell; a zero-length bar is an authoritative "no bar" result, not a
                        // "rule doesn't apply" signal — do not fall through to a lower-priority
                        // overlapping Data Bar rule.
                        return null;
                    }

                    return new ConditionalFormatDataBar(
                        axisFraction,
                        axisFraction + length,
                        cf.DataBarColor,
                        cf.DataBarGradient,
                        cf.DataBarBorder,
                        cf.DataBarShowValue,
                        IsNegative: false,
                        AxisFraction: axisFraction,
                        NegativeFillColor: cf.DataBarNegativeFillColor,
                        AxisColor: cf.DataBarAxisColor,
                        BorderColor: cf.DataBarBorderColor);
                }
                else
                {
                    var t = Math.Clamp((0d - cellValue) / (0d - min), 0d, 1d);
                    var length = (minLength + (maxLength - minLength) * t) * axisFraction;
                    if (length <= 0)
                    {
                        // See comment above: this rule already applies to this cell, so a
                        // zero-length bar must render as no bar rather than falling through.
                        return null;
                    }

                    var negColor = cf.DataBarNegativeFillColor ?? cf.DataBarColor;
                    // For negative bars use the negative border color when available, otherwise fall
                    // back to the positive border color.
                    var negBorderColor = cf.DataBarNegativeBorderColor ?? cf.DataBarBorderColor;
                    return new ConditionalFormatDataBar(
                        axisFraction - length,
                        axisFraction,
                        negColor,
                        cf.DataBarGradient,
                        cf.DataBarBorder,
                        cf.DataBarShowValue,
                        IsNegative: true,
                        AxisFraction: axisFraction,
                        NegativeFillColor: cf.DataBarNegativeFillColor,
                        AxisColor: cf.DataBarAxisColor,
                        BorderColor: negBorderColor);
                }
            }

            var fraction = Math.Clamp((cellValue - min) / (max - min), 0d, 1d);
            var barLength = minLength + (maxLength - minLength) * fraction;
            if (barLength <= 0)
            {
                // This is the single highest-priority Data Bar rule that applies to this cell
                // (e.g. cellValue == min with the default MinLength of 0%); a zero-length bar is
                // an authoritative "no bar" result and must not fall through to a lower-priority
                // overlapping Data Bar rule.
                return null;
            }

            return new ConditionalFormatDataBar(
                0d,
                Math.Clamp(barLength, 0d, 1d),
                cf.DataBarColor,
                cf.DataBarGradient,
                cf.DataBarBorder,
                cf.DataBarShowValue,
                BorderColor: cf.DataBarBorderColor);
        }

        return null;
    }

    internal static bool TryResolveThreshold(
        CfThresholdType type,
        string? text,
        CfAggregateCache cache,
        Sheet sheet,
        Workbook workbook,
        CellAddress currentCell,
        CellAddress anchorCell,
        double? staticFormulaValue,
        FormulaNode? formulaAst,
        out double value)
    {
        value = 0;
        return type switch
        {
            CfThresholdType.Min => Set(cache.Min, out value),
            CfThresholdType.Max => Set(cache.Max, out value),
            CfThresholdType.Number => TryParseDouble(text, out value),
            CfThresholdType.Percent => TryParseDouble(text, out var percent) &&
                                       Set(cache.Min + (cache.Max - cache.Min) * (percent / 100d), out value),
            CfThresholdType.Percentile => TryParseDouble(text, out var percentile) &&
                                          TryResolvePercentile(cache.SortedValues, percentile, out value),
            CfThresholdType.Formula => staticFormulaValue.HasValue
                ? Set(staticFormulaValue.Value, out value)
                : TryEvaluateThresholdFormula(formulaAst, sheet, workbook, anchorCell, currentCell, out value),
            _ => false
        };

        static bool Set(double input, out double output)
        {
            output = input;
            return double.IsFinite(input);
        }
    }

    private static bool TryResolvePercentile(IReadOnlyList<double>? sortedValues, double percentile, out double value)
    {
        value = 0;
        if (sortedValues is null || sortedValues.Count == 0)
            return false;

        percentile = Math.Clamp(percentile, 0d, 100d);
        if (sortedValues.Count == 1)
        {
            value = sortedValues[0];
            return true;
        }

        var position = (sortedValues.Count - 1) * percentile / 100d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            value = sortedValues[lower];
            return true;
        }

        var weight = position - lower;
        value = sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight;
        return true;
    }

    private static bool TryEvaluateThresholdFormula(
        FormulaNode? ast,
        Sheet sheet,
        Workbook workbook,
        CellAddress anchorCell,
        CellAddress currentCell,
        out double value)
    {
        value = 0;
        if (ast is null)
            return false;

        try
        {
            var shiftedAst = GetShiftedConditionalFormatFormula(ast, anchorCell, currentCell);
            var result = ThresholdFormulaEvaluator.Evaluate(shiftedAst, sheet, workbook, currentCell);
            return TryGetDouble(result, out value);
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static bool TryEvaluateThresholdFormula(
        FormulaNode? ast,
        Sheet sheet,
        Workbook workbook,
        CellAddress currentCell,
        out double value)
    {
        value = 0;
        if (ast is null)
            return false;

        try
        {
            var result = ThresholdFormulaEvaluator.Evaluate(ast, sheet, workbook, currentCell);
            return TryGetDouble(result, out value);
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static CellColor Lerp(RgbColor a, RgbColor b, double t)
    {
        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new CellColor(r, g, bl);
    }

}
