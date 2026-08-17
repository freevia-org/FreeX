using System.Runtime.CompilerServices;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

internal sealed record CfAggregateCache(
    int Count,
    double Average,
    double Min,
    double Max,
    IReadOnlyList<double>? SortedValues = null,
    IReadOnlySet<CellAddress>? TopBottomMatches = null,
    IReadOnlyDictionary<string, int>? ValueCounts = null,
    double StdDev = 0);

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
    IReadOnlyList<GridRange> StyleRuleRanges,
    CfColorScaleStyleCache? ColorScaleStyles,
    CfStackedStyleCache? StackedStyles,
    CfFormulaResultCache FormulaResults);

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

internal sealed class CfStackedStyleCache
{
    private Dictionary<CfStackedStyleKey, CellStyle>? _styles;

    public bool TryGet(CellStyle accumulatedStyle, CellStyle cfStyle, out CellStyle stackedStyle)
    {
        stackedStyle = null!;
        return _styles is not null &&
               _styles.TryGetValue(new CfStackedStyleKey(accumulatedStyle, cfStyle), out stackedStyle!);
    }

    public void Add(CellStyle accumulatedStyle, CellStyle cfStyle, CellStyle stackedStyle)
    {
        (_styles ??= new Dictionary<CfStackedStyleKey, CellStyle>(8))
            .Add(new CfStackedStyleKey(accumulatedStyle, cfStyle), stackedStyle);
    }
}

internal readonly struct CfStackedStyleKey : IEquatable<CfStackedStyleKey>
{
    private readonly CellStyle _accumulatedStyle;
    private readonly CellStyle _cfStyle;

    public CfStackedStyleKey(CellStyle accumulatedStyle, CellStyle cfStyle)
    {
        _accumulatedStyle = accumulatedStyle;
        _cfStyle = cfStyle;
    }

    public bool Equals(CfStackedStyleKey other) =>
        ReferenceEquals(_accumulatedStyle, other._accumulatedStyle) &&
        ReferenceEquals(_cfStyle, other._cfStyle);

    public override bool Equals(object? obj) => obj is CfStackedStyleKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            RuntimeHelpers.GetHashCode(_accumulatedStyle),
            RuntimeHelpers.GetHashCode(_cfStyle));
}

/// <summary>
/// Caches the evaluated boolean result of a Formula-type conditional-format rule per (rule, cell).
/// A rule's formula (e.g. "=RAND()&gt;0.5") is otherwise re-evaluated on every <see cref="ViewportService.GetViewport"/>
/// call, including pure re-renders (scroll/resize) that touch no content — which makes a rule built on a
/// volatile function like RAND()/NOW() flicker on every render instead of only on a genuine recalc. This
/// cache lives on <see cref="CfEvaluationContext"/>, which is itself rebuilt only when
/// Sheet.ContentVersion or the conditional-format rule set actually changes (see
/// ViewportService.BuildConditionalFormatContext), so the cached result is invalidated exactly on a real
/// recalc/content change and reused across render-only viewport requests in between.
/// </summary>
internal sealed class CfFormulaResultCache
{
    private Dictionary<CfFormulaResultKey, bool>? _results;

    public bool TryGet(ConditionalFormat rule, CellAddress address, out bool result)
    {
        if (_results is not null)
            return _results.TryGetValue(new CfFormulaResultKey(rule, address), out result);

        result = false;
        return false;
    }

    public void Set(ConditionalFormat rule, CellAddress address, bool result) =>
        (_results ??= new Dictionary<CfFormulaResultKey, bool>(64))[new CfFormulaResultKey(rule, address)] = result;
}

internal readonly struct CfFormulaResultKey : IEquatable<CfFormulaResultKey>
{
    private readonly ConditionalFormat _rule;
    private readonly CellAddress _address;

    public CfFormulaResultKey(ConditionalFormat rule, CellAddress address)
    {
        _rule = rule;
        _address = address;
    }

    public bool Equals(CfFormulaResultKey other) =>
        ReferenceEquals(_rule, other._rule) && _address.Equals(other._address);

    public override bool Equals(object? obj) => obj is CfFormulaResultKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(_rule), _address);
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
    DataBarMin,
    DataBarMax,
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
    private static readonly GridRange[] EmptyStyleRuleRanges = [];
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
        EmptyStyleRuleRanges,
        null,
        null,
        new CfFormulaResultCache());

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
            PrecomputeStyleRuleRanges(rulesByPriority),
            CreateColorScaleStyleCache(rulesByPriority),
            CreateStackedStyleCache(rulesByPriority),
            new CfFormulaResultCache());
    }

    // Flattened ranges of every rule that can produce a conditional style. Blank viewport slots
    // consult these to decide whether conditional formatting must run for them at all; icon-set
    // and data-bar rules are excluded because they require numeric values and never fire on blanks.
    private static GridRange[] PrecomputeStyleRuleRanges(IReadOnlyList<ConditionalFormat> rulesByPriority)
    {
        List<GridRange>? ranges = null;
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            var rule = rulesByPriority[i];
            if (!CanProduceConditionalStyle(rule))
                continue;

            ranges ??= [];
            ranges.Add(rule.AppliesTo);
            if (rule.AdditionalRanges is { } additionalRanges)
            {
                for (var rangeIndex = 0; rangeIndex < additionalRanges.Count; rangeIndex++)
                    ranges.Add(additionalRanges[rangeIndex]);
            }
        }

        return ranges is null ? EmptyStyleRuleRanges : [.. ranges];
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

    private static CfStackedStyleCache? CreateStackedStyleCache(IReadOnlyList<ConditionalFormat> rulesByPriority)
    {
        var styleRuleCount = 0;
        for (var i = 0; i < rulesByPriority.Count; i++)
        {
            if (!CanProduceConditionalStyle(rulesByPriority[i]))
                continue;

            styleRuleCount++;
            if (styleRuleCount > 1)
                return new CfStackedStyleCache();
        }

        return null;
    }

    private static bool CanProduceConditionalStyle(ConditionalFormat rule) =>
        rule.RuleType != CfRuleType.IconSet &&
        (rule.RuleType == CfRuleType.ColorScale || rule.FormatIfTrue is not null);

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
            if (!cf.AllRanges.Any(r => r.Contains(addr)))
                continue;

            CfStyleResult? matchedStyle = null;
            var conditionMet = MatchesRuleCondition(cf, sheet, addr, value, workbook, cfContext, matchesFormula, out var colorScaleStyle);
            if (cf.RuleType == CfRuleType.ColorScale)
            {
                if (colorScaleStyle is not null)
                    matchedStyle = new CfStyleResult(colorScaleStyle, CanUseAsDefaultMergedStyle: true);
            }
            else if (conditionMet && cf.FormatIfTrue is not null)
            {
                matchedStyle = cfContext.DefaultMergedFormatStyles.TryGetValue(cf, out var defaultMergedStyle)
                    ? new CfStyleResult(defaultMergedStyle, CanUseAsDefaultMergedStyle: true)
                    : new CfStyleResult(cf.FormatIfTrue, CanUseAsDefaultMergedStyle: false);
            }

            if (!conditionMet)
                continue;

            if (matchedStyle is { } styleResult)
            {
                result = result is null
                    ? styleResult
                    : new CfStyleResult(
                        GetStackedDifferentialStyle(cfContext, result.Value.Style, styleResult.Style),
                        CanUseAsDefaultMergedStyle: true);
            }

            if (cf.StopIfTrue)
                break;
        }

        return result;
    }

    /// <summary>
    /// Evaluates whether a single rule's condition is true for <paramref name="addr"/>, independent
    /// of rule kind. Shared by the style evaluator (<see cref="Evaluate"/>) and the icon-set/data-bar
    /// evaluators so that a higher-priority Stop-If-True rule of ANY kind (style, icon set, or data
    /// bar) can suppress a lower-priority icon-set or data-bar rule exactly like Excel does.
    /// </summary>
    // Internal (not private): shared with ConditionalFormatRenderEvaluator (FreeX.App.Presentation,
    // granted access via InternalsVisibleTo) so print preview and PDF export evaluate Formula, Top10,
    // Duplicate/Unique, text, DateOccurring, and Blanks/Errors rules through the same condition logic
    // the screen renderer uses, instead of a second, drift-prone implementation.
    internal static bool MatchesRuleCondition(
        ConditionalFormat cf,
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        Func<ConditionalFormat, Sheet, CellAddress, Workbook, CfEvaluationContext, bool> matchesFormula,
        out CellStyle? colorScaleStyle)
    {
        if (cf.RuleType == CfRuleType.ColorScale)
        {
            colorScaleStyle = ComputeColorScaleStyle(cf, value, sheet, workbook, addr, cfContext);
            return colorScaleStyle is not null;
        }

        colorScaleStyle = null;
        return cf.RuleType switch
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
            CfRuleType.IconSet => MatchesIconSetOrDataBarCondition(value),
            CfRuleType.DataBar => MatchesIconSetOrDataBarCondition(value),
            _ => false
        };
    }

    /// <summary>
    /// An icon set always sorts a finite numeric value into *some* icon bucket, and a data bar
    /// always renders (or would render, for Stop-If-True suppression purposes) for every finite
    /// numeric cell in its range -- exactly as Excel treats both rule kinds. Non-numeric cells
    /// (blank/text/error) never receive an icon or bar and so do not match. This mirrors the gate
    /// both <c>EvaluateConditionalIcon</c> (ViewportService.ConditionalFormatIcons.cs) and
    /// <see cref="EvaluateDataBar"/> apply before resolving a bucket/bar, so a matched higher-
    /// priority Stop-If-True IconSet/DataBar rule here is only reported as "condition met" when it
    /// would actually have produced an icon or bar for this cell.
    /// </summary>
    private static bool MatchesIconSetOrDataBarCondition(ScalarValue value) =>
        TryGetDouble(value, out var numeric) && double.IsFinite(numeric);

    /// <summary>
    /// Returns true when a rule strictly above <paramref name="belowPriorityRule"/> in priority order,
    /// applying to <paramref name="addr"/>, has its condition met AND is marked Stop-If-True. Excel
    /// suppresses ALL lower-priority conditional formatting (style, icon set, or data bar alike) once
    /// such a rule fires; icon-set and data-bar rules do not evaluate their own StopIfTrue flag against
    /// each other because Excel only ever displays one icon set and one data bar per cell regardless,
    /// but a Stop-If-True rule of any kind above them must still hide them.
    /// </summary>
    internal static bool IsSuppressedByHigherPriorityStopIfTrue(
        ConditionalFormat belowPriorityRule,
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
            if (ReferenceEquals(cf, belowPriorityRule))
                return false;

            if (!cf.StopIfTrue)
                continue;
            if (!cf.AllRanges.Any(r => r.Contains(addr)))
                continue;

            if (MatchesRuleCondition(cf, sheet, addr, value, workbook, cfContext, matchesFormula, out _))
                return true;
        }

        return false;
    }

    private static CellStyle GetStackedDifferentialStyle(
        CfEvaluationContext cfContext,
        CellStyle accumulatedStyle,
        CellStyle cfStyle)
    {
        if (cfContext.StackedStyles is null)
            return StackDifferentialStyle(accumulatedStyle, cfStyle);

        if (cfContext.StackedStyles.TryGet(accumulatedStyle, cfStyle, out var cached))
            return cached;

        var stacked = StackDifferentialStyle(accumulatedStyle, cfStyle);
        cfContext.StackedStyles.Add(accumulatedStyle, cfStyle, stacked);
        return stacked;
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

    /// <summary>
    /// Resolves a CF dxf's tri-state decision for one of the Bold/Italic/Underline/Strikethrough
    /// toggles: <paramref name="dxfValue"/> (the style's <c>Dxf*</c> field) wins when the dxf reader
    /// recorded an explicit on/off; otherwise falls back to treating <paramref name="plainValue"/> of
    /// true as an implicit "on" (matching every non-dxf CF style producer's existing convention) and
    /// false as "not specified". Returns null when the attribute was never specified at all, in which
    /// case the caller must leave the base/accumulated value untouched.
    /// </summary>
    private static bool? EffectiveToggle(bool? dxfValue, bool plainValue) =>
        dxfValue ?? (plainValue ? true : null);

    /// <summary>
    /// Resolves a CF dxf-derived style's font color decision, mirroring <see cref="EffectiveToggle"/>:
    /// <paramref name="dxfFontColor"/> (the style's <see cref="CellStyle.DxfFontColor"/>) wins when the
    /// dxf reader recorded an explicit color - including an explicit black, which the plain
    /// <paramref name="plainFontColor"/> value cannot distinguish from "never specified". Falls back to
    /// treating a non-black <paramref name="plainFontColor"/> as an implicit "explicitly set" (matching
    /// every non-dxf CF style producer's existing convention: UI/paste-built rules never set
    /// DxfFontColor). Returns null when no color was specified at all, in which case the caller must
    /// leave the base/accumulated color untouched.
    /// </summary>
    private static CellColor? EffectiveFontColor(CellColor? dxfFontColor, CellColor plainFontColor) =>
        dxfFontColor ?? (plainFontColor != CellColor.Black ? plainFontColor : null);

    public static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle)
    {
        var result = (baseStyle ?? CellStyle.Default).Clone();

        // A CF rule that specifies any fill (a flat color and/or a pattern) fully replaces the
        // base cell's background in Excel - a gradient fill or pattern hatch on the base cell
        // never shows through a matching CF fill, even when the CF itself only specifies a flat
        // color (the common case: dxf patternType omitted/"solid" -> FillPatternStyle stays None).
        // Clear the stale gradient and adopt the CF's pattern fields verbatim (including "None")
        // instead of only conditionally overwriting them, so a plain solid CF fill doesn't leave
        // the base cell's pattern hatch or gradient visible on top of/instead of the CF color.
        if (cfStyle.FillColor.HasValue || cfStyle.FillPatternStyle != CellFillPatternStyle.None)
        {
            result.GradientFill = null;
            result.FillPatternStyle = cfStyle.FillPatternStyle;
            result.FillPatternColor = cfStyle.FillPatternColor;
        }
        if (cfStyle.FillColor.HasValue)
            result.FillColor = cfStyle.FillColor;

        // Bold/Italic/Underline/Strikethrough: a dxf that explicitly turns one of these off (e.g. Format
        // Cells > Font > Font style = Regular over an already-bold base cell) must clear it, not just
        // leave it alone - Excel's CF format wins over the base format for every attribute the dxf
        // specifies, including "off". EffectiveToggle resolves the CF's tri-state Dxf* field (explicit
        // on/off) when present, falling back to the legacy "true means on, false means untouched"
        // reading of the plain bool for CF styles that never went through the dxf reader (tests, UI/paste
        // -built rules). The resolved value is also written back onto Dxf* on the result so a later merge
        // layer (e.g. this style being stacked again, or merged onto the real base cell style) still sees
        // that this attribute was explicitly decided.
        if (EffectiveToggle(cfStyle.DxfBold, cfStyle.Bold) is { } boldOverride)
        {
            result.Bold = boldOverride;
            result.DxfBold = boldOverride;
        }
        if (EffectiveToggle(cfStyle.DxfItalic, cfStyle.Italic) is { } italicOverride)
        {
            result.Italic = italicOverride;
            result.DxfItalic = italicOverride;
        }
        if (EffectiveToggle(cfStyle.DxfUnderline, cfStyle.Underline) is { } underlineOverride)
        {
            result.Underline = underlineOverride;
            result.DxfUnderline = underlineOverride;
        }
        if (EffectiveToggle(cfStyle.DxfStrikethrough, cfStyle.Strikethrough) is { } strikeOverride)
        {
            result.Strikethrough = strikeOverride;
            result.DxfStrikethrough = strikeOverride;
        }
        // An explicit CF font color always overrides the base cell's color, including an explicit
        // choice of black - EffectiveFontColor consults DxfFontColor (set by the dxf reader) so a
        // deliberately-authored black doesn't get mistaken for "no color specified" and skipped in
        // favor of the base cell's own color. The resolved value is also written back onto
        // DxfFontColor on the result so a later stacking layer still sees this as explicitly decided.
        if (EffectiveFontColor(cfStyle.DxfFontColor, cfStyle.FontColor) is { } fontColorOverride)
        {
            result.FontColor = fontColorOverride;
            result.DxfFontColor = fontColorOverride;
        }

        // dxf number format: override cell format when the CF explicitly specifies one.
        if (!string.IsNullOrEmpty(cfStyle.NumberFormat) &&
            !string.Equals(cfStyle.NumberFormat, "General", StringComparison.OrdinalIgnoreCase))
        {
            result.NumberFormat = cfStyle.NumberFormat;
        }

        // dxf borders: apply each edge from the CF when the CF dxf has a visible border on that edge.
        if (cfStyle.BorderTop.Style != BorderStyle.None)
            result.BorderTop = cfStyle.BorderTop;
        if (cfStyle.BorderRight.Style != BorderStyle.None)
            result.BorderRight = cfStyle.BorderRight;
        if (cfStyle.BorderBottom.Style != BorderStyle.None)
            result.BorderBottom = cfStyle.BorderBottom;
        if (cfStyle.BorderLeft.Style != BorderStyle.None)
            result.BorderLeft = cfStyle.BorderLeft;

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

        // First matching (highest-priority) rule that explicitly decides Bold/Italic/Underline/
        // Strikethrough wins, exactly like the "first matching rule wins" borders/number-format rule
        // below - so a lower-priority rule's explicit un-bold never re-overrides a higher-priority
        // rule's explicit bold, and vice-versa. result.Dxf* (already resolved on `result` by an earlier
        // MergeStyles/StackDifferentialStyle call, since accumulatedStyle is always itself prior merge
        // output) records whether this attribute has already been explicitly decided by a
        // higher-priority rule in the stack.
        if (!result.DxfBold.HasValue && EffectiveToggle(cfStyle.DxfBold, cfStyle.Bold) is { } boldOverride)
        {
            result.Bold = boldOverride;
            result.DxfBold = boldOverride;
        }
        if (!result.DxfItalic.HasValue && EffectiveToggle(cfStyle.DxfItalic, cfStyle.Italic) is { } italicOverride)
        {
            result.Italic = italicOverride;
            result.DxfItalic = italicOverride;
        }
        if (!result.DxfUnderline.HasValue && EffectiveToggle(cfStyle.DxfUnderline, cfStyle.Underline) is { } underlineOverride)
        {
            result.Underline = underlineOverride;
            result.DxfUnderline = underlineOverride;
        }
        if (!result.DxfStrikethrough.HasValue && EffectiveToggle(cfStyle.DxfStrikethrough, cfStyle.Strikethrough) is { } strikeOverride)
        {
            result.Strikethrough = strikeOverride;
            result.DxfStrikethrough = strikeOverride;
        }
        // First matching (highest-priority) rule that explicitly decides a font color wins, exactly
        // like the Bold/Italic/Underline/Strikethrough handling above: result.DxfFontColor (already
        // resolved on `result` by an earlier MergeStyles/StackDifferentialStyle call) records whether
        // a higher-priority rule already explicitly decided this attribute - including an explicit
        // black - so a lower-priority rule's non-black color never silently overwrites it.
        if (!result.DxfFontColor.HasValue &&
            EffectiveFontColor(cfStyle.DxfFontColor, cfStyle.FontColor) is { } fontColorOverride)
        {
            result.FontColor = fontColorOverride;
            result.DxfFontColor = fontColorOverride;
        }

        // dxf number format: first matching rule wins (highest-priority rule that specifies a format).
        if (string.Equals(result.NumberFormat, "General", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(cfStyle.NumberFormat) &&
            !string.Equals(cfStyle.NumberFormat, "General", StringComparison.OrdinalIgnoreCase))
        {
            result.NumberFormat = cfStyle.NumberFormat;
        }

        // dxf borders: first matching rule wins per edge (highest-priority rule that sets that edge).
        if (result.BorderTop.Style == BorderStyle.None && cfStyle.BorderTop.Style != BorderStyle.None)
            result.BorderTop = cfStyle.BorderTop;
        if (result.BorderRight.Style == BorderStyle.None && cfStyle.BorderRight.Style != BorderStyle.None)
            result.BorderRight = cfStyle.BorderRight;
        if (result.BorderBottom.Style == BorderStyle.None && cfStyle.BorderBottom.Style != BorderStyle.None)
            result.BorderBottom = cfStyle.BorderBottom;
        if (result.BorderLeft.Style == BorderStyle.None && cfStyle.BorderLeft.Style != BorderStyle.None)
            result.BorderLeft = cfStyle.BorderLeft;

        return result;
    }

    public static bool TryGetDouble(ScalarValue value, out double result)
    {
        if (value is NumberValue nv) { result = nv.Value; return true; }
        if (value is DateTimeValue dv) { result = dv.Value; return true; }
        result = 0;
        return false;
    }

    public static bool TryParseDouble(string? text, out double result) =>
        ConditionalFormatEvaluationMath.TryParseInvariant(text, out result);

}
