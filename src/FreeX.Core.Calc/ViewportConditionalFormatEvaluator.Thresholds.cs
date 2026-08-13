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
            return !IsNonVolatileCellOrInfoCall(function);

        if (function.Arguments.Count == 0 &&
            function.FunctionName is "ROW" or "COLUMN")
        {
            return true;
        }

        return false;
    }

    // Mirrors RecalcEngine.IsNonVolatileCellOrInfoCall: CELL("width", ...) and INFO(...) with a
    // constant info-type in {directory,numfile,origin,osversion,recalc,release,system} are non-
    // volatile in Excel, so they shouldn't force a conditional format threshold to be treated as
    // current-cell-sensitive (which would disable the static-value precompute cache for it).
    private static bool IsNonVolatileCellOrInfoCall(FunctionCallNode func)
    {
        if (func.Arguments.Count == 0 || func.Arguments[0] is not StringNode { Value: var infoTypeArg })
            return false;

        var infoType = infoTypeArg.Trim();
        return func.FunctionName switch
        {
            "CELL" => string.Equals(infoType, "width", StringComparison.OrdinalIgnoreCase),
            "INFO" => NonVolatileInfoTypes.Contains(infoType.ToLowerInvariant()),
            _ => false,
        };
    }

    private static readonly HashSet<string> NonVolatileInfoTypes =
        ["directory", "numfile", "origin", "osversion", "recalc", "release", "system"];

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

            var iconCount = ConditionalFormatEvaluationMath.GetIconSetCount(cf.IconSetStyle);
            var thresholdCount = iconCount - 1;
            var thresholdStartIndex = ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(cf.IconSetThresholds.Count, iconCount);
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
                    out var resolvedMid))
            {
                // R68-render-conditional-format-6-2: a degenerate resolved midpoint (e.g. a
                // skewed dataset where percentile-50 lands exactly on min or max) must still keep
                // the 3-stop MidColor in the gradient -- clamp into [min,max] instead of dropping
                // mid to null, which used to collapse the WHOLE range to a plain Min->Max lerp and
                // silently erase MidColor everywhere, not just at the degenerate point.
                mid = Math.Clamp(resolvedMid, min, max);
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
        if (cache is null && type is not CfThresholdType.Number)
        {
            value = 0;
            return false;
        }

        return ConditionalFormatEvaluationMath.TryResolveStaticThreshold(
            type,
            text,
            cache is null ? default : ToEvaluationStatistics(cache),
            out value);
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
            // R68-render-conditional-format-6-2: keep a degenerate resolved midpoint (== min or
            // == max, e.g. a skewed dataset where percentile-50 lands exactly on the min) clamped
            // into [min,max] instead of nulling it out -- nulling collapsed the WHOLE range to a
            // plain Min->Max lerp and erased MidColor everywhere, not just at the degenerate point.
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
                      out var resolvedMid)
                ? Math.Clamp(resolvedMid, min, max)
                : null;
        }

        if (max <= min) return GetColorScaleStyle(cfContext, cf.MinColor.ToCellColor());

        var interpolated = mid.HasValue
            ? cellVal <= mid.Value
                ? mid.Value > min
                    ? Lerp(cf.MinColor, cf.MidColor, Math.Clamp((cellVal - min) / (mid.Value - min), 0d, 1d))
                    : cf.MidColor.ToCellColor()
                : mid.Value < max
                    ? Lerp(cf.MidColor, cf.MaxColor, Math.Clamp((cellVal - mid.Value) / (max - mid.Value), 0d, 1d))
                    : cf.MidColor.ToCellColor()
            : Lerp(cf.MinColor, cf.MaxColor, Math.Clamp((cellVal - min) / (max - min), 0d, 1d));

        return GetColorScaleStyle(cfContext, interpolated);
    }

    private static CellStyle GetColorScaleStyle(CfEvaluationContext cfContext, CellColor fillColor) =>
        cfContext.ColorScaleStyles?.Get(fillColor) ?? new CellStyle { FillColor = fillColor };

    /// <summary>
    /// Excel's automatic negative data-bar fill color (solid red) applied when a data-bar rule has
    /// no explicit <c>negativeFillColor</c>.
    /// </summary>
    private static readonly RgbColor ExcelAutomaticNegativeDataBarColor = new(0xFF, 0x00, 0x00);

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

            // Excel's Data Bar "Bar Direction" setting (x14:dataBar/@direction) has three values:
            // "rightToLeft" and "leftToRight" force a fixed direction regardless of the sheet's
            // reading order, while the default "Context" (absent attribute, or the literal value
            // "context") follows the worksheet's own reading order -- i.e. it mirrors automatically
            // on a sheet authored right-to-left (Sheet.IsRightToLeft, from sheetView/@rightToLeft).
            // Every fraction computed below (StartFraction/EndFraction/AxisFraction) is built as a
            // left-to-right layout; when the resolved direction is right-to-left the whole bar is
            // mirrored about the cell's horizontal center before being returned.
            var isRightToLeftBar = string.Equals(cf.DataBarDirection, "rightToLeft", StringComparison.OrdinalIgnoreCase)
                || (sheet.IsRightToLeft && !string.Equals(cf.DataBarDirection, "leftToRight", StringComparison.OrdinalIgnoreCase));

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

            // Excel's default ("automatic") data bar minimum/maximum -- CfThresholdType.AutoMin/
            // AutoMax, read from the x14 extended cfvo's "autoMin"/"autoMax" @type (or defaulted from
            // a classic-only cfvo's bare "min"/"max", which pre-2010 Excel and files without an x14
            // extended data bar cannot express any other way -- see ReadDataBarConditionalFormat) --
            // is NOT simply "use the range's actual minimum/maximum" the way an icon set or color
            // scale type="min"/"max" threshold is. For data bars specifically, Excel always keeps a
            // zero baseline for Automatic: the automatic minimum is min(0, actual minimum) and the
            // automatic maximum is max(0, actual maximum). Without this, an all-positive range (e.g.
            // 10/20/30) would resolve min=10, giving the smallest cell a zero-length bar (no bar
            // at all) instead of Excel's ~1/3-length bar. An EXPLICIT Lowest/Highest Value endpoint
            // (CfThresholdType.Min/Max, read from an x14 cfvo @type of literal "min"/"max") is
            // deliberately excluded from the clamp -- Excel does not zero-clamp an explicit endpoint,
            // only Automatic -- as are the genuinely explicit numeric/percent/percentile/formula
            // thresholds.
            if (cf.DataBarMinThresholdType == CfThresholdType.AutoMin)
                min = Math.Min(0d, min);
            if (cf.DataBarMaxThresholdType == CfThresholdType.AutoMax)
                max = Math.Max(0d, max);

            if (max <= min)
            {
                continue;
            }

            var minLength = Math.Clamp(cf.DataBarMinLength ?? 0, 0, 100) / 100d;
            var maxLength = Math.Clamp(cf.DataBarMaxLength ?? 100, 0, 100) / 100d;
            if (maxLength < minLength)
                (minLength, maxLength) = (maxLength, minLength);

            // Negative-axis path: when the range straddles zero (or is entirely negative, which
            // the zero clamp above pins to max == 0 -- i.e. the axis sits at the right edge) and
            // axisPosition is not "none", place the axis. "Middle" pins the axis at Excel's fixed
            // 50% position regardless of the min/max skew -- including an all-positive (or
            // all-negative) range that would otherwise never straddle zero, since the user
            // explicitly asked for the cell-center axis rather than the automatic zero-crossing
            // one; "Automatic" (unset) places it proportionally at the zero crossing, which only
            // exists when the range genuinely straddles zero. Positive bars extend rightward from
            // the axis; negative bars extend leftward from the axis using the negative fill
            // color. An all-negative range (max <= 0) has no positive bars to draw, but every
            // value must still go through the negative branch below so the longest (most
            // negative) bar is the most negative value, growing leftward from the axis in the
            // negative color -- not the positive-path fallthrough, which would invert both length
            // and color.
            var axisAtNone = string.Equals(cf.DataBarAxisPosition, "none", StringComparison.OrdinalIgnoreCase);
            var axisAtMiddle = string.Equals(cf.DataBarAxisPosition, "middle", StringComparison.OrdinalIgnoreCase);
            if (!axisAtNone && (axisAtMiddle || (min < 0 && max >= 0)))
            {
                // Division-by-zero guard: with axisAtMiddle forcing entry here, min/max need not
                // straddle zero any more (e.g. an all-positive range has min == 0 after the
                // automatic-minimum zero clamp above) -- unlike the "Automatic" ternary branch
                // below, which is only ever reached when min < 0 <= max (see the outer condition),
                // guaranteeing max - min > 0 there.
                var axisFraction = axisAtMiddle ? 0.5d : (0d - min) / (max - min);
                if (cellValue >= 0)
                {
                    // max can be exactly 0 for an all-negative (or all-zero) range where the
                    // automatic-maximum zero clamp pins max at 0; the only cell that can reach
                    // this branch then is cellValue == 0 itself, which is always a full-length
                    // "positive" segment of zero width (t is irrelevant / avoid a 0/0 NaN).
                    var t = max > 0d ? Math.Clamp(cellValue / max, 0d, 1d) : 0d;
                    var length = (minLength + (maxLength - minLength) * t) * (1d - axisFraction);
                    if (length <= 0)
                    {
                        // This is the single highest-priority Data Bar rule that applies to this
                        // cell; a zero-length bar is an authoritative "no bar" result, not a
                        // "rule doesn't apply" signal — do not fall through to a lower-priority
                        // overlapping Data Bar rule.
                        return null;
                    }

                    return MirrorDataBarIfRightToLeft(
                        isRightToLeftBar,
                        new ConditionalFormatDataBar(
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
                            BorderColor: cf.DataBarBorderColor));
                }
                else
                {
                    // min can be exactly 0 here too (axisAtMiddle forcing entry with an explicit,
                    // non-auto-clamped non-negative min threshold while an actual cell value is
                    // still negative -- see the division-by-zero guard comment above); treat a
                    // negative value below a zero-or-positive min as a zero-length negative
                    // segment from the axis rather than dividing by zero.
                    var t = min < 0d ? Math.Clamp((0d - cellValue) / (0d - min), 0d, 1d) : 0d;
                    var length = (minLength + (maxLength - minLength) * t) * axisFraction;
                    if (length <= 0)
                    {
                        // See comment above: this rule already applies to this cell, so a
                        // zero-length bar must render as no bar rather than falling through.
                        return null;
                    }

                    // Excel's "automatic" negative data-bar fill (no explicit negativeFillColor set
                    // on the rule) is a solid red, not the positive-bar color -- only an explicit
                    // negativeFillColor overrides it.
                    var negColor = cf.DataBarNegativeFillColor ?? ExcelAutomaticNegativeDataBarColor;
                    // For negative bars use the negative border color when available, otherwise fall
                    // back to the positive border color.
                    var negBorderColor = cf.DataBarNegativeBorderColor ?? cf.DataBarBorderColor;
                    return MirrorDataBarIfRightToLeft(
                        isRightToLeftBar,
                        new ConditionalFormatDataBar(
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
                            BorderColor: negBorderColor));
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

            return MirrorDataBarIfRightToLeft(
                isRightToLeftBar,
                new ConditionalFormatDataBar(
                    0d,
                    Math.Clamp(barLength, 0d, 1d),
                    cf.DataBarColor,
                    cf.DataBarGradient,
                    cf.DataBarBorder,
                    cf.DataBarShowValue,
                    BorderColor: cf.DataBarBorderColor));
        }

        return null;
    }

    /// <summary>
    /// Mirrors a left-to-right data-bar layout about the cell's horizontal center so it renders
    /// growing from the right edge instead, matching Excel's "Bar Direction" = Right-to-Left (or
    /// "Context" on a right-to-left-authored sheet). <see cref="ConditionalFormatDataBar.StartFraction"/>/
    /// <see cref="ConditionalFormatDataBar.EndFraction"/>/<see cref="ConditionalFormatDataBar.AxisFraction"/>
    /// are all expressed as a 0..1 fraction of the cell measured from its left edge, so mirroring is a
    /// simple 1-x reflection; every other field (colors, gradient, border, IsNegative, ShowValue) is
    /// direction-independent and passes through unchanged.
    /// </summary>
    private static ConditionalFormatDataBar MirrorDataBarIfRightToLeft(bool isRightToLeftBar, ConditionalFormatDataBar bar)
    {
        if (!isRightToLeftBar)
            return bar;

        return bar with
        {
            StartFraction = 1d - bar.EndFraction,
            EndFraction = 1d - bar.StartFraction,
            AxisFraction = bar.AxisFraction is > 0d and < 1d ? 1d - bar.AxisFraction : bar.AxisFraction,
        };
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
        if (type != CfThresholdType.Formula)
            return ConditionalFormatEvaluationMath.TryResolveStaticThreshold(
                type,
                text,
                ToEvaluationStatistics(cache),
                out value);

        if (staticFormulaValue is { } staticValue)
        {
            value = staticValue;
            return double.IsFinite(staticValue);
        }

        return TryEvaluateThresholdFormula(formulaAst, sheet, workbook, anchorCell, currentCell, out value);
    }

    private static ConditionalFormatEvaluationStatistics ToEvaluationStatistics(CfAggregateCache cache) =>
        new(cache.Count, cache.Min, cache.Max, cache.Average, cache.StdDev, cache.SortedValues);

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
