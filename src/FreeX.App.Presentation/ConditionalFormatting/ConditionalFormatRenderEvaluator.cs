using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable differential style produced by matched conditional-format rules. Properties are already
/// stacked in rule-priority order, with the first rule to set a color winning.
/// </summary>
public readonly record struct ConditionalFormatStylePlan(
    CellColor? FillColor,
    CellColor? FontColor,
    bool Bold,
    bool Italic,
    bool Underline,
    string? NumberFormat,
    CellBorder BorderTop,
    CellBorder BorderRight,
    CellBorder BorderBottom,
    CellBorder BorderLeft);

/// <summary>Renderer-neutral conditional-format result for one worksheet cell.</summary>
public readonly record struct ConditionalFormatCellPlan(
    ConditionalFormatStylePlan? Style,
    DataBarLayout? DataBar,
    IconSetResult? IconSet);

/// <summary>
/// Evaluates the portable subset of worksheet conditional formatting used by print preview and PDF.
/// One instance owns the priority ordering and lazy range-statistics cache for a sheet render pass.
/// </summary>
public sealed class ConditionalFormatRenderEvaluator
{
    private const long DenseScanLimit = 10_000;

    private readonly Sheet _sheet;
    private readonly Workbook _workbook;
    private readonly IReadOnlyList<ConditionalFormat> _rulesByPriority;
    private readonly Dictionary<ConditionalFormat, ConditionalFormatStatistics> _statisticsCache =
        new(ReferenceEqualityComparer.Instance);

    // Backs Formula/Top10/Duplicate/Unique/text/date/blank/error rule evaluation: these delegate to
    // ViewportConditionalFormatEvaluator.MatchesRuleCondition (the same condition logic the screen
    // renderer uses via ViewportService) instead of a second, independently-maintained
    // implementation. Built once per evaluator instance -- i.e. once per sheet render pass, matching
    // this evaluator's existing per-sheet statistics cache lifetime.
    private readonly CfEvaluationContext _cfContext;

    public ConditionalFormatRenderEvaluator(Sheet sheet, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(workbook);

        _sheet = sheet;
        _workbook = workbook;
        _rulesByPriority = OrderRulesByPriority(sheet.ConditionalFormats);
        _cfContext = ViewportConditionalFormatEvaluator.BuildContext(sheet, workbook);
    }

    public bool HasRules => _rulesByPriority.Count > 0;

    /// <summary>
    /// Evaluates applicable rules in Excel priority order. Style properties stack with first-property-
    /// wins semantics; only the first data bar and icon set are retained. A matching StopIfTrue rule
    /// prevents all lower-priority rules from contributing.
    /// </summary>
    public ConditionalFormatCellPlan Evaluate(CellAddress address, ScalarValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ConditionalFormatStylePlan? style = null;
        DataBarLayout? dataBar = null;
        IconSetResult? iconSet = null;

        for (var i = 0; i < _rulesByPriority.Count; i++)
        {
            var rule = _rulesByPriority[i];
            if (!rule.AllRanges.Any(range => range.Contains(address)))
                continue;

            var conditionMet = EvaluateRule(rule, address, value, out var ruleStyle, out var ruleDataBar, out var ruleIconSet);

            if (ruleStyle is { } matchedStyle)
                style = style is { } accumulated ? StackStyle(accumulated, matchedStyle) : matchedStyle;
            if (dataBar is null && ruleDataBar is { } matchedDataBar)
                dataBar = matchedDataBar;
            if (iconSet is null && ruleIconSet is { } matchedIconSet)
                iconSet = matchedIconSet;

            if (conditionMet && rule.StopIfTrue)
                break;
        }

        return new ConditionalFormatCellPlan(style, dataBar, iconSet);
    }

    private bool EvaluateRule(
        ConditionalFormat rule,
        CellAddress address,
        ScalarValue value,
        out ConditionalFormatStylePlan? style,
        out DataBarLayout? dataBar,
        out IconSetResult? iconSet)
    {
        style = null;
        dataBar = null;
        iconSet = null;

        switch (rule.RuleType)
        {
            case CfRuleType.ColorScale:
            {
                if (!TryGetNumeric(value, out var numeric))
                    return false;

                var scale = ConditionalFormatEvaluator.EvaluateColorScale(rule, numeric, GetStatistics(rule));
                if (scale is null)
                    return false;

                style = new ConditionalFormatStylePlan(
                    scale.Value.Fill.ToCellColor(),
                    null,
                    false,
                    false,
                    false,
                    null,
                    default,
                    default,
                    default,
                    default);
                return true;
            }
            case CfRuleType.CellValue:
            {
                if (!TryGetNumeric(value, out var numeric) ||
                    !ConditionalFormatEvaluator.MatchesCellValueNumeric(rule, numeric))
                {
                    return false;
                }

                if (rule.FormatIfTrue is { } formatIfTrue)
                    style = ExtractStyle(formatIfTrue);
                return true;
            }
            case CfRuleType.AboveAverage:
            {
                if (!TryGetNumeric(value, out var numeric) ||
                    !ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, numeric, GetStatistics(rule)))
                {
                    return false;
                }

                if (rule.FormatIfTrue is { } formatIfTrue)
                    style = ExtractStyle(formatIfTrue);
                return true;
            }
            case CfRuleType.DataBar:
            {
                if (!TryGetNumeric(value, out var numeric))
                    return false;

                // A finite numeric value matches even when invalid thresholds produce no bar. This
                // matters for StopIfTrue and mirrors the interactive evaluator.
                dataBar = ConditionalFormatEvaluator.EvaluateDataBar(rule, numeric, GetStatistics(rule));
                return true;
            }
            case CfRuleType.IconSet:
            {
                if (!TryGetNumeric(value, out var numeric))
                    return false;

                iconSet = ConditionalFormatEvaluator.EvaluateIconSet(rule, numeric, GetStatistics(rule));
                return true;
            }
            // Formula, Top10, Duplicate/Unique Values, the four text rules, DateOccurring, and
            // Blanks/Errors/NoBlanks/NoErrors are all style-only (never icon set or data bar) rule
            // types. Rather than re-implementing their condition logic a second time here (which
            // previously fell into the `default: return false` branch below and silently dropped
            // these rule types from print/PDF), delegate the match to the same
            // ViewportConditionalFormatEvaluator.MatchesRuleCondition the screen renderer uses --
            // including its Formula-rule matcher (ViewportService.MatchesFormula) -- so print/PDF
            // and the on-screen grid can never drift apart on what these rules match.
            case CfRuleType.Formula:
            case CfRuleType.Top10:
            case CfRuleType.DuplicateValues:
            case CfRuleType.UniqueValues:
            case CfRuleType.ContainsText:
            case CfRuleType.NotContainsText:
            case CfRuleType.BeginsWith:
            case CfRuleType.EndsWith:
            case CfRuleType.DateOccurring:
            case CfRuleType.Blanks:
            case CfRuleType.NoBlanks:
            case CfRuleType.Errors:
            case CfRuleType.NoErrors:
            {
                var matched = ViewportConditionalFormatEvaluator.MatchesRuleCondition(
                    rule, _sheet, address, value, _workbook, _cfContext, ViewportService.MatchesFormula, out _);
                if (!matched)
                    return false;

                if (rule.FormatIfTrue is { } formatIfTrue)
                    style = ExtractStyle(formatIfTrue);
                return true;
            }
            default:
                return false;
        }
    }

    private ConditionalFormatStatistics GetStatistics(ConditionalFormat rule)
    {
        if (_statisticsCache.TryGetValue(rule, out var cached))
            return cached;

        var statistics = ConditionalFormatStatistics.FromValues(EnumerateNumericValues(rule));
        _statisticsCache[rule] = statistics;
        return statistics;
    }

    private IEnumerable<double> EnumerateNumericValues(ConditionalFormat rule)
    {
        var ranges = rule.AllRanges.ToList();
        var seen = ranges.Count > 1 ? new HashSet<CellAddress>() : null;

        foreach (var range in ranges)
        {
            if (range.CellCount <= DenseScanLimit)
            {
                foreach (var address in range.AllCells())
                {
                    if (seen is not null && !seen.Add(address))
                        continue;
                    if (TryGetNumeric(_sheet.GetValue(address), out var numeric))
                        yield return numeric;
                }

                continue;
            }

            foreach (var (address, cell) in _sheet.EnumerateCells())
            {
                if (!range.Contains(address))
                    continue;
                if (seen is not null && !seen.Add(address))
                    continue;
                if (TryGetNumeric(cell.Value, out var numeric))
                    yield return numeric;
            }
        }
    }

    private static IReadOnlyList<ConditionalFormat> OrderRulesByPriority(IReadOnlyList<ConditionalFormat> rules)
    {
        if (rules.Count == 0)
            return [];

        var indexed = new (ConditionalFormat Rule, int Index)[rules.Count];
        for (var i = 0; i < rules.Count; i++)
            indexed[i] = (rules[i], i);

        Array.Sort(indexed, static (left, right) =>
        {
            var priorityOrder = left.Rule.Priority.CompareTo(right.Rule.Priority);
            return priorityOrder != 0 ? priorityOrder : left.Index.CompareTo(right.Index);
        });

        var ordered = new ConditionalFormat[indexed.Length];
        for (var i = 0; i < indexed.Length; i++)
            ordered[i] = indexed[i].Rule;
        return ordered;
    }

    private static ConditionalFormatStylePlan ExtractStyle(CellStyle style) =>
        new(
            style.FillColor,
            style.FontColor != CellColor.Black ? style.FontColor : null,
            style.Bold,
            style.Italic,
            style.Underline,
            // Mirrors ViewportConditionalFormatEvaluator.MergeStyles: a dxf number format only counts
            // as an override when it's explicitly set to something other than "General".
            !string.IsNullOrEmpty(style.NumberFormat) &&
            !string.Equals(style.NumberFormat, "General", StringComparison.OrdinalIgnoreCase)
                ? style.NumberFormat
                : null,
            style.BorderTop,
            style.BorderRight,
            style.BorderBottom,
            style.BorderLeft);

    private static ConditionalFormatStylePlan StackStyle(
        ConditionalFormatStylePlan accumulated,
        ConditionalFormatStylePlan next) =>
        new(
            accumulated.FillColor ?? next.FillColor,
            accumulated.FontColor ?? next.FontColor,
            accumulated.Bold || next.Bold,
            accumulated.Italic || next.Italic,
            accumulated.Underline || next.Underline,
            // First matching (highest-priority) rule that specifies a number format wins, matching
            // ViewportConditionalFormatEvaluator.StackDifferentialStyle's "first matching rule wins"
            // semantics for the on-screen grid.
            accumulated.NumberFormat ?? next.NumberFormat,
            accumulated.BorderTop.Style != BorderStyle.None ? accumulated.BorderTop : next.BorderTop,
            accumulated.BorderRight.Style != BorderStyle.None ? accumulated.BorderRight : next.BorderRight,
            accumulated.BorderBottom.Style != BorderStyle.None ? accumulated.BorderBottom : next.BorderBottom,
            accumulated.BorderLeft.Style != BorderStyle.None ? accumulated.BorderLeft : next.BorderLeft);

    private static bool TryGetNumeric(ScalarValue value, out double result)
    {
        switch (value)
        {
            case NumberValue number:
                result = number.Value;
                return double.IsFinite(result);
            case DateTimeValue date:
                result = date.Value;
                return double.IsFinite(result);
            default:
                result = 0;
                return false;
        }
    }
}
