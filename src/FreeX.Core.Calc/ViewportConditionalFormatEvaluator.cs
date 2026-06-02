using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal sealed record CfAggregateCache(
    double Average,
    double Min,
    double Max,
    IReadOnlyList<double>? SortedValues = null,
    IReadOnlySet<CellAddress>? TopBottomMatches = null,
    IReadOnlyDictionary<string, int>? ValueCounts = null);

internal sealed record CfEvaluationContext(
    IReadOnlyList<ConditionalFormat> RulesByPriority,
    IReadOnlyList<ConditionalFormat> IconRulesByPriority,
    Dictionary<ConditionalFormat, CfAggregateCache> Aggregates,
    Dictionary<ConditionalFormat, CfFormulaCache> Formulas,
    Dictionary<CfThresholdFormulaKey, FormulaNode> ThresholdFormulas,
    Dictionary<CfThresholdFormulaKey, double> StaticThresholdFormulaValues,
    Dictionary<ConditionalFormat, CfColorScaleThresholdCache> ColorScaleThresholds,
    Dictionary<ConditionalFormat, CfIconSetThresholdCache> IconSetThresholds,
    Dictionary<ConditionalFormat, CellStyle> DefaultMergedFormatStyles);

internal sealed record CfColorScaleThresholdCache(double Min, double Max, double? Mid);
internal sealed record CfIconSetThresholdCache(double[] Values, bool[] GreaterThanOrEqual);

internal sealed record CfFormulaCache(
    FormulaNode Ast,
    bool HasRelativeReferences,
    CfSimpleFormulaComparison? SimpleComparison,
    CfSimpleFormulaAnd? SimpleAnd);

internal readonly record struct CfStyleResult(CellStyle Style, bool CanUseAsDefaultMergedStyle);

internal readonly record struct CfSimpleFormulaComparison(
    CfFormulaScalarOperand Left,
    BinaryOperator Operator,
    CfFormulaScalarOperand Right);

internal sealed record CfSimpleFormulaAnd(CfSimpleFormulaComparison[] Comparisons);

internal readonly record struct CfFormulaScalarOperand(
    CfFormulaScalarOperandKind Kind,
    ScalarValue? Literal,
    uint Row,
    uint Col,
    bool IsRowAbsolute,
    bool IsColAbsolute,
    string? SheetName);

internal enum CfFormulaScalarOperandKind
{
    Literal,
    Reference
}

internal readonly record struct CfThresholdFormulaKey(
    ConditionalFormat Rule,
    CfThresholdFormulaSlot Slot,
    int Index = -1);

internal enum CfThresholdFormulaSlot
{
    CellValue1,
    CellValue2,
    ColorScaleMin,
    ColorScaleMid,
    ColorScaleMax,
    IconSet
}

internal static class ViewportConditionalFormatEvaluator
{
    private static readonly ConditionalFormat[] EmptyRules = [];
    private static readonly Dictionary<ConditionalFormat, CfAggregateCache> EmptyAggregates = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<ConditionalFormat, CfFormulaCache> EmptyFormulas = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<CfThresholdFormulaKey, FormulaNode> EmptyThresholdFormulas = [];
    private static readonly Dictionary<CfThresholdFormulaKey, double> EmptyStaticThresholdFormulaValues = [];
    private static readonly Dictionary<ConditionalFormat, CfColorScaleThresholdCache> EmptyColorScaleThresholds = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<ConditionalFormat, CfIconSetThresholdCache> EmptyIconSetThresholds = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<ConditionalFormat, CellStyle> EmptyDefaultMergedFormatStyles = new(ReferenceEqualityComparer.Instance);
    private static readonly FormulaEvaluator ThresholdFormulaEvaluator = new();
    private static readonly CfEvaluationContext EmptyContext = new(
        EmptyRules,
        EmptyRules,
        EmptyAggregates,
        EmptyFormulas,
        EmptyThresholdFormulas,
        EmptyStaticThresholdFormulaValues,
        EmptyColorScaleThresholds,
        EmptyIconSetThresholds,
        EmptyDefaultMergedFormatStyles);

    public static CfEvaluationContext BuildContext(Sheet sheet, Workbook workbook)
    {
        if (sheet.ConditionalFormats.Count == 0)
            return EmptyContext;

        var rulesByPriority = CopyRulesByPriority(sheet.ConditionalFormats);
        var iconRulesByPriority = CopyIconRulesByPriority(rulesByPriority);
        var aggregates = PrecomputeAggregates(sheet);
        var thresholdFormulas = PrecomputeThresholdFormulaCaches(sheet);
        var staticThresholdFormulaValues = PrecomputeStaticThresholdFormulaValues(sheet, workbook, thresholdFormulas);

        return new CfEvaluationContext(
            rulesByPriority,
            iconRulesByPriority,
            aggregates,
            PrecomputeFormulaCaches(sheet),
            thresholdFormulas,
            staticThresholdFormulaValues,
            PrecomputeColorScaleThresholdCaches(sheet, aggregates, staticThresholdFormulaValues),
            PrecomputeIconSetThresholdCaches(sheet, aggregates, staticThresholdFormulaValues),
            PrecomputeDefaultMergedFormatStyles(rulesByPriority));
    }

    private static ConditionalFormat[] CopyRulesByPriority(IReadOnlyList<ConditionalFormat> rules)
    {
        var indexedRules = new IndexedConditionalFormat[rules.Count];
        for (var i = 0; i < rules.Count; i++)
            indexedRules[i] = new IndexedConditionalFormat(rules[i], i);

        Array.Sort(indexedRules, static (left, right) =>
        {
            var priorityOrder = left.Rule.Priority.CompareTo(right.Rule.Priority);
            return priorityOrder != 0
                ? priorityOrder
                : left.Index.CompareTo(right.Index);
        });

        var sortedRules = new ConditionalFormat[indexedRules.Length];
        for (var i = 0; i < indexedRules.Length; i++)
            sortedRules[i] = indexedRules[i].Rule;

        return sortedRules;
    }

    private static ConditionalFormat[] CopyIconRulesByPriority(IReadOnlyList<ConditionalFormat> rulesByPriority)
    {
        var iconRuleCount = 0;
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            if (rulesByPriority[i].RuleType == CfRuleType.IconSet)
                iconRuleCount++;
        }

        if (iconRuleCount == 0)
            return EmptyRules;

        var iconRules = new ConditionalFormat[iconRuleCount];
        var iconIndex = 0;
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            var rule = rulesByPriority[i];
            if (rule.RuleType == CfRuleType.IconSet)
                iconRules[iconIndex++] = rule;
        }

        return iconRules;
    }

    private readonly record struct IndexedConditionalFormat(ConditionalFormat Rule, int Index);

    public static CfStyleResult? Evaluate(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        Func<ConditionalFormat, Sheet, CellAddress, Workbook, CfEvaluationContext, bool> matchesFormula)
    {
        if (cfContext.RulesByPriority.Count == 0)
            return null;

        CfStyleResult? result = null;
        for (var i = 0; i < cfContext.RulesByPriority.Count; i++)
        {
            var cf = cfContext.RulesByPriority[i];
            if (!cf.AppliesTo.Contains(addr))
                continue;

            CfStyleResult? matchedStyle = null;
            bool conditionMet;
            if (cf.RuleType == CfRuleType.ColorScale)
            {
                var colorScaleStyle = ComputeColorScaleStyle(cf, value, sheet, workbook, addr, cfContext);
                conditionMet = colorScaleStyle is not null;
                if (colorScaleStyle is not null)
                    matchedStyle = new CfStyleResult(colorScaleStyle, CanUseAsDefaultMergedStyle: true);
            }
            else if (cf.RuleType == CfRuleType.DataBar)
            {
                conditionMet = TryGetDouble(value, out _);
                if (conditionMet)
                    matchedStyle = new CfStyleResult(
                        new CellStyle { FillColor = cf.DataBarColor.ToCellColor() },
                        CanUseAsDefaultMergedStyle: true);
            }
            else
            {
                conditionMet = cf.RuleType switch
                {
                    CfRuleType.CellValue => MatchesCellValue(cf, value, sheet, workbook, addr, cfContext),
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

                if (conditionMet && cf.FormatIfTrue is not null)
                {
                    matchedStyle = cfContext.DefaultMergedFormatStyles.TryGetValue(cf, out var defaultMergedStyle)
                        ? new CfStyleResult(defaultMergedStyle, CanUseAsDefaultMergedStyle: true)
                        : new CfStyleResult(cf.FormatIfTrue, CanUseAsDefaultMergedStyle: false);
                }
            }

            if (!conditionMet)
                continue;

            if (matchedStyle is { } styleResult)
            {
                result = result is null
                    ? styleResult
                    : new CfStyleResult(
                        StackDifferentialStyle(result.Value.Style, styleResult.Style),
                        CanUseAsDefaultMergedStyle: true);
            }

            if (cf.StopIfTrue)
                break;
        }

        return result;
    }

    private static Dictionary<ConditionalFormat, CellStyle> PrecomputeDefaultMergedFormatStyles(
        IReadOnlyList<ConditionalFormat> rulesByPriority)
    {
        Dictionary<ConditionalFormat, CellStyle>? result = null;
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            var cf = rulesByPriority[i];
            if (cf.FormatIfTrue is null)
                continue;

            result ??= new Dictionary<ConditionalFormat, CellStyle>(ReferenceEqualityComparer.Instance);
            result[cf] = MergeStyles(CellStyle.Default, cf.FormatIfTrue);
        }

        return result ?? EmptyDefaultMergedFormatStyles;
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

    private static CellStyle StackDifferentialStyle(CellStyle? accumulatedStyle, CellStyle cfStyle)
    {
        var result = (accumulatedStyle ?? CellStyle.Default).Clone();

        if (!result.FillColor.HasValue && cfStyle.FillColor.HasValue)
            result.FillColor = cfStyle.FillColor;
        if (result.FillPatternStyle == CellFillPatternStyle.None &&
            cfStyle.FillPatternStyle != CellFillPatternStyle.None)
            result.FillPatternStyle = cfStyle.FillPatternStyle;
        if (!result.FillPatternColor.HasValue && cfStyle.FillPatternColor.HasValue)
            result.FillPatternColor = cfStyle.FillPatternColor;

        if (cfStyle.Bold)
            result.Bold = true;
        if (cfStyle.Italic)
            result.Italic = true;
        if (cfStyle.Underline)
            result.Underline = true;
        if (result.FontColor == CellColor.Black && cfStyle.FontColor != CellColor.Black)
            result.FontColor = cfStyle.FontColor;

        return result;
    }

    private static Dictionary<ConditionalFormat, CfFormulaCache> PrecomputeFormulaCaches(Sheet sheet)
    {
        Dictionary<ConditionalFormat, CfFormulaCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (cf.RuleType != CfRuleType.Formula || string.IsNullOrWhiteSpace(cf.FormulaText))
                continue;

            try
            {
                var ast = ParseFormulaText(cf.FormulaText);
                var simpleComparison = TryCreateSimpleComparison(ast, out var comparison)
                    ? comparison
                    : (CfSimpleFormulaComparison?)null;
                var simpleAnd = simpleComparison is null && TryCreateSimpleAnd(ast, out var and)
                    ? and
                    : null;

                (result ??= new Dictionary<ConditionalFormat, CfFormulaCache>(ReferenceEqualityComparer.Instance))[cf] = new CfFormulaCache(
                    ast,
                    HasRelativeReferences(ast),
                    simpleComparison,
                    simpleAnd);
            }
            catch
            {
                // Preserve formula CF error handling: invalid formulas do not match.
            }
        }

        return result ?? EmptyFormulas;
    }

    private static bool TryCreateSimpleComparison(FormulaNode ast, out CfSimpleFormulaComparison comparison)
    {
        comparison = default;
        if (ast is not BinaryOpNode binary || !IsComparisonOperator(binary.Operator))
            return false;

        if (!TryCreateSimpleOperand(binary.Left, out var left) ||
            !TryCreateSimpleOperand(binary.Right, out var right))
            return false;

        comparison = new CfSimpleFormulaComparison(left, binary.Operator, right);
        return true;
    }

    private static bool TryCreateSimpleAnd(FormulaNode ast, out CfSimpleFormulaAnd and)
    {
        and = default!;
        if (ast is not FunctionCallNode { Arguments.Count: > 0 } function ||
            !string.Equals(function.FunctionName, "AND", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comparisons = new CfSimpleFormulaComparison[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryCreateSimpleComparison(function.Arguments[i], out comparisons[i]))
                return false;
        }

        and = new CfSimpleFormulaAnd(comparisons);
        return true;
    }

    private static bool IsComparisonOperator(BinaryOperator op) =>
        op is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.GreaterThan
            or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterOrEqual;

    private static bool TryCreateSimpleOperand(FormulaNode node, out CfFormulaScalarOperand operand)
    {
        operand = default;
        switch (node)
        {
            case CellRefNode cell:
                operand = new CfFormulaScalarOperand(
                    CfFormulaScalarOperandKind.Reference,
                    null,
                    cell.Row,
                    cell.ColumnNumber,
                    cell.IsRowAbsolute,
                    cell.IsColAbsolute,
                    cell.SheetName);
                return true;
            case NumberNode number:
                operand = LiteralOperand(new NumberValue(number.Value));
                return true;
            case StringNode text:
                operand = LiteralOperand(new TextValue(text.Value));
                return true;
            case BooleanNode boolean:
                operand = LiteralOperand(new BoolValue(boolean.Value));
                return true;
            case ErrorNode error:
                operand = LiteralOperand(error.Error);
                return true;
            default:
                return false;
        }
    }

    private static CfFormulaScalarOperand LiteralOperand(ScalarValue value) =>
        new(CfFormulaScalarOperandKind.Literal, value, 0, 0, true, true, null);

    private static bool HasRelativeReferences(FormulaNode node)
    {
        return node switch
        {
            CellRefNode cr => !cr.IsColAbsolute || !cr.IsRowAbsolute,
            RangeRefNode rr => HasRelativeReferences(rr.Start) || HasRelativeReferences(rr.End),
            FullColumnRangeRefNode fcr => !fcr.IsStartAbsolute || !fcr.IsEndAbsolute,
            FullRowRangeRefNode frr => !frr.IsStartAbsolute || !frr.IsEndAbsolute,
            BinaryOpNode bin => HasRelativeReferences(bin.Left) || HasRelativeReferences(bin.Right),
            UnaryOpNode un => HasRelativeReferences(un.Operand),
            FunctionCallNode fn => HasRelativeReferences(fn.Arguments),
            _ => false
        };
    }

    private static bool HasRelativeReferences(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (HasRelativeReferences(nodes[i]))
                return true;
        }

        return false;
    }

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
            if (cf.IconSetThresholds.Count < thresholdCount)
                continue;

            var values = new double[thresholdCount];
            var comparisons = new bool[thresholdCount];
            var resolved = true;
            for (var i = 0; i < thresholdCount; i++)
            {
                var threshold = cf.IconSetThresholds[i];
                if (threshold.Type == CfThresholdType.Formula)
                {
                    if (!staticThresholdFormulaValues.TryGetValue(
                            new CfThresholdFormulaKey(cf, CfThresholdFormulaSlot.IconSet, i),
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
        var formula = text is { Length: > 0 } && text[0] == '=' ? text : "=" + text;
        return new Parser(new Lexer(formula).Tokenize()).Parse();
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
        Dictionary<ConditionalFormat, CfAggregateCache>? result = null;
        foreach (var cf in sheet.ConditionalFormats)
        {
            if (!RequiresAggregateCache(cf))
                continue;

            double sum = 0, min = double.MaxValue, max = double.MinValue;
            int count = 0;
            List<(CellAddress Address, double Value, int Index)>? rankedValues =
                cf.RuleType == CfRuleType.Top10 ? [] : null;
            Dictionary<string, int>? valueCounts =
                cf.RuleType is CfRuleType.DuplicateValues or CfRuleType.UniqueValues
                    ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    : null;
            List<double>? numericValues = RequiresSortedNumericValues(cf) ? [] : null;
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
                    rankedValues?.Add((a, x, count));
                    numericValues?.Add(x);
                    count++;
                }
            }

            var topBottomMatches = ResolveTopBottomMatches(cf, rankedValues);
            numericValues?.Sort();
            if (count > 0 || valueCounts?.Count > 0 || topBottomMatches is not null)
                (result ??= new Dictionary<ConditionalFormat, CfAggregateCache>(ReferenceEqualityComparer.Instance))[cf] = new CfAggregateCache(
                    count > 0 ? sum / count : 0,
                    count > 0 ? min : 0,
                    count > 0 ? max : 0,
                    numericValues,
                    topBottomMatches,
                    valueCounts?.Count > 0 ? valueCounts : null);
        }
        return result ?? EmptyAggregates;
    }

    private static bool RequiresAggregateCache(ConditionalFormat cf) =>
        cf.RuleType switch
        {
            CfRuleType.AboveAverage or
            CfRuleType.ColorScale or
            CfRuleType.Top10 or
            CfRuleType.DuplicateValues or
            CfRuleType.UniqueValues => true,
            CfRuleType.IconSet => RequiresIconSetAggregateCache(cf),
            _ => false
        };

    private static bool RequiresIconSetAggregateCache(ConditionalFormat cf)
    {
        var thresholdCount = GetIconSetCount(cf.IconSetStyle) - 1;
        if (cf.IconSetThresholds.Count < thresholdCount)
            return true;

        for (var i = 0; i < thresholdCount; i++)
        {
            var threshold = cf.IconSetThresholds[i];
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

        if (cf.RuleType != CfRuleType.IconSet)
            return false;

        var thresholdCount = Math.Min(GetIconSetCount(cf.IconSetStyle) - 1, cf.IconSetThresholds.Count);
        for (var i = 0; i < thresholdCount; i++)
        {
            if (cf.IconSetThresholds[i].Type == CfThresholdType.Percentile)
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

    internal static FormulaNode GetShiftedConditionalFormatFormula(
        FormulaNode ast,
        CellAddress anchorCell,
        CellAddress currentCell,
        bool? hasRelativeReferences = null)
    {
        int dr = (int)currentCell.Row - (int)anchorCell.Row;
        int dc = (int)currentCell.Col - (int)anchorCell.Col;
        if ((dr == 0 && dc == 0) || !(hasRelativeReferences ?? HasRelativeReferences(ast)))
            return ast;

        return ShiftAst(ast, dr, dc);
    }

    private static FormulaNode ShiftAst(FormulaNode node, int dr, int dc)
    {
        return node switch
        {
            CellRefNode cr => ShiftCellRef(cr, dr, dc),
            RangeRefNode rr => ShiftRangeRef(rr, dr, dc),
            FullColumnRangeRefNode fcr => ShiftFullColumnRangeRef(fcr, dc),
            FullRowRangeRefNode frr => ShiftFullRowRangeRef(frr, dr),
            BinaryOpNode bin => ShiftBinaryOp(bin, dr, dc),
            UnaryOpNode un => ShiftUnaryOp(un, dr, dc),
            FunctionCallNode fn => ShiftFunctionCall(fn, dr, dc),
            _ => node
        };
    }

    private static FormulaNode ShiftBinaryOp(BinaryOpNode node, int dr, int dc)
    {
        var left = ShiftAst(node.Left, dr, dc);
        var right = ShiftAst(node.Right, dr, dc);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    private static FormulaNode ShiftUnaryOp(UnaryOpNode node, int dr, int dc)
    {
        var operand = ShiftAst(node.Operand, dr, dc);
        return ReferenceEquals(operand, node.Operand)
            ? node
            : node with { Operand = operand };
    }

    private static FormulaNode ShiftFunctionCall(FunctionCallNode node, int dr, int dc)
    {
        List<FormulaNode>? shiftedArgs = null;
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            var original = node.Arguments[i];
            var shifted = ShiftAst(original, dr, dc);
            if (shiftedArgs is not null)
            {
                shiftedArgs.Add(shifted);
                continue;
            }

            if (ReferenceEquals(shifted, original))
                continue;

            shiftedArgs = new List<FormulaNode>(node.Arguments.Count);
            for (var j = 0; j < i; j++)
                shiftedArgs.Add(node.Arguments[j]);
            shiftedArgs.Add(shifted);
        }

        return shiftedArgs is null
            ? node
            : node with { Arguments = shiftedArgs };
    }

    private static FormulaNode ShiftRangeRef(RangeRefNode rr, int dr, int dc)
    {
        var start = ShiftCellRefOrError(rr.Start, dr, dc);
        if (start is ErrorNode) return start;

        var end = ShiftCellRefOrError(rr.End, dr, dc);
        if (end is ErrorNode) return end;

        if (ReferenceEquals(start, rr.Start) && ReferenceEquals(end, rr.End))
            return rr;

        return rr with
        {
            Start = (CellRefNode)start,
            End = (CellRefNode)end
        };
    }

    private static FormulaNode ShiftFullColumnRangeRef(FullColumnRangeRefNode range, int dc)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftColumn(range.StartColumnNumber, range.IsStartAbsolute, dc);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftColumn(range.EndColumnNumber, range.IsEndAbsolute, dc);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        var startName = range.IsStartAbsolute ? range.StartColumnName : CellAddress.NumberToColumnName(start.Value);
        var endName = range.IsEndAbsolute ? range.EndColumnName : CellAddress.NumberToColumnName(end.Value);
        if (startName == range.StartColumnName && endName == range.EndColumnName)
            return range;

        return range with
        {
            StartColumnName = startName,
            EndColumnName = endName
        };
    }

    private static FormulaNode ShiftFullRowRangeRef(FullRowRangeRefNode range, int dr)
    {
        if (range.IsStartAbsolute && range.IsEndAbsolute)
            return range;

        var start = ShiftRow(range.StartRow, range.IsStartAbsolute, dr);
        if (!start.HasValue) return new ErrorNode(ErrorValue.Ref);

        var end = ShiftRow(range.EndRow, range.IsEndAbsolute, dr);
        if (!end.HasValue) return new ErrorNode(ErrorValue.Ref);

        if (start.Value == range.StartRow && end.Value == range.EndRow)
            return range;

        return range with
        {
            StartRow = start.Value,
            EndRow = end.Value
        };
    }

    private static FormulaNode ShiftCellRef(CellRefNode cr, int dr, int dc) =>
        ShiftCellRefOrError(cr, dr, dc);

    private static FormulaNode ShiftCellRefOrError(CellRefNode cr, int dr, int dc)
    {
        if (cr.IsRowAbsolute && cr.IsColAbsolute)
            return cr;

        var newRow = ShiftRow(cr.Row, cr.IsRowAbsolute, dr);
        if (!newRow.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColNum = ShiftColumn(cr.ColumnNumber, cr.IsColAbsolute, dc);
        if (!newColNum.HasValue) return new ErrorNode(ErrorValue.Ref);

        var newColName = cr.IsColAbsolute ? cr.ColumnName : CellAddress.NumberToColumnName(newColNum.Value);
        if (newRow.Value == cr.Row && newColName == cr.ColumnName)
            return cr;

        return cr with { Row = newRow.Value, ColumnName = newColName };
    }

    internal static uint? ShiftRow(uint row, bool isAbsolute, int dr)
    {
        if (isAbsolute)
            return row;

        var shifted = (long)row + dr;
        return shifted is < 1 or > CellAddress.MaxRow ? null : (uint)shifted;
    }

    internal static uint? ShiftColumn(uint col, bool isAbsolute, int dc)
    {
        if (isAbsolute)
            return col;

        var shifted = (long)col + dc;
        return shifted is < 1 or > CellAddress.MaxCol ? null : (uint)shifted;
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

        if (max <= min) return new CellStyle { FillColor = cf.MinColor.ToCellColor() };

        var interpolated = mid.HasValue
            ? cellVal <= mid.Value
                ? Lerp(cf.MinColor, cf.MidColor, Math.Clamp((cellVal - min) / (mid.Value - min), 0d, 1d))
                : Lerp(cf.MidColor, cf.MaxColor, Math.Clamp((cellVal - mid.Value) / (max - mid.Value), 0d, 1d))
            : Lerp(cf.MinColor, cf.MaxColor, Math.Clamp((cellVal - min) / (max - min), 0d, 1d));

        return new CellStyle { FillColor = interpolated };
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
