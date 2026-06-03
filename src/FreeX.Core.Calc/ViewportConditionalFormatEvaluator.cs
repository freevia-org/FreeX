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
    Dictionary<ConditionalFormat, CellStyle> DefaultMergedFormatStyles,
    CfColorScaleStyleCache? ColorScaleStyles);

internal sealed record CfColorScaleThresholdCache(double Min, double Max, double? Mid);
internal sealed record CfIconSetThresholdCache(double[] Values, bool[] GreaterThanOrEqual);

internal sealed class CfColorScaleStyleCache
{
    private Dictionary<CellColor, CellStyle>? _styles;

    public CellStyle Get(CellColor fillColor)
    {
        if (_styles is not null && _styles.TryGetValue(fillColor, out var cached))
            return cached;

        var style = new CellStyle { FillColor = fillColor };
        (_styles ??= new Dictionary<CellColor, CellStyle>(128)).Add(fillColor, style);
        return style;
    }
}

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

internal static partial class ViewportConditionalFormatEvaluator
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
        EmptyDefaultMergedFormatStyles,
        null);

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
            PrecomputeDefaultMergedFormatStyles(rulesByPriority),
            CreateColorScaleStyleCache(rulesByPriority));
    }

    private static CfColorScaleStyleCache? CreateColorScaleStyleCache(IReadOnlyList<ConditionalFormat> rulesByPriority)
    {
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            if (rulesByPriority[i].RuleType == CfRuleType.ColorScale)
                return new CfColorScaleStyleCache();
        }

        return null;
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

}
