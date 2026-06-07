using System.Globalization;
using System.Text.RegularExpressions;

using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
    private static readonly Regex FormulaDateTimeTextHasTimeSeparatorRegex = new(@"\d\s*:\s*\d");
    private static readonly Regex FormulaDateTimeTextHasAmPmRegex = new(@"\b(?:AM|PM)\b", RegexOptions.IgnoreCase);
    private static readonly Regex FormulaDateTimeTextHasDateSeparatorRegex = new(@"\d+\s*[-/]\s*\d+");
    private static readonly Regex FormulaDateTimeTextHasMonthNameRegex = new(
        @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex FormulaDateTimeFakeLeapDayTextRegex = new(
        @"^(?:2/29/1900|02/29/1900|1900-02-29)(?:\s+(.+))?$",
        RegexOptions.IgnoreCase);
    private static readonly TimeSpan FormulaTextSearchRegexTimeout = TimeSpan.FromSeconds(1);

    private static void AddLowContrastChartTextIssues(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        ChartModel chart)
    {
        var chartBackground = chart.ResolveChartAreaFillColor(workbook.Theme) ?? CellColor.White;
        var plotBackground = chart.ResolvePlotAreaFillColor(workbook.Theme) ?? chartBackground;
        var defaultText = chart.ChartDefaultTextThemeColor?.Resolve(workbook.Theme) ??
            chart.ChartDefaultTextColor ??
            CellColor.Black;

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "Chart title",
            chart.Title,
            chart.ResolveChartTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.ChartTitleFontSize);

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "X-axis title",
            chart.XAxisTitle,
            chart.ResolveAxisTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.AxisTitleFontSize);

        AddLowContrastChartTextIssue(
            issues,
            sheet,
            chart,
            "Y-axis title",
            chart.YAxisTitle,
            chart.ResolveAxisTitleTextColor(workbook.Theme) ?? defaultText,
            chartBackground,
            chart.AxisTitleFontSize);

        if (!chart.HideXAxis && chart.ShowXAxisLabels)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "X-axis labels",
                "Axis labels",
                chart.ResolveXAxisLabelTextColor(workbook.Theme) ?? defaultText,
                chartBackground,
                chart.XAxisLabelFontSize);
        }

        if (!chart.HideYAxis && chart.ShowYAxisLabels)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Y-axis labels",
                "Axis labels",
                chart.ResolveYAxisLabelTextColor(workbook.Theme) ?? defaultText,
                chartBackground,
                chart.YAxisLabelFontSize);
        }

        if (chart.ShowLegend)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Legend text",
                "Legend",
                chart.ResolveLegendTextColor(workbook.Theme) ?? defaultText,
                chart.ResolveLegendFillColor(workbook.Theme) ?? chartBackground,
                chart.LegendFontSize);
        }

        if (chart.ShowDataLabels)
        {
            var dataLabelTextColor = chart.ResolveDataLabelTextColor(workbook.Theme) ?? defaultText;
            var dataLabelFillColor = chart.ResolveDataLabelFillColor(workbook.Theme) ?? plotBackground;

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Data label text",
                "Data labels",
                dataLabelTextColor,
                dataLabelFillColor,
                chart.DataLabelFontSize);

            AddLowContrastChartDataLabelOverrideIssues(
                issues,
                workbook,
                sheet,
                chart,
                dataLabelTextColor,
                dataLabelFillColor);
        }

        if (chart.DataTable is { } dataTable)
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Chart data table text",
                "Data table",
                dataTable.TextThemeColor?.Resolve(workbook.Theme) ?? dataTable.TextColor ?? defaultText,
                dataTable.FillThemeColor?.Resolve(workbook.Theme) ?? dataTable.FillColor ?? chartBackground,
                dataTable.FontSize ?? chart.ChartDefaultFontSize);
        }

        if (chart.ShowLinearTrendline && (chart.ShowTrendlineEquation || chart.ShowTrendlineRSquared))
        {
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Trendline label text",
                "Trendline label",
                chart.TrendlineLabelTextThemeColor?.Resolve(workbook.Theme) ?? chart.TrendlineLabelTextColor ?? defaultText,
                chart.TrendlineLabelFillThemeColor?.Resolve(workbook.Theme) ?? chart.TrendlineLabelFillColor ?? chartBackground,
                chart.TrendlineLabelFontSize ?? chart.ChartDefaultFontSize);
        }
    }

    private static void AddLowContrastChartDataLabelOverrideIssues(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        ChartModel chart,
        CellColor dataLabelTextColor,
        CellColor dataLabelFillColor)
    {
        var seriesFormatsByIndex = chart.SeriesDataLabelFormats
            .GroupBy(format => format.SeriesIndex)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var seriesFormat in seriesFormatsByIndex.Values.OrderBy(format => format.SeriesIndex))
        {
            if (!HasSeriesDataLabelContrastOverride(seriesFormat) ||
                !IsSeriesDataLabelVisible(chart, seriesFormat))
            {
                continue;
            }

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Series data label text",
                "Data labels",
                seriesFormat.ResolveTextColor(workbook.Theme) ?? dataLabelTextColor,
                seriesFormat.ResolveFillColor(workbook.Theme) ?? dataLabelFillColor,
                seriesFormat.FontSize ?? chart.DataLabelFontSize);
        }

        foreach (var pointFormat in chart.PointDataLabelFormats
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex))
        {
            if (pointFormat.IsDeleted == true ||
                !HasPointDataLabelContrastOverride(pointFormat) ||
                !IsPointDataLabelVisible(chart, pointFormat, seriesFormatsByIndex.GetValueOrDefault(pointFormat.SeriesIndex)))
            {
                continue;
            }

            seriesFormatsByIndex.TryGetValue(pointFormat.SeriesIndex, out var seriesFormat);
            var inheritedTextColor = seriesFormat?.ResolveTextColor(workbook.Theme) ?? dataLabelTextColor;
            var inheritedFillColor = seriesFormat?.ResolveFillColor(workbook.Theme) ?? dataLabelFillColor;
            var inheritedFontSize = seriesFormat?.FontSize ?? chart.DataLabelFontSize;

            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Point data label text",
                "Data labels",
                pointFormat.ResolveTextColor(workbook.Theme) ?? inheritedTextColor,
                pointFormat.ResolveFillColor(workbook.Theme) ?? inheritedFillColor,
                pointFormat.FontSize ?? inheritedFontSize);
        }
    }

    private static bool HasSeriesDataLabelContrastOverride(ChartSeriesDataLabelFormat format) =>
        format.TextColor is not null ||
        format.TextThemeColor is not null ||
        format.FillColor is not null ||
        format.FillThemeColor is not null ||
        format.FontSize is not null;

    private static bool HasPointDataLabelContrastOverride(ChartPointDataLabelFormat format) =>
        format.TextColor is not null ||
        format.TextThemeColor is not null ||
        format.FillColor is not null ||
        format.FillThemeColor is not null ||
        format.FontSize is not null;

    private static bool IsSeriesDataLabelVisible(ChartModel chart, ChartSeriesDataLabelFormat format) =>
        (format.ShowValue ?? chart.ShowDataLabelValue) ||
        (format.ShowCategoryName ?? chart.ShowDataLabelCategoryName) ||
        (format.ShowSeriesName ?? chart.ShowDataLabelSeriesName) ||
        (format.ShowLegendKey ?? chart.ShowDataLabelLegendKey) ||
        (format.ShowPercentage ?? chart.ShowDataLabelPercentage) ||
        (format.ShowBubbleSize ?? chart.ShowDataLabelBubbleSize);

    private static bool IsPointDataLabelVisible(
        ChartModel chart,
        ChartPointDataLabelFormat format,
        ChartSeriesDataLabelFormat? seriesFormat) =>
        (format.ShowValue ?? seriesFormat?.ShowValue ?? chart.ShowDataLabelValue) ||
        (format.ShowCategoryName ?? seriesFormat?.ShowCategoryName ?? chart.ShowDataLabelCategoryName) ||
        (format.ShowSeriesName ?? seriesFormat?.ShowSeriesName ?? chart.ShowDataLabelSeriesName) ||
        (format.ShowLegendKey ?? seriesFormat?.ShowLegendKey ?? chart.ShowDataLabelLegendKey) ||
        (format.ShowPercentage ?? seriesFormat?.ShowPercentage ?? chart.ShowDataLabelPercentage) ||
        (format.ShowBubbleSize ?? seriesFormat?.ShowBubbleSize ?? chart.ShowDataLabelBubbleSize);

    private static void AddLowContrastChartTextIssue(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        ChartModel chart,
        string textArea,
        string? text,
        CellColor textColor,
        CellColor background,
        double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var minimumContrastRatio = MinimumTextContrastRatio(fontSize, bold: false);
        if (ContrastRatio(textColor, background) >= minimumContrastRatio)
            return;

        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.LowContrastChartText,
            sheet.Id,
            sheet.Name,
            FormatRange(chart.DataRange),
            $"{textArea} should have at least {minimumContrastRatio:0.0}:1 contrast against its background."));
    }

    private static void AddLowContrastCellTextIssues(List<AccessibilityIssue> issues, Workbook workbook, Sheet sheet)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var conditionalContrastRules = GetConditionalContrastRules(workbook, sheet, occupiedCells);
        Dictionary<StyleId, CellStyle>? workbookStyleCache = null;
        Dictionary<CellStyle, CellContrastCheck>? contrastCache = null;
        foreach (var entry in occupiedCells)
        {
            var (row, col) = entry.Key;
            var cell = entry.Value;
            if (!HasVisibleCellText(cell.Value))
                continue;

            var address = new CellAddress(sheet.Id, row, col);
            var style = GetEffectiveContrastStyle(
                workbook,
                conditionalContrastRules,
                address,
                cell,
                ref workbookStyleCache);
            var contrast = GetCellContrastCheck(style, workbook.Theme, ref contrastCache);
            if (contrast.HasSufficientContrast)
                continue;

            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.LowContrastCellText,
                sheet.Id,
                sheet.Name,
                address.ToA1(),
                $"Cell text should have at least {contrast.MinimumContrastRatio:0.0}:1 contrast against its fill."));
        }
    }

    private static void AddLowContrastTextBoxTextIssue(
        List<AccessibilityIssue> issues,
        Workbook workbook,
        Sheet sheet,
        TextBoxModel textBox)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
            return;

        var textColor = ResolveDefaultObjectTextColor(workbook.Theme);
        var background = textBox.GetEffectiveFillColor(workbook.Theme, ResolveDefaultObjectFillColor(workbook.Theme));
        var minimumContrastRatio = MinimumTextContrastRatio(DefaultObjectTextFontSize, bold: false);
        if (ContrastRatio(textColor, background) >= minimumContrastRatio)
            return;

        issues.Add(new AccessibilityIssue(
            AccessibilityIssueKind.LowContrastObjectText,
            sheet.Id,
            sheet.Name,
            textBox.Anchor.ToA1(),
            $"Text box text should have at least {minimumContrastRatio:0.0}:1 contrast against its fill."));
    }

    private static CellColor ResolveDefaultObjectTextColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Text?.TextThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Text?.TextColor ??
        CellColor.Black;

    private static CellColor ResolveDefaultObjectFillColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.FillThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.FillColor ??
        CellColor.White;

    private static ConditionalContrastRuleSet? GetConditionalContrastRules(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells)
    {
        List<ConditionalFormat>? rules = null;
        foreach (var rule in sheet.ConditionalFormats)
        {
            if (rule.FormatIfTrue is null)
                continue;

            rules ??= [];
            rules.Add(rule);
        }

        if (rules is null)
            return null;

        rules.Sort(static (left, right) => left.Priority.CompareTo(right.Priority));
        var hasSharedAppliesToRange = TryGetSharedAppliesToRange(rules, out var sharedAppliesToRange);
        return new ConditionalContrastRuleSet(
            rules,
            hasSharedAppliesToRange,
            sharedAppliesToRange,
            hasSharedAppliesToRange ? GetAlwaysTrueTextValueStyle(rules) : null,
            new ConditionalFormatEvaluationCache(workbook, sheet, occupiedCells));
    }

    private static bool TryGetSharedAppliesToRange(IReadOnlyList<ConditionalFormat> rules, out GridRange range)
    {
        range = rules[0].AppliesTo;
        for (var i = 1; i < rules.Count; i++)
        {
            if (rules[i].AppliesTo != range)
                return false;
        }

        return true;
    }

    private static CellStyle? GetAlwaysTrueTextValueStyle(IReadOnlyList<ConditionalFormat> rules)
    {
        CellStyle? style = null;
        foreach (var rule in rules)
        {
            if (!IsRuleAlwaysTrueForScannedText(rule))
                return null;

            style = rule.FormatIfTrue!;
            if (rule.StopIfTrue)
                break;
        }

        return style;
    }

    private static bool IsRuleAlwaysTrueForScannedText(ConditionalFormat rule) =>
        rule.RuleType is CfRuleType.NoBlanks or CfRuleType.NoErrors;

    private sealed record ConditionalContrastRuleSet(
        IReadOnlyList<ConditionalFormat> Rules,
        bool HasSharedAppliesToRange,
        GridRange SharedAppliesToRange,
        CellStyle? AlwaysTrueTextValueStyle,
        ConditionalFormatEvaluationCache EvaluationCache);

    private static CellStyle GetEffectiveContrastStyle(
        Workbook workbook,
        ConditionalContrastRuleSet? conditionalContrastRules,
        CellAddress address,
        Cell cell,
        ref Dictionary<StyleId, CellStyle>? workbookStyleCache)
    {
        CellStyle? style = null;
        if (conditionalContrastRules is null)
            return GetCachedWorkbookStyle(workbook, ref workbookStyleCache, cell.StyleId);

        if (conditionalContrastRules.HasSharedAppliesToRange)
        {
            if (!conditionalContrastRules.SharedAppliesToRange.Contains(address))
                return GetCachedWorkbookStyle(workbook, ref workbookStyleCache, cell.StyleId);

            if (conditionalContrastRules.AlwaysTrueTextValueStyle is { } alwaysTrueTextValueStyle)
                return alwaysTrueTextValueStyle;

            return GetEffectiveContrastStyleForApplicableRules(
                    conditionalContrastRules.Rules,
                    address,
                    cell,
                    conditionalContrastRules.EvaluationCache) ??
                GetCachedWorkbookStyle(workbook, ref workbookStyleCache, cell.StyleId);
        }

        foreach (var rule in conditionalContrastRules.Rules)
        {
            if (!rule.AppliesTo.Contains(address))
                continue;

            if (!IsConditionalFormatTrue(rule, address, cell.Value, conditionalContrastRules.EvaluationCache))
                continue;

            style = rule.FormatIfTrue!;
            if (rule.StopIfTrue)
                break;
        }

        return style ?? GetCachedWorkbookStyle(workbook, ref workbookStyleCache, cell.StyleId);
    }

    private static CellStyle? GetEffectiveContrastStyleForApplicableRules(
        IReadOnlyList<ConditionalFormat> rules,
        CellAddress address,
        Cell cell,
        ConditionalFormatEvaluationCache evaluationCache)
    {
        CellStyle? style = null;
        foreach (var rule in rules)
        {
            if (!IsConditionalFormatTrue(rule, address, cell.Value, evaluationCache))
                continue;

            style = rule.FormatIfTrue!;
            if (rule.StopIfTrue)
                break;
        }

        return style;
    }

    private static CellStyle GetCachedWorkbookStyle(
        Workbook workbook,
        ref Dictionary<StyleId, CellStyle>? styleCache,
        StyleId styleId)
    {
        styleCache ??= [];
        if (!styleCache.TryGetValue(styleId, out var style))
        {
            style = workbook.GetStyle(styleId);
            styleCache[styleId] = style;
        }

        return style;
    }

    private static CellContrastCheck GetCellContrastCheck(
        CellStyle style,
        WorkbookTheme theme,
        ref Dictionary<CellStyle, CellContrastCheck>? contrastCache)
    {
        contrastCache ??= new Dictionary<CellStyle, CellContrastCheck>(CellStyleReferenceComparer.Instance);
        if (!contrastCache.TryGetValue(style, out var check))
        {
            var minimumContrastRatio = MinimumTextContrastRatio(style);
            check = new CellContrastCheck(
                minimumContrastRatio,
                HasSufficientCellTextContrast(style, theme, minimumContrastRatio));
            contrastCache[style] = check;
        }

        return check;
    }

    private static bool IsConditionalFormatTrue(
        ConditionalFormat rule,
        CellAddress address,
        ScalarValue value,
        ConditionalFormatEvaluationCache evaluationCache) =>
        rule.RuleType switch
        {
            CfRuleType.NoBlanks => value is not BlankValue,
            CfRuleType.Blanks => value is BlankValue,
            CfRuleType.Errors => value is ErrorValue,
            CfRuleType.NoErrors => value is not ErrorValue,
            CfRuleType.DuplicateValues => evaluationCache.HasDuplicateValue(rule, value),
            CfRuleType.UniqueValues => evaluationCache.HasUniqueValue(rule, value),
            CfRuleType.AboveAverage => evaluationCache.MatchesAverageRule(rule, value),
            CfRuleType.Top10 => evaluationCache.MatchesTopBottomRule(rule, address),
            CfRuleType.ContainsText => ValueText(value).Contains(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.NotContainsText => !ValueText(value).Contains(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.BeginsWith => ValueText(value).StartsWith(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.EndsWith => ValueText(value).EndsWith(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.DateOccurring => IsDateOccurringRuleTrue(rule, value),
            CfRuleType.CellValue => IsCellValueRuleTrue(rule, value),
            CfRuleType.Formula => evaluationCache.MatchesFormulaRule(rule, address),
            _ => false
        };

    private static bool TryCreateFormulaExpression(string? formulaText, out ConditionalFormulaExpression expression)
    {
        expression = default!;
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        try
        {
            var text = formulaText.Trim();
            var formula = text[0] == '=' ? text : "=" + text;
            var ast = new Parser(new Lexer(formula).Tokenize()).Parse();
            return TryCreateFormulaExpression(ast, out expression);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateFormulaExpression(FormulaNode ast, out ConditionalFormulaExpression expression)
    {
        if (TryCreateFormulaComparison(ast, out var comparison))
        {
            expression = new ConditionalFormulaComparisonExpression(comparison);
            return true;
        }

        if (TryCreateFormulaBooleanOperandExpression(ast, out expression))
            return true;

        if (ast is not FunctionCallNode function)
            return false;

        if (TryCreateFormulaBooleanFunctionExpression(function, out expression))
            return true;

        if (string.Equals(function.FunctionName, "NOT", StringComparison.OrdinalIgnoreCase))
        {
            if (function.Arguments.Count != 1 ||
                !TryCreateFormulaExpression(function.Arguments[0], out var operand))
            {
                return false;
            }

            expression = new ConditionalFormulaLogicalExpression(
                ConditionalFormulaLogicalOperator.Not,
                [operand]);
            return true;
        }

        if (string.Equals(function.FunctionName, "IF", StringComparison.OrdinalIgnoreCase))
        {
            if (function.Arguments.Count != 3 ||
                !TryCreateFormulaExpression(function.Arguments[0], out var condition) ||
                !TryCreateFormulaExpression(function.Arguments[1], out var whenTrue) ||
                !TryCreateFormulaExpression(function.Arguments[2], out var whenFalse))
            {
                return false;
            }

            expression = new ConditionalFormulaIfExpression(condition, whenTrue, whenFalse);
            return true;
        }

        if (TryCreateFormulaPredicate(function, out var predicate))
        {
            expression = new ConditionalFormulaPredicateExpression(predicate);
            return true;
        }

        var logicalOperator = string.Equals(function.FunctionName, "AND", StringComparison.OrdinalIgnoreCase)
            ? ConditionalFormulaLogicalOperator.And
            : string.Equals(function.FunctionName, "OR", StringComparison.OrdinalIgnoreCase)
                ? ConditionalFormulaLogicalOperator.Or
                : string.Equals(function.FunctionName, "XOR", StringComparison.OrdinalIgnoreCase)
                    ? ConditionalFormulaLogicalOperator.Xor
                    : (ConditionalFormulaLogicalOperator?)null;
        if (!logicalOperator.HasValue || function.Arguments.Count == 0)
            return false;

        var operands = new ConditionalFormulaExpression[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryCreateFormulaExpression(function.Arguments[i], out operands[i]))
                return false;
        }

        expression = new ConditionalFormulaLogicalExpression(logicalOperator.Value, operands);
        return true;
    }

    private static bool TryCreateFormulaBooleanFunctionExpression(
        FunctionCallNode function,
        out ConditionalFormulaExpression expression)
    {
        expression = default!;
        if (function.Arguments.Count != 0)
            return false;

        if (string.Equals(function.FunctionName, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            expression = new ConditionalFormulaOperandExpression(LiteralFormulaOperand(new BoolValue(true)));
            return true;
        }

        if (string.Equals(function.FunctionName, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            expression = new ConditionalFormulaOperandExpression(LiteralFormulaOperand(new BoolValue(false)));
            return true;
        }

        return false;
    }

    private static bool TryCreateFormulaPredicate(FunctionCallNode function, out ConditionalFormulaPredicate predicate)
    {
        predicate = default;
        if (function.Arguments.Count != 1 ||
            !TryGetFormulaPredicateKind(function.FunctionName, out var kind) ||
            !TryCreateFormulaOperand(function.Arguments[0], out var operand))
        {
            return false;
        }

        predicate = new ConditionalFormulaPredicate(kind, operand);
        return true;
    }

    private static bool TryGetFormulaPredicateKind(
        string functionName,
        out ConditionalFormulaPredicateKind kind)
    {
        switch (functionName.ToUpperInvariant())
        {
            case "ISBLANK":
                kind = ConditionalFormulaPredicateKind.IsBlank;
                return true;
            case "ISNUMBER":
                kind = ConditionalFormulaPredicateKind.IsNumber;
                return true;
            case "ISTEXT":
                kind = ConditionalFormulaPredicateKind.IsText;
                return true;
            case "ISNONTEXT":
                kind = ConditionalFormulaPredicateKind.IsNonText;
                return true;
            case "ISLOGICAL":
                kind = ConditionalFormulaPredicateKind.IsLogical;
                return true;
            case "ISERROR":
                kind = ConditionalFormulaPredicateKind.IsError;
                return true;
            case "ISERR":
                kind = ConditionalFormulaPredicateKind.IsErr;
                return true;
            case "ISNA":
                kind = ConditionalFormulaPredicateKind.IsNa;
                return true;
            case "ISEVEN":
                kind = ConditionalFormulaPredicateKind.IsEven;
                return true;
            case "ISODD":
                kind = ConditionalFormulaPredicateKind.IsOdd;
                return true;
            case "ISREF":
                kind = ConditionalFormulaPredicateKind.IsRef;
                return true;
            case "ISFORMULA":
                kind = ConditionalFormulaPredicateKind.IsFormula;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryCreateFormulaBooleanOperandExpression(FormulaNode ast, out ConditionalFormulaExpression expression)
    {
        expression = default!;
        if (!IsFormulaPredicateOperand(ast))
            return false;

        if (!TryCreateFormulaOperand(ast, out var operand))
            return false;

        expression = new ConditionalFormulaOperandExpression(operand);
        return true;
    }

    private const ulong MaxFormulaAggregateRangeCells = 10_000;
    private const int MaxFormulaRoundDigits = 15;
    private const int MaxFormulaFactorialInput = 170;
    private const int MaxFormulaDoubleFactorialInput = 300;
    private const int MaxFormulaCombinInput = 1_000_000;
    private const int MaxFormulaCombinIterations = 10_000;
    private const int MaxFormulaCombinaCombinationInput = 1_029;
    private const int MaxFormulaPermutInput = 1_000_000;
    private const int MaxFormulaPermutIterations = 10_000;
    private const int MaxFormulaPermutationAInput = int.MaxValue;
    private const int MaxFormulaGcdArgumentCount = 255;
    private const int MaxFormulaMultinomialArgumentCount = 255;
    private const int MaxFormulaSumProductArgumentCount = 255;
    private const double MaxFormulaGcdInputExclusive = 9_223_372_036_854_775_808d;
    private const ulong MaxFormulaBitwiseInput = 281_474_976_710_655UL;
    private const int MaxFormulaBitwiseShift = 53;
    private const int MaxFormulaTextSliceLength = 32_767;
    private const double FormulaSecZeroCosineTolerance = 1E-15d;
    private static bool IsFormulaPredicateOperand(FormulaNode ast) =>
        ast is (BooleanNode
            or CellRefNode
            or NumberNode)
            || ast is UnaryOpNode unary && IsFormulaUnaryArithmeticOperator(unary.Operator)
            || ast is BinaryOpNode binary && IsFormulaArithmeticOperator(binary.Operator)
            || ast is FunctionCallNode function &&
                (IsFormulaAggregateFunction(function.FunctionName) ||
                 IsFormulaScalarFunction(function.FunctionName));

    private static bool TryCreateFormulaComparison(FormulaNode ast, out ConditionalFormulaComparison comparison)
    {
        comparison = default;
        if (ast is not BinaryOpNode binary || !IsFormulaComparisonOperator(binary.Operator))
            return false;

        if (!TryCreateFormulaOperand(binary.Left, out var left) ||
            !TryCreateFormulaOperand(binary.Right, out var right))
        {
            return false;
        }

        comparison = new ConditionalFormulaComparison(left, binary.Operator, right);
        return true;
    }

    private static bool IsFormulaComparisonOperator(BinaryOperator op) =>
        op is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.GreaterThan
            or BinaryOperator.LessOrEqual
            or BinaryOperator.GreaterOrEqual;

    private static bool TryCreateFormulaOperand(FormulaNode node, out ConditionalFormulaOperand operand)
    {
        operand = default;
        switch (node)
        {
            case CellRefNode cell:
                operand = new ConditionalFormulaOperand(
                    ConditionalFormulaOperandKind.Reference,
                    null,
                    cell.Row,
                    cell.ColumnNumber,
                    cell.IsRowAbsolute,
                    cell.IsColAbsolute,
                    cell.SheetName);
                return true;
            case RangeRefNode range:
                operand = FormulaReferenceRangeOperand(
                    range.Start.Row,
                    range.Start.ColumnNumber,
                    range.Start.IsRowAbsolute,
                    range.Start.IsColAbsolute,
                    range.End.Row,
                    range.End.ColumnNumber,
                    range.End.IsRowAbsolute,
                    range.End.IsColAbsolute,
                    range.SheetName ?? range.Start.SheetName);
                return true;
            case FullColumnRangeRefNode range:
                operand = FormulaReferenceRangeOperand(
                    1,
                    CellAddress.ColumnNameToNumber(range.StartColumnName),
                    true,
                    range.IsStartAbsolute,
                    CellAddress.MaxRow,
                    CellAddress.ColumnNameToNumber(range.EndColumnName),
                    true,
                    range.IsEndAbsolute,
                    range.SheetName);
                return true;
            case FullRowRangeRefNode range:
                operand = FormulaReferenceRangeOperand(
                    range.StartRow,
                    1,
                    range.IsStartAbsolute,
                    true,
                    range.EndRow,
                    CellAddress.MaxCol,
                    range.IsEndAbsolute,
                    true,
                    range.SheetName);
                return true;
            case NumberNode number:
                operand = LiteralFormulaOperand(new NumberValue(number.Value));
                return true;
            case StringNode text:
                operand = LiteralFormulaOperand(new TextValue(text.Value));
                return true;
            case BooleanNode boolean:
                operand = LiteralFormulaOperand(new BoolValue(boolean.Value));
                return true;
            case ErrorNode error:
                operand = LiteralFormulaOperand(error.Error);
                return true;
            case UnaryOpNode { Operator: UnaryOperator.Negate, Operand: NumberNode number }:
                operand = LiteralFormulaOperand(new NumberValue(-number.Value));
                return true;
            case UnaryOpNode { Operator: UnaryOperator.Percent, Operand: NumberNode number }:
                operand = LiteralFormulaOperand(new NumberValue(number.Value / 100d));
                return true;
            case UnaryOpNode unary when TryCreateFormulaUnaryOperand(unary, out operand):
                return true;
            case BinaryOpNode binary when TryCreateFormulaArithmeticOperand(binary, out operand):
                return true;
            case FunctionCallNode function when TryCreateFormulaScalarFunctionOperand(function, out operand):
                return true;
            case FunctionCallNode function when TryCreateFormulaAggregateOperand(function, out operand):
                return true;
            default:
                return false;
        }
    }

    private static ConditionalFormulaOperand LiteralFormulaOperand(ScalarValue value) =>
        new(ConditionalFormulaOperandKind.Literal, value, 0, 0, true, true, null);

    private static ConditionalFormulaOperand FormulaReferenceRangeOperand(
        uint row,
        uint col,
        bool isRowAbsolute,
        bool isColAbsolute,
        uint endRow,
        uint endCol,
        bool isEndRowAbsolute,
        bool isEndColAbsolute,
        string? sheetName) =>
        new(
            ConditionalFormulaOperandKind.ReferenceRange,
            null,
            row,
            col,
            isRowAbsolute,
            isColAbsolute,
            sheetName,
            default,
            null,
            null,
            null,
            null,
            new ConditionalFormulaReferenceRange(endRow, endCol, isEndRowAbsolute, isEndColAbsolute));

    private static bool TryCreateFormulaUnaryOperand(
        UnaryOpNode unary,
        out ConditionalFormulaOperand operand)
    {
        operand = default;
        if (!IsFormulaUnaryArithmeticOperator(unary.Operator) ||
            !TryCreateFormulaOperand(unary.Operand, out var inner))
        {
            return false;
        }

        operand = new ConditionalFormulaOperand(
            ConditionalFormulaOperandKind.Unary,
            null,
            0,
            0,
            true,
            true,
            null,
            default,
            null,
            null,
            new ConditionalFormulaUnary(unary.Operator, inner));
        return true;
    }

    private static bool IsFormulaUnaryArithmeticOperator(UnaryOperator op) =>
        op is UnaryOperator.Negate or UnaryOperator.Percent;

    private static bool TryCreateFormulaArithmeticOperand(
        BinaryOpNode binary,
        out ConditionalFormulaOperand operand)
    {
        operand = default;
        if (!IsFormulaArithmeticOperator(binary.Operator) ||
            !TryCreateFormulaOperand(binary.Left, out var left) ||
            !TryCreateFormulaOperand(binary.Right, out var right))
        {
            return false;
        }

        operand = new ConditionalFormulaOperand(
            ConditionalFormulaOperandKind.Arithmetic,
            null,
            0,
            0,
            true,
            true,
            null,
            default,
            null,
            new ConditionalFormulaArithmetic(binary.Operator, left, right));
        return true;
    }

    private static bool IsFormulaArithmeticOperator(BinaryOperator op) =>
        op is BinaryOperator.Add
            or BinaryOperator.Subtract
            or BinaryOperator.Multiply
            or BinaryOperator.Divide
            or BinaryOperator.Power;

    private static bool TryCreateFormulaScalarFunctionOperand(
        FunctionCallNode function,
        out ConditionalFormulaOperand operand)
    {
        operand = default;
        if (!TryGetFormulaScalarFunctionKind(function.FunctionName, out var kind) ||
            !FormulaScalarFunctionArityMatches(kind, function.Arguments.Count))
        {
            return false;
        }

        var arguments = new ConditionalFormulaOperand[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryCreateFormulaOperand(function.Arguments[i], out arguments[i]))
                return false;
        }

        operand = new ConditionalFormulaOperand(
            ConditionalFormulaOperandKind.ScalarFunction,
            null,
            0,
            0,
            true,
            true,
            null,
            default,
            null,
            null,
            null,
            new ConditionalFormulaScalarFunction(kind, arguments));
        return true;
    }

    private static bool IsFormulaScalarFunction(string functionName) =>
        TryGetFormulaScalarFunctionKind(functionName, out _);

    private static bool TryGetFormulaScalarFunctionKind(
        string functionName,
        out ConditionalFormulaScalarFunctionKind kind)
    {
        switch (functionName.ToUpperInvariant())
        {
            case "ABS":
                kind = ConditionalFormulaScalarFunctionKind.Abs;
                return true;
            case "INT":
                kind = ConditionalFormulaScalarFunctionKind.Int;
                return true;
            case "EVEN":
                kind = ConditionalFormulaScalarFunctionKind.Even;
                return true;
            case "ODD":
                kind = ConditionalFormulaScalarFunctionKind.Odd;
                return true;
            case "ROUND":
                kind = ConditionalFormulaScalarFunctionKind.Round;
                return true;
            case "ROUNDUP":
                kind = ConditionalFormulaScalarFunctionKind.RoundUp;
                return true;
            case "ROUNDDOWN":
                kind = ConditionalFormulaScalarFunctionKind.RoundDown;
                return true;
            case "MROUND":
                kind = ConditionalFormulaScalarFunctionKind.MRound;
                return true;
            case "CEILING":
                kind = ConditionalFormulaScalarFunctionKind.Ceiling;
                return true;
            case "CEILING.MATH":
                kind = ConditionalFormulaScalarFunctionKind.CeilingMath;
                return true;
            case "CEILING.PRECISE":
            case "ISO.CEILING":
                kind = ConditionalFormulaScalarFunctionKind.IsoCeiling;
                return true;
            case "FLOOR":
                kind = ConditionalFormulaScalarFunctionKind.Floor;
                return true;
            case "FLOOR.MATH":
                kind = ConditionalFormulaScalarFunctionKind.FloorMath;
                return true;
            case "FLOOR.PRECISE":
                kind = ConditionalFormulaScalarFunctionKind.FloorPrecise;
                return true;
            case "TRUNC":
                kind = ConditionalFormulaScalarFunctionKind.Trunc;
                return true;
            case "FACT":
                kind = ConditionalFormulaScalarFunctionKind.Fact;
                return true;
            case "FACTDOUBLE":
                kind = ConditionalFormulaScalarFunctionKind.FactDouble;
                return true;
            case "MOD":
                kind = ConditionalFormulaScalarFunctionKind.Mod;
                return true;
            case "QUOTIENT":
                kind = ConditionalFormulaScalarFunctionKind.Quotient;
                return true;
            case "COMBIN":
                kind = ConditionalFormulaScalarFunctionKind.Combin;
                return true;
            case "COMBINA":
                kind = ConditionalFormulaScalarFunctionKind.Combina;
                return true;
            case "PERMUT":
                kind = ConditionalFormulaScalarFunctionKind.Permut;
                return true;
            case "PERMUTATIONA":
                kind = ConditionalFormulaScalarFunctionKind.PermutationA;
                return true;
            case "MULTINOMIAL":
                kind = ConditionalFormulaScalarFunctionKind.Multinomial;
                return true;
            case "GCD":
                kind = ConditionalFormulaScalarFunctionKind.Gcd;
                return true;
            case "LCM":
                kind = ConditionalFormulaScalarFunctionKind.Lcm;
                return true;
            case "SQRT":
                kind = ConditionalFormulaScalarFunctionKind.Sqrt;
                return true;
            case "SQRTPI":
                kind = ConditionalFormulaScalarFunctionKind.SqrtPi;
                return true;
            case "SIGN":
                kind = ConditionalFormulaScalarFunctionKind.Sign;
                return true;
            case "POWER":
                kind = ConditionalFormulaScalarFunctionKind.Power;
                return true;
            case "EXP":
                kind = ConditionalFormulaScalarFunctionKind.Exp;
                return true;
            case "LN":
                kind = ConditionalFormulaScalarFunctionKind.Ln;
                return true;
            case "LOG10":
                kind = ConditionalFormulaScalarFunctionKind.Log10;
                return true;
            case "LOG":
                kind = ConditionalFormulaScalarFunctionKind.Log;
                return true;
            case "DEGREES":
                kind = ConditionalFormulaScalarFunctionKind.Degrees;
                return true;
            case "RADIANS":
                kind = ConditionalFormulaScalarFunctionKind.Radians;
                return true;
            case "SIN":
                kind = ConditionalFormulaScalarFunctionKind.Sin;
                return true;
            case "CSC":
                kind = ConditionalFormulaScalarFunctionKind.Csc;
                return true;
            case "CSCH":
                kind = ConditionalFormulaScalarFunctionKind.Csch;
                return true;
            case "SINH":
                kind = ConditionalFormulaScalarFunctionKind.Sinh;
                return true;
            case "ASINH":
                kind = ConditionalFormulaScalarFunctionKind.Asinh;
                return true;
            case "ACOSH":
                kind = ConditionalFormulaScalarFunctionKind.Acosh;
                return true;
            case "COSH":
                kind = ConditionalFormulaScalarFunctionKind.Cosh;
                return true;
            case "SECH":
                kind = ConditionalFormulaScalarFunctionKind.Sech;
                return true;
            case "TANH":
                kind = ConditionalFormulaScalarFunctionKind.Tanh;
                return true;
            case "ATANH":
                kind = ConditionalFormulaScalarFunctionKind.Atanh;
                return true;
            case "ACOTH":
                kind = ConditionalFormulaScalarFunctionKind.Acoth;
                return true;
            case "COTH":
                kind = ConditionalFormulaScalarFunctionKind.Coth;
                return true;
            case "ASIN":
                kind = ConditionalFormulaScalarFunctionKind.Asin;
                return true;
            case "ACOS":
                kind = ConditionalFormulaScalarFunctionKind.Acos;
                return true;
            case "ACOT":
                kind = ConditionalFormulaScalarFunctionKind.Acot;
                return true;
            case "ATAN":
                kind = ConditionalFormulaScalarFunctionKind.Atan;
                return true;
            case "ATAN2":
                kind = ConditionalFormulaScalarFunctionKind.Atan2;
                return true;
            case "COS":
                kind = ConditionalFormulaScalarFunctionKind.Cos;
                return true;
            case "SEC":
                kind = ConditionalFormulaScalarFunctionKind.Sec;
                return true;
            case "COT":
                kind = ConditionalFormulaScalarFunctionKind.Cot;
                return true;
            case "TAN":
                kind = ConditionalFormulaScalarFunctionKind.Tan;
                return true;
            case "PI":
                kind = ConditionalFormulaScalarFunctionKind.Pi;
                return true;
            case "ARABIC":
                kind = ConditionalFormulaScalarFunctionKind.Arabic;
                return true;
            case "ROMAN":
                kind = ConditionalFormulaScalarFunctionKind.Roman;
                return true;
            case "UNICHAR":
                kind = ConditionalFormulaScalarFunctionKind.Unichar;
                return true;
            case "UNICODE":
                kind = ConditionalFormulaScalarFunctionKind.Unicode;
                return true;
            case "CHAR":
                kind = ConditionalFormulaScalarFunctionKind.Char;
                return true;
            case "CODE":
                kind = ConditionalFormulaScalarFunctionKind.Code;
                return true;
            case "PROPER":
                kind = ConditionalFormulaScalarFunctionKind.Proper;
                return true;
            case "REPT":
                kind = ConditionalFormulaScalarFunctionKind.Rept;
                return true;
            case "CLEAN":
                kind = ConditionalFormulaScalarFunctionKind.Clean;
                return true;
            case "T":
                kind = ConditionalFormulaScalarFunctionKind.T;
                return true;
            case "VALUE":
                kind = ConditionalFormulaScalarFunctionKind.Value;
                return true;
            case "NUMBERVALUE":
                kind = ConditionalFormulaScalarFunctionKind.NumberValue;
                return true;
            case "LEN":
                kind = ConditionalFormulaScalarFunctionKind.Len;
                return true;
            case "LENB":
                kind = ConditionalFormulaScalarFunctionKind.LenB;
                return true;
            case "UPPER":
                kind = ConditionalFormulaScalarFunctionKind.Upper;
                return true;
            case "LOWER":
                kind = ConditionalFormulaScalarFunctionKind.Lower;
                return true;
            case "TRIM":
                kind = ConditionalFormulaScalarFunctionKind.Trim;
                return true;
            case "CONCAT":
                kind = ConditionalFormulaScalarFunctionKind.Concat;
                return true;
            case "CONCATENATE":
                kind = ConditionalFormulaScalarFunctionKind.Concatenate;
                return true;
            case "TEXTJOIN":
                kind = ConditionalFormulaScalarFunctionKind.TextJoin;
                return true;
            case "SUBSTITUTE":
                kind = ConditionalFormulaScalarFunctionKind.Substitute;
                return true;
            case "REPLACE":
                kind = ConditionalFormulaScalarFunctionKind.Replace;
                return true;
            case "REPLACEB":
                kind = ConditionalFormulaScalarFunctionKind.ReplaceB;
                return true;
            case "LEFT":
                kind = ConditionalFormulaScalarFunctionKind.Left;
                return true;
            case "RIGHT":
                kind = ConditionalFormulaScalarFunctionKind.Right;
                return true;
            case "LEFTB":
                kind = ConditionalFormulaScalarFunctionKind.LeftB;
                return true;
            case "RIGHTB":
                kind = ConditionalFormulaScalarFunctionKind.RightB;
                return true;
            case "MID":
                kind = ConditionalFormulaScalarFunctionKind.Mid;
                return true;
            case "MIDB":
                kind = ConditionalFormulaScalarFunctionKind.MidB;
                return true;
            case "FIND":
                kind = ConditionalFormulaScalarFunctionKind.Find;
                return true;
            case "SEARCH":
                kind = ConditionalFormulaScalarFunctionKind.Search;
                return true;
            case "FINDB":
                kind = ConditionalFormulaScalarFunctionKind.FindB;
                return true;
            case "SEARCHB":
                kind = ConditionalFormulaScalarFunctionKind.SearchB;
                return true;
            case "EXACT":
                kind = ConditionalFormulaScalarFunctionKind.Exact;
                return true;
            case "DATE":
                kind = ConditionalFormulaScalarFunctionKind.Date;
                return true;
            case "DATEVALUE":
                kind = ConditionalFormulaScalarFunctionKind.DateValue;
                return true;
            case "TIME":
                kind = ConditionalFormulaScalarFunctionKind.Time;
                return true;
            case "TIMEVALUE":
                kind = ConditionalFormulaScalarFunctionKind.TimeValue;
                return true;
            case "YEAR":
                kind = ConditionalFormulaScalarFunctionKind.Year;
                return true;
            case "MONTH":
                kind = ConditionalFormulaScalarFunctionKind.Month;
                return true;
            case "DAY":
                kind = ConditionalFormulaScalarFunctionKind.Day;
                return true;
            case "HOUR":
                kind = ConditionalFormulaScalarFunctionKind.Hour;
                return true;
            case "MINUTE":
                kind = ConditionalFormulaScalarFunctionKind.Minute;
                return true;
            case "SECOND":
                kind = ConditionalFormulaScalarFunctionKind.Second;
                return true;
            case "TODAY":
                kind = ConditionalFormulaScalarFunctionKind.Today;
                return true;
            case "NOW":
                kind = ConditionalFormulaScalarFunctionKind.Now;
                return true;
            case "WEEKDAY":
                kind = ConditionalFormulaScalarFunctionKind.Weekday;
                return true;
            case "WEEKNUM":
                kind = ConditionalFormulaScalarFunctionKind.Weeknum;
                return true;
            case "ISOWEEKNUM":
                kind = ConditionalFormulaScalarFunctionKind.IsoWeeknum;
                return true;
            case "EDATE":
                kind = ConditionalFormulaScalarFunctionKind.EDate;
                return true;
            case "EOMONTH":
                kind = ConditionalFormulaScalarFunctionKind.EOMonth;
                return true;
            case "DAYS":
                kind = ConditionalFormulaScalarFunctionKind.Days;
                return true;
            case "DATEDIF":
                kind = ConditionalFormulaScalarFunctionKind.Datedif;
                return true;
            case "DAYS360":
                kind = ConditionalFormulaScalarFunctionKind.Days360;
                return true;
            case "YEARFRAC":
                kind = ConditionalFormulaScalarFunctionKind.Yearfrac;
                return true;
            case "WORKDAY":
                kind = ConditionalFormulaScalarFunctionKind.Workday;
                return true;
            case "WORKDAY.INTL":
                kind = ConditionalFormulaScalarFunctionKind.WorkdayIntl;
                return true;
            case "NETWORKDAYS":
                kind = ConditionalFormulaScalarFunctionKind.Networkdays;
                return true;
            case "NETWORKDAYS.INTL":
                kind = ConditionalFormulaScalarFunctionKind.NetworkdaysIntl;
                return true;
            case "N":
                kind = ConditionalFormulaScalarFunctionKind.N;
                return true;
            case "TYPE":
                kind = ConditionalFormulaScalarFunctionKind.Type;
                return true;
            case "ERROR.TYPE":
                kind = ConditionalFormulaScalarFunctionKind.ErrorType;
                return true;
            case "NA":
                kind = ConditionalFormulaScalarFunctionKind.Na;
                return true;
            case "ROW":
                kind = ConditionalFormulaScalarFunctionKind.Row;
                return true;
            case "COLUMN":
                kind = ConditionalFormulaScalarFunctionKind.Column;
                return true;
            case "ROWS":
                kind = ConditionalFormulaScalarFunctionKind.Rows;
                return true;
            case "COLUMNS":
                kind = ConditionalFormulaScalarFunctionKind.Columns;
                return true;
            case "AREAS":
                kind = ConditionalFormulaScalarFunctionKind.Areas;
                return true;
            case "BIN2DEC":
                kind = ConditionalFormulaScalarFunctionKind.Bin2Dec;
                return true;
            case "BIN2HEX":
                kind = ConditionalFormulaScalarFunctionKind.Bin2Hex;
                return true;
            case "BIN2OCT":
                kind = ConditionalFormulaScalarFunctionKind.Bin2Oct;
                return true;
            case "HEX2BIN":
                kind = ConditionalFormulaScalarFunctionKind.Hex2Bin;
                return true;
            case "HEX2DEC":
                kind = ConditionalFormulaScalarFunctionKind.Hex2Dec;
                return true;
            case "HEX2OCT":
                kind = ConditionalFormulaScalarFunctionKind.Hex2Oct;
                return true;
            case "OCT2BIN":
                kind = ConditionalFormulaScalarFunctionKind.Oct2Bin;
                return true;
            case "OCT2DEC":
                kind = ConditionalFormulaScalarFunctionKind.Oct2Dec;
                return true;
            case "OCT2HEX":
                kind = ConditionalFormulaScalarFunctionKind.Oct2Hex;
                return true;
            case "DEC2BIN":
                kind = ConditionalFormulaScalarFunctionKind.Dec2Bin;
                return true;
            case "DEC2HEX":
                kind = ConditionalFormulaScalarFunctionKind.Dec2Hex;
                return true;
            case "DEC2OCT":
                kind = ConditionalFormulaScalarFunctionKind.Dec2Oct;
                return true;
            case "BASE":
                kind = ConditionalFormulaScalarFunctionKind.Base;
                return true;
            case "DECIMAL":
                kind = ConditionalFormulaScalarFunctionKind.Decimal;
                return true;
            case "CONVERT":
                kind = ConditionalFormulaScalarFunctionKind.Convert;
                return true;
            case "COMPLEX":
                kind = ConditionalFormulaScalarFunctionKind.Complex;
                return true;
            case "IMREAL":
                kind = ConditionalFormulaScalarFunctionKind.ImReal;
                return true;
            case "IMAGINARY":
                kind = ConditionalFormulaScalarFunctionKind.Imaginary;
                return true;
            case "IMABS":
                kind = ConditionalFormulaScalarFunctionKind.ImAbs;
                return true;
            case "IMARGUMENT":
                kind = ConditionalFormulaScalarFunctionKind.ImArgument;
                return true;
            case "IMCONJUGATE":
                kind = ConditionalFormulaScalarFunctionKind.ImConjugate;
                return true;
            case "IMCOS":
                kind = ConditionalFormulaScalarFunctionKind.ImCos;
                return true;
            case "IMCOSH":
                kind = ConditionalFormulaScalarFunctionKind.ImCosh;
                return true;
            case "IMCOT":
                kind = ConditionalFormulaScalarFunctionKind.ImCot;
                return true;
            case "IMCSC":
                kind = ConditionalFormulaScalarFunctionKind.ImCsc;
                return true;
            case "IMCSCH":
                kind = ConditionalFormulaScalarFunctionKind.ImCsch;
                return true;
            case "IMDIV":
                kind = ConditionalFormulaScalarFunctionKind.ImDiv;
                return true;
            case "IMEXP":
                kind = ConditionalFormulaScalarFunctionKind.ImExp;
                return true;
            case "IMLN":
                kind = ConditionalFormulaScalarFunctionKind.ImLn;
                return true;
            case "IMLOG10":
                kind = ConditionalFormulaScalarFunctionKind.ImLog10;
                return true;
            case "IMLOG2":
                kind = ConditionalFormulaScalarFunctionKind.ImLog2;
                return true;
            case "IMPOWER":
                kind = ConditionalFormulaScalarFunctionKind.ImPower;
                return true;
            case "IMPRODUCT":
                kind = ConditionalFormulaScalarFunctionKind.ImProduct;
                return true;
            case "IMSIN":
                kind = ConditionalFormulaScalarFunctionKind.ImSin;
                return true;
            case "IMSINH":
                kind = ConditionalFormulaScalarFunctionKind.ImSinh;
                return true;
            case "IMSEC":
                kind = ConditionalFormulaScalarFunctionKind.ImSec;
                return true;
            case "IMSECH":
                kind = ConditionalFormulaScalarFunctionKind.ImSech;
                return true;
            case "IMTAN":
                kind = ConditionalFormulaScalarFunctionKind.ImTan;
                return true;
            case "IMSQRT":
                kind = ConditionalFormulaScalarFunctionKind.ImSqrt;
                return true;
            case "IMSUB":
                kind = ConditionalFormulaScalarFunctionKind.ImSub;
                return true;
            case "IMSUM":
                kind = ConditionalFormulaScalarFunctionKind.ImSum;
                return true;
            case "DELTA":
                kind = ConditionalFormulaScalarFunctionKind.Delta;
                return true;
            case "ERF":
                kind = ConditionalFormulaScalarFunctionKind.Erf;
                return true;
            case "ERF.PRECISE":
                kind = ConditionalFormulaScalarFunctionKind.ErfPrecise;
                return true;
            case "ERFC":
                kind = ConditionalFormulaScalarFunctionKind.Erfc;
                return true;
            case "ERFC.PRECISE":
                kind = ConditionalFormulaScalarFunctionKind.ErfcPrecise;
                return true;
            case "GESTEP":
                kind = ConditionalFormulaScalarFunctionKind.GeStep;
                return true;
            case "BITAND":
                kind = ConditionalFormulaScalarFunctionKind.BitAnd;
                return true;
            case "BITOR":
                kind = ConditionalFormulaScalarFunctionKind.BitOr;
                return true;
            case "BITXOR":
                kind = ConditionalFormulaScalarFunctionKind.BitXor;
                return true;
            case "BITLSHIFT":
                kind = ConditionalFormulaScalarFunctionKind.BitLShift;
                return true;
            case "BITRSHIFT":
                kind = ConditionalFormulaScalarFunctionKind.BitRShift;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool FormulaScalarFunctionArityMatches(
        ConditionalFormulaScalarFunctionKind kind,
        int argumentCount) =>
        kind switch
        {
            ConditionalFormulaScalarFunctionKind.Abs or
            ConditionalFormulaScalarFunctionKind.Int or
            ConditionalFormulaScalarFunctionKind.Even or
            ConditionalFormulaScalarFunctionKind.Odd or
            ConditionalFormulaScalarFunctionKind.Fact or
            ConditionalFormulaScalarFunctionKind.FactDouble or
            ConditionalFormulaScalarFunctionKind.Sqrt or
            ConditionalFormulaScalarFunctionKind.SqrtPi or
            ConditionalFormulaScalarFunctionKind.Sign or
            ConditionalFormulaScalarFunctionKind.Exp or
            ConditionalFormulaScalarFunctionKind.Ln or
            ConditionalFormulaScalarFunctionKind.Log10 or
            ConditionalFormulaScalarFunctionKind.Degrees or
            ConditionalFormulaScalarFunctionKind.Radians or
            ConditionalFormulaScalarFunctionKind.Sin or
            ConditionalFormulaScalarFunctionKind.Csc or
            ConditionalFormulaScalarFunctionKind.Csch or
            ConditionalFormulaScalarFunctionKind.Sinh or
            ConditionalFormulaScalarFunctionKind.Asinh or
            ConditionalFormulaScalarFunctionKind.Acosh or
            ConditionalFormulaScalarFunctionKind.Cosh or
            ConditionalFormulaScalarFunctionKind.Sech or
            ConditionalFormulaScalarFunctionKind.Tanh or
            ConditionalFormulaScalarFunctionKind.Atanh or
            ConditionalFormulaScalarFunctionKind.Acoth or
            ConditionalFormulaScalarFunctionKind.Coth or
            ConditionalFormulaScalarFunctionKind.Asin or
            ConditionalFormulaScalarFunctionKind.Acos or
            ConditionalFormulaScalarFunctionKind.Acot or
            ConditionalFormulaScalarFunctionKind.Atan or
            ConditionalFormulaScalarFunctionKind.Cos or
            ConditionalFormulaScalarFunctionKind.Sec or
            ConditionalFormulaScalarFunctionKind.Cot or
            ConditionalFormulaScalarFunctionKind.Tan or
            ConditionalFormulaScalarFunctionKind.Arabic or
            ConditionalFormulaScalarFunctionKind.ErfPrecise or
            ConditionalFormulaScalarFunctionKind.Erfc or
            ConditionalFormulaScalarFunctionKind.ErfcPrecise or
            ConditionalFormulaScalarFunctionKind.Unichar or
            ConditionalFormulaScalarFunctionKind.Unicode or
            ConditionalFormulaScalarFunctionKind.Char or
            ConditionalFormulaScalarFunctionKind.Code or
            ConditionalFormulaScalarFunctionKind.Proper or
            ConditionalFormulaScalarFunctionKind.Clean or
            ConditionalFormulaScalarFunctionKind.T or
            ConditionalFormulaScalarFunctionKind.Value or
            ConditionalFormulaScalarFunctionKind.Len or
            ConditionalFormulaScalarFunctionKind.LenB or
            ConditionalFormulaScalarFunctionKind.Upper or
            ConditionalFormulaScalarFunctionKind.Lower or
            ConditionalFormulaScalarFunctionKind.Trim or
            ConditionalFormulaScalarFunctionKind.DateValue or
            ConditionalFormulaScalarFunctionKind.TimeValue or
            ConditionalFormulaScalarFunctionKind.Year or
            ConditionalFormulaScalarFunctionKind.Month or
            ConditionalFormulaScalarFunctionKind.Day or
            ConditionalFormulaScalarFunctionKind.Hour or
            ConditionalFormulaScalarFunctionKind.Minute or
            ConditionalFormulaScalarFunctionKind.Second or
            ConditionalFormulaScalarFunctionKind.IsoWeeknum or
            ConditionalFormulaScalarFunctionKind.Bin2Dec or
            ConditionalFormulaScalarFunctionKind.Hex2Dec or
            ConditionalFormulaScalarFunctionKind.Oct2Dec or
            ConditionalFormulaScalarFunctionKind.ImReal or
            ConditionalFormulaScalarFunctionKind.Imaginary or
            ConditionalFormulaScalarFunctionKind.ImAbs or
            ConditionalFormulaScalarFunctionKind.ImArgument or
            ConditionalFormulaScalarFunctionKind.ImConjugate or
            ConditionalFormulaScalarFunctionKind.ImCos or
            ConditionalFormulaScalarFunctionKind.ImCosh or
            ConditionalFormulaScalarFunctionKind.ImCot or
            ConditionalFormulaScalarFunctionKind.ImCsc or
            ConditionalFormulaScalarFunctionKind.ImCsch or
            ConditionalFormulaScalarFunctionKind.ImExp or
            ConditionalFormulaScalarFunctionKind.ImLn or
            ConditionalFormulaScalarFunctionKind.ImLog10 or
            ConditionalFormulaScalarFunctionKind.ImLog2 or
            ConditionalFormulaScalarFunctionKind.ImSin or
            ConditionalFormulaScalarFunctionKind.ImSinh or
            ConditionalFormulaScalarFunctionKind.ImSec or
            ConditionalFormulaScalarFunctionKind.ImSech or
            ConditionalFormulaScalarFunctionKind.ImSqrt or
            ConditionalFormulaScalarFunctionKind.ImTan => argumentCount == 1,
            ConditionalFormulaScalarFunctionKind.ImSum or
            ConditionalFormulaScalarFunctionKind.ImProduct => argumentCount is >= 1 and <= 255,
            ConditionalFormulaScalarFunctionKind.Concat or
            ConditionalFormulaScalarFunctionKind.Concatenate => argumentCount is >= 1 and <= 255,
            ConditionalFormulaScalarFunctionKind.TextJoin => argumentCount is >= 3 and <= 255,
            ConditionalFormulaScalarFunctionKind.NumberValue => argumentCount is >= 1 and <= 3,
            ConditionalFormulaScalarFunctionKind.Log or
            ConditionalFormulaScalarFunctionKind.Roman or
            ConditionalFormulaScalarFunctionKind.IsoCeiling or
            ConditionalFormulaScalarFunctionKind.FloorPrecise or
            ConditionalFormulaScalarFunctionKind.Trunc or
            ConditionalFormulaScalarFunctionKind.Erf or
            ConditionalFormulaScalarFunctionKind.Delta or
            ConditionalFormulaScalarFunctionKind.GeStep or
            ConditionalFormulaScalarFunctionKind.Weekday or
            ConditionalFormulaScalarFunctionKind.Bin2Hex or
            ConditionalFormulaScalarFunctionKind.Bin2Oct or
            ConditionalFormulaScalarFunctionKind.Hex2Bin or
            ConditionalFormulaScalarFunctionKind.Hex2Oct or
            ConditionalFormulaScalarFunctionKind.Oct2Bin or
            ConditionalFormulaScalarFunctionKind.Oct2Hex or
            ConditionalFormulaScalarFunctionKind.Dec2Bin or
            ConditionalFormulaScalarFunctionKind.Dec2Hex or
            ConditionalFormulaScalarFunctionKind.Dec2Oct or
            ConditionalFormulaScalarFunctionKind.Weeknum => argumentCount is 1 or 2,
            ConditionalFormulaScalarFunctionKind.CeilingMath or
            ConditionalFormulaScalarFunctionKind.FloorMath => argumentCount is >= 1 and <= 3,
            ConditionalFormulaScalarFunctionKind.Round or
            ConditionalFormulaScalarFunctionKind.RoundUp or
            ConditionalFormulaScalarFunctionKind.RoundDown or
            ConditionalFormulaScalarFunctionKind.MRound or
            ConditionalFormulaScalarFunctionKind.Ceiling or
            ConditionalFormulaScalarFunctionKind.Floor or
            ConditionalFormulaScalarFunctionKind.Mod or
            ConditionalFormulaScalarFunctionKind.Quotient or
            ConditionalFormulaScalarFunctionKind.Combin or
            ConditionalFormulaScalarFunctionKind.Combina or
            ConditionalFormulaScalarFunctionKind.Permut or
            ConditionalFormulaScalarFunctionKind.PermutationA or
            ConditionalFormulaScalarFunctionKind.Power or
            ConditionalFormulaScalarFunctionKind.Atan2 or
            ConditionalFormulaScalarFunctionKind.Left or
            ConditionalFormulaScalarFunctionKind.Right or
            ConditionalFormulaScalarFunctionKind.Exact or
            ConditionalFormulaScalarFunctionKind.BitAnd or
            ConditionalFormulaScalarFunctionKind.BitOr or
            ConditionalFormulaScalarFunctionKind.BitXor or
            ConditionalFormulaScalarFunctionKind.BitLShift or
            ConditionalFormulaScalarFunctionKind.BitRShift or
            ConditionalFormulaScalarFunctionKind.ImSub or
            ConditionalFormulaScalarFunctionKind.ImDiv or
            ConditionalFormulaScalarFunctionKind.ImPower or
            ConditionalFormulaScalarFunctionKind.Rept or
            ConditionalFormulaScalarFunctionKind.EDate or
            ConditionalFormulaScalarFunctionKind.EOMonth or
            ConditionalFormulaScalarFunctionKind.Days or
            ConditionalFormulaScalarFunctionKind.Decimal => argumentCount == 2,
            ConditionalFormulaScalarFunctionKind.LeftB or
            ConditionalFormulaScalarFunctionKind.RightB => argumentCount is 1 or 2,
            ConditionalFormulaScalarFunctionKind.Substitute => argumentCount is 3 or 4,
            ConditionalFormulaScalarFunctionKind.Replace or
            ConditionalFormulaScalarFunctionKind.ReplaceB => argumentCount == 4,
            ConditionalFormulaScalarFunctionKind.Base or
            ConditionalFormulaScalarFunctionKind.Days360 or
            ConditionalFormulaScalarFunctionKind.Workday or
            ConditionalFormulaScalarFunctionKind.Networkdays or
            ConditionalFormulaScalarFunctionKind.Complex or
            ConditionalFormulaScalarFunctionKind.Yearfrac => argumentCount is 2 or 3,
            ConditionalFormulaScalarFunctionKind.WorkdayIntl or
            ConditionalFormulaScalarFunctionKind.NetworkdaysIntl => argumentCount is >= 2 and <= 4,
            ConditionalFormulaScalarFunctionKind.N or
            ConditionalFormulaScalarFunctionKind.Type or
            ConditionalFormulaScalarFunctionKind.ErrorType => argumentCount == 1,
            ConditionalFormulaScalarFunctionKind.Find or
            ConditionalFormulaScalarFunctionKind.Search or
            ConditionalFormulaScalarFunctionKind.FindB or
            ConditionalFormulaScalarFunctionKind.SearchB => argumentCount is 2 or 3,
            ConditionalFormulaScalarFunctionKind.Mid or
            ConditionalFormulaScalarFunctionKind.MidB or
            ConditionalFormulaScalarFunctionKind.Date or
            ConditionalFormulaScalarFunctionKind.Time or
            ConditionalFormulaScalarFunctionKind.Datedif or
            ConditionalFormulaScalarFunctionKind.Convert => argumentCount == 3,
            ConditionalFormulaScalarFunctionKind.Multinomial => argumentCount is >= 1 and <= MaxFormulaMultinomialArgumentCount,
            ConditionalFormulaScalarFunctionKind.Gcd or
            ConditionalFormulaScalarFunctionKind.Lcm => argumentCount is >= 1 and <= MaxFormulaGcdArgumentCount,
            ConditionalFormulaScalarFunctionKind.Today or
            ConditionalFormulaScalarFunctionKind.Now or
            ConditionalFormulaScalarFunctionKind.Na or
            ConditionalFormulaScalarFunctionKind.Pi => argumentCount == 0,
            ConditionalFormulaScalarFunctionKind.Row or
            ConditionalFormulaScalarFunctionKind.Column => argumentCount is 0 or 1,
            ConditionalFormulaScalarFunctionKind.Rows or
            ConditionalFormulaScalarFunctionKind.Columns or
            ConditionalFormulaScalarFunctionKind.Areas => argumentCount == 1,
            _ => false
        };

    private static bool TryCreateFormulaAggregateOperand(
        FunctionCallNode function,
        out ConditionalFormulaOperand operand)
    {
        operand = default;
        if (!TryGetFormulaAggregateKind(function.FunctionName, out var aggregateKind) ||
            !IsFormulaAggregateArgumentCountSupported(aggregateKind, function.Arguments.Count))
        {
            return false;
        }

        var arguments = new ConditionalFormulaAggregateArgument[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryCreateFormulaAggregateArgument(function.Arguments[i], out arguments[i]))
                return false;
        }

        operand = new ConditionalFormulaOperand(
            ConditionalFormulaOperandKind.Aggregate,
            null,
            0,
            0,
            true,
            true,
            null,
            aggregateKind,
            arguments);
        return true;
    }

    private static bool IsFormulaAggregateFunction(string functionName) =>
        TryGetFormulaAggregateKind(functionName, out _);

    private static bool IsFormulaAggregateArgumentCountSupported(
        ConditionalFormulaAggregateKind aggregateKind,
        int argumentCount) =>
        aggregateKind switch
        {
            ConditionalFormulaAggregateKind.SumProduct => argumentCount is >= 1 and <= MaxFormulaSumProductArgumentCount,
            _ when IsFormulaPairwiseAggregate(aggregateKind) => argumentCount == 2,
            _ => argumentCount > 0
        };

    private static bool TryGetFormulaAggregateKind(
        string functionName,
        out ConditionalFormulaAggregateKind kind)
    {
        switch (functionName.ToUpperInvariant())
        {
            case "SUM":
                kind = ConditionalFormulaAggregateKind.Sum;
                return true;
            case "SUMSQ":
                kind = ConditionalFormulaAggregateKind.SumSq;
                return true;
            case "SUMPRODUCT":
                kind = ConditionalFormulaAggregateKind.SumProduct;
                return true;
            case "SUMXMY2":
                kind = ConditionalFormulaAggregateKind.SumXMy2;
                return true;
            case "SUMX2MY2":
                kind = ConditionalFormulaAggregateKind.SumX2My2;
                return true;
            case "SUMX2PY2":
                kind = ConditionalFormulaAggregateKind.SumX2Py2;
                return true;
            case "DEVSQ":
                kind = ConditionalFormulaAggregateKind.DevSq;
                return true;
            case "STDEV":
            case "STDEV.S":
                kind = ConditionalFormulaAggregateKind.StdDevSample;
                return true;
            case "STDEVP":
            case "STDEV.P":
                kind = ConditionalFormulaAggregateKind.StdDevPopulation;
                return true;
            case "VAR":
            case "VAR.S":
                kind = ConditionalFormulaAggregateKind.VarianceSample;
                return true;
            case "VARP":
            case "VAR.P":
                kind = ConditionalFormulaAggregateKind.VariancePopulation;
                return true;
            case "AVEDEV":
                kind = ConditionalFormulaAggregateKind.AveDev;
                return true;
            case "GEOMEAN":
                kind = ConditionalFormulaAggregateKind.GeoMean;
                return true;
            case "HARMEAN":
                kind = ConditionalFormulaAggregateKind.HarMean;
                return true;
            case "PRODUCT":
                kind = ConditionalFormulaAggregateKind.Product;
                return true;
            case "AVERAGE":
                kind = ConditionalFormulaAggregateKind.Average;
                return true;
            case "AVERAGEA":
                kind = ConditionalFormulaAggregateKind.AverageA;
                return true;
            case "MEDIAN":
                kind = ConditionalFormulaAggregateKind.Median;
                return true;
            case "MIN":
                kind = ConditionalFormulaAggregateKind.Min;
                return true;
            case "MINA":
                kind = ConditionalFormulaAggregateKind.MinA;
                return true;
            case "MAX":
                kind = ConditionalFormulaAggregateKind.Max;
                return true;
            case "MAXA":
                kind = ConditionalFormulaAggregateKind.MaxA;
                return true;
            case "COUNT":
                kind = ConditionalFormulaAggregateKind.Count;
                return true;
            case "COUNTA":
                kind = ConditionalFormulaAggregateKind.CountA;
                return true;
            case "COUNTBLANK":
                kind = ConditionalFormulaAggregateKind.CountBlank;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsFormulaPairwiseAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
        aggregateKind is
            ConditionalFormulaAggregateKind.SumXMy2 or
            ConditionalFormulaAggregateKind.SumX2My2 or
            ConditionalFormulaAggregateKind.SumX2Py2;

    private static bool TryCreateFormulaAggregateArgument(
        FormulaNode node,
        out ConditionalFormulaAggregateArgument argument)
    {
        argument = default;
        switch (node)
        {
            case CellRefNode cell:
                argument = new ConditionalFormulaAggregateArgument(
                    ConditionalFormulaAggregateArgumentKind.Reference,
                    null,
                    cell.Row,
                    cell.ColumnNumber,
                    cell.IsRowAbsolute,
                    cell.IsColAbsolute,
                    0,
                    0,
                    true,
                    true,
                    cell.SheetName);
                return true;
            case RangeRefNode range:
                argument = new ConditionalFormulaAggregateArgument(
                    ConditionalFormulaAggregateArgumentKind.Range,
                    null,
                    range.Start.Row,
                    range.Start.ColumnNumber,
                    range.Start.IsRowAbsolute,
                    range.Start.IsColAbsolute,
                    range.End.Row,
                    range.End.ColumnNumber,
                    range.End.IsRowAbsolute,
                    range.End.IsColAbsolute,
                    range.SheetName ?? range.Start.SheetName);
                return true;
            case NumberNode number:
                argument = LiteralFormulaAggregateArgument(new NumberValue(number.Value));
                return true;
            case StringNode text:
                argument = LiteralFormulaAggregateArgument(new TextValue(text.Value));
                return true;
            case BooleanNode boolean:
                argument = LiteralFormulaAggregateArgument(new BoolValue(boolean.Value));
                return true;
            case ErrorNode error:
                argument = LiteralFormulaAggregateArgument(error.Error);
                return true;
            case UnaryOpNode { Operator: UnaryOperator.Negate, Operand: NumberNode number }:
                argument = LiteralFormulaAggregateArgument(new NumberValue(-number.Value));
                return true;
            case UnaryOpNode { Operator: UnaryOperator.Percent, Operand: NumberNode number }:
                argument = LiteralFormulaAggregateArgument(new NumberValue(number.Value / 100d));
                return true;
            case FormulaNode scalar when TryCreateFormulaOperand(scalar, out var operand):
                argument = OperandFormulaAggregateArgument(operand);
                return true;
            default:
                return false;
        }
    }

    private static ConditionalFormulaAggregateArgument LiteralFormulaAggregateArgument(ScalarValue value) =>
        new(
            ConditionalFormulaAggregateArgumentKind.Literal,
            value,
            0,
            0,
            true,
            true,
            0,
            0,
            true,
            true,
            null);

    private static ConditionalFormulaAggregateArgument OperandFormulaAggregateArgument(ConditionalFormulaOperand operand) =>
        new(
            ConditionalFormulaAggregateArgumentKind.Operand,
            null,
            0,
            0,
            true,
            true,
            0,
            0,
            true,
            true,
            null,
            operand);

    private static int CompareFormulaValues(ScalarValue left, ScalarValue right)
    {
        if (TryGetNumber(left, out var leftNumber) && TryGetNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        if (left is TextValue leftText && right is TextValue rightText)
            return string.Compare(leftText.Value, rightText.Value, StringComparison.OrdinalIgnoreCase);

        if (left is BoolValue leftBool && right is BoolValue rightBool)
            return leftBool.Value.CompareTo(rightBool.Value);

        return FormulaValueTypeOrder(left).CompareTo(FormulaValueTypeOrder(right));
    }

    private static int FormulaValueTypeOrder(ScalarValue value) => value switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    private static uint? ShiftFormulaRow(uint row, bool isAbsolute, int rowOffset)
    {
        if (isAbsolute)
            return row;

        var shifted = (long)row + rowOffset;
        return shifted is < 1 or > CellAddress.MaxRow ? null : (uint)shifted;
    }

    private static uint? ShiftFormulaColumn(uint col, bool isAbsolute, int colOffset)
    {
        if (isAbsolute)
            return col;

        var shifted = (long)col + colOffset;
        return shifted is < 1 or > CellAddress.MaxCol ? null : (uint)shifted;
    }

    private static bool HasVisibleCellText(ScalarValue value) =>
        value switch
        {
            TextValue text => !string.IsNullOrWhiteSpace(text.Value),
            NumberValue or BoolValue or DateTimeValue or ErrorValue => true,
            _ => false
        };

    private static bool IsCellValueRuleTrue(ConditionalFormat rule, ScalarValue value)
    {
        var cellText = ValueText(value);
        var firstComparison = CompareCellValue(value, cellText, rule.Value1);
        var secondComparison = CompareCellValue(value, cellText, rule.Value2);

        return rule.Operator switch
        {
            CfOperator.Equal => firstComparison == 0,
            CfOperator.NotEqual => firstComparison != 0,
            CfOperator.GreaterThan => firstComparison > 0,
            CfOperator.GreaterThanOrEqual => firstComparison >= 0,
            CfOperator.LessThan => firstComparison < 0,
            CfOperator.LessThanOrEqual => firstComparison <= 0,
            CfOperator.Between => firstComparison >= 0 && secondComparison <= 0,
            CfOperator.NotBetween => firstComparison < 0 || secondComparison > 0,
            _ => false
        };
    }

    private static bool IsDateOccurringRuleTrue(ConditionalFormat rule, ScalarValue value)
    {
        if (value is not DateTimeValue dateValue)
            return false;

        var date = dateValue.ToDateTime().Date;
        var today = DateTime.Today;

        return (rule.DateOccurringPeriod ?? "today") switch
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

    private static int CompareCellValue(ScalarValue value, string cellText, string? threshold)
    {
        if (TryGetNumber(value, out var cellNumber) &&
            double.TryParse(threshold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var thresholdNumber))
        {
            return cellNumber.CompareTo(thresholdNumber);
        }

        return string.Compare(cellText, threshold ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetNumber(ScalarValue value, out double number)
    {
        switch (value)
        {
            case NumberValue numeric:
                number = numeric.Value;
                return true;
            case DateTimeValue dateTime:
                number = dateTime.Value;
                return true;
            case BoolValue boolean:
                number = boolean.Value ? 1 : 0;
                return true;
            case TextValue text when double.TryParse(
                text.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed):
                number = parsed;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string ValueText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeValue dateTime => dateTime.ToDateTime().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static double ContrastRatio(CellColor first, CellColor second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double MinimumTextContrastRatio(CellStyle style) =>
        MinimumTextContrastRatio(style.FontSize, style.Bold);

    private static double MinimumTextContrastRatio(double fontSize, bool bold) =>
        fontSize >= 18 || (bold && fontSize >= 14)
            ? 3.0
            : 4.5;

    private static bool HasSufficientCellTextContrast(
        CellStyle style,
        WorkbookTheme theme,
        double minimumContrastRatio)
    {
        var fontColor = style.ResolveFontColor(theme);
        var baseFill = style.ResolveFillColor(theme) ?? CellColor.White;
        if (ContrastRatio(fontColor, baseFill) < minimumContrastRatio)
            return false;

        if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return true;

        var patternColor = style.ResolveFillPatternColor(theme) ?? CellColor.Black;
        if (TryGetGrayPatternOpacity(style.FillPatternStyle, out var opacity))
            return ContrastRatio(fontColor, Blend(patternColor, baseFill, opacity)) >= minimumContrastRatio;

        return ContrastRatio(fontColor, patternColor) >= minimumContrastRatio;
    }

    private static bool TryGetGrayPatternOpacity(CellFillPatternStyle patternStyle, out double opacity)
    {
        opacity = patternStyle switch
        {
            CellFillPatternStyle.Gray0625 => 0.12,
            CellFillPatternStyle.Gray125 => 0.18,
            CellFillPatternStyle.LightGray => 0.28,
            CellFillPatternStyle.MediumGray => 0.45,
            CellFillPatternStyle.DarkGray => 0.62,
            _ => double.NaN
        };
        return !double.IsNaN(opacity);
    }

    private static CellColor Blend(CellColor foreground, CellColor background, double opacity) => new(
        BlendChannel(foreground.R, background.R, opacity),
        BlendChannel(foreground.G, background.G, opacity),
        BlendChannel(foreground.B, background.B, opacity));

    private static byte BlendChannel(byte foreground, byte background, double opacity) =>
        (byte)Math.Round(foreground * opacity + background * (1 - opacity));

    private static double RelativeLuminance(CellColor color) =>
        0.2126 * LinearRgb(color.R) +
        0.7152 * LinearRgb(color.G) +
        0.0722 * LinearRgb(color.B);

    private static double LinearRgb(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private readonly record struct CellContrastCheck(double MinimumContrastRatio, bool HasSufficientContrast);

    private abstract record ConditionalFormulaExpression;

    private sealed record ConditionalFormulaOperandExpression(
        ConditionalFormulaOperand Operand) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaComparisonExpression(
        ConditionalFormulaComparison Comparison) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaLogicalExpression(
        ConditionalFormulaLogicalOperator Operator,
        IReadOnlyList<ConditionalFormulaExpression> Operands) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaPredicateExpression(
        ConditionalFormulaPredicate Predicate) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaIfExpression(
        ConditionalFormulaExpression Condition,
        ConditionalFormulaExpression WhenTrue,
        ConditionalFormulaExpression WhenFalse) : ConditionalFormulaExpression;

    private enum ConditionalFormulaLogicalOperator
    {
        And,
        Or,
        Xor,
        Not
    }

    private readonly record struct ConditionalFormulaComparison(
        ConditionalFormulaOperand Left,
        BinaryOperator Operator,
        ConditionalFormulaOperand Right);

    private readonly record struct ConditionalFormulaPredicate(
        ConditionalFormulaPredicateKind Kind,
        ConditionalFormulaOperand Operand);

    private enum ConditionalFormulaPredicateKind
    {
        IsBlank,
        IsNumber,
        IsText,
        IsNonText,
        IsLogical,
        IsError,
        IsErr,
        IsNa,
        IsEven,
        IsOdd,
        IsRef,
        IsFormula
    }

    private readonly record struct ConditionalFormulaOperand(
        ConditionalFormulaOperandKind Kind,
        ScalarValue? Literal,
        uint Row,
        uint Col,
        bool IsRowAbsolute,
        bool IsColAbsolute,
        string? SheetName,
        ConditionalFormulaAggregateKind AggregateKind = default,
        IReadOnlyList<ConditionalFormulaAggregateArgument>? AggregateArguments = null,
        ConditionalFormulaArithmetic? Arithmetic = null,
        ConditionalFormulaUnary? Unary = null,
        ConditionalFormulaScalarFunction? ScalarFunction = null,
        ConditionalFormulaReferenceRange? ReferenceRange = null);

    private enum ConditionalFormulaOperandKind
    {
        Literal,
        Reference,
        ReferenceRange,
        Unary,
        Arithmetic,
        Aggregate,
        ScalarFunction
    }

    private sealed record ConditionalFormulaUnary(
        UnaryOperator Operator,
        ConditionalFormulaOperand Operand);

    private sealed record ConditionalFormulaArithmetic(
        BinaryOperator Operator,
        ConditionalFormulaOperand Left,
        ConditionalFormulaOperand Right);

    private sealed record ConditionalFormulaScalarFunction(
        ConditionalFormulaScalarFunctionKind Kind,
        IReadOnlyList<ConditionalFormulaOperand> Arguments);

    private readonly record struct ConditionalFormulaReferenceRange(
        uint EndRow,
        uint EndCol,
        bool IsEndRowAbsolute,
        bool IsEndColAbsolute);

    private enum ConditionalFormulaScalarFunctionKind
    {
        Abs,
        Int,
        Even,
        Odd,
        Round,
        RoundUp,
        RoundDown,
        MRound,
        Ceiling,
        CeilingMath,
        IsoCeiling,
        Floor,
        FloorMath,
        FloorPrecise,
        Trunc,
        Fact,
        FactDouble,
        Mod,
        Quotient,
        Combin,
        Combina,
        Permut,
        PermutationA,
        Multinomial,
        Gcd,
        Lcm,
        Sqrt,
        SqrtPi,
        Sign,
        Power,
        Exp,
        Ln,
        Log10,
        Log,
        Degrees,
        Radians,
        Sin,
        Csc,
        Csch,
        Sinh,
        Asinh,
        Acosh,
        Cosh,
        Sech,
        Tanh,
        Atanh,
        Acoth,
        Coth,
        Asin,
        Acos,
        Acot,
        Atan,
        Atan2,
        Cos,
        Sec,
        Cot,
        Tan,
        Pi,
        Arabic,
        Roman,
        Unichar,
        Unicode,
        Char,
        Code,
        Proper,
        Rept,
        Clean,
        T,
        Value,
        NumberValue,
        Len,
        LenB,
        Upper,
        Lower,
        Trim,
        Concat,
        Concatenate,
        TextJoin,
        Substitute,
        Replace,
        ReplaceB,
        Left,
        Right,
        LeftB,
        RightB,
        Mid,
        MidB,
        Find,
        Search,
        FindB,
        SearchB,
        Exact,
        Date,
        DateValue,
        Time,
        TimeValue,
        Year,
        Month,
        Day,
        Hour,
        Minute,
        Second,
        Today,
        Now,
        Weekday,
        Weeknum,
        IsoWeeknum,
        EDate,
        EOMonth,
        Days,
        Datedif,
        Days360,
        Yearfrac,
        Workday,
        WorkdayIntl,
        Networkdays,
        NetworkdaysIntl,
        N,
        Type,
        ErrorType,
        Na,
        Row,
        Column,
        Rows,
        Columns,
        Areas,
        Bin2Dec,
        Bin2Hex,
        Bin2Oct,
        Hex2Bin,
        Hex2Dec,
        Hex2Oct,
        Oct2Bin,
        Oct2Dec,
        Oct2Hex,
        Dec2Bin,
        Dec2Hex,
        Dec2Oct,
        Base,
        Decimal,
        Convert,
        Complex,
        ImReal,
        Imaginary,
        ImAbs,
        ImArgument,
        ImConjugate,
        ImCos,
        ImCosh,
        ImCot,
        ImCsc,
        ImCsch,
        ImDiv,
        ImExp,
        ImLn,
        ImLog10,
        ImLog2,
        ImPower,
        ImProduct,
        ImSin,
        ImSinh,
        ImSec,
        ImSech,
        ImSqrt,
        ImSub,
        ImSum,
        ImTan,
        Delta,
        Erf,
        ErfPrecise,
        Erfc,
        ErfcPrecise,
        GeStep,
        BitAnd,
        BitOr,
        BitXor,
        BitLShift,
        BitRShift
    }

    private enum ConditionalFormulaAggregateKind
    {
        Sum,
        SumSq,
        SumProduct,
        SumXMy2,
        SumX2My2,
        SumX2Py2,
        DevSq,
        StdDevSample,
        StdDevPopulation,
        VarianceSample,
        VariancePopulation,
        AveDev,
        GeoMean,
        HarMean,
        Product,
        Average,
        AverageA,
        Median,
        Min,
        MinA,
        Max,
        MaxA,
        Count,
        CountA,
        CountBlank
    }

    private readonly record struct ConditionalFormulaAggregateArgument(
        ConditionalFormulaAggregateArgumentKind Kind,
        ScalarValue? Literal,
        uint Row,
        uint Col,
        bool IsRowAbsolute,
        bool IsColAbsolute,
        uint EndRow,
        uint EndCol,
        bool IsEndRowAbsolute,
        bool IsEndColAbsolute,
        string? SheetName,
        ConditionalFormulaOperand? Operand = null);

    private readonly record struct ConditionalFormulaPairwiseAggregateValue(
        ScalarValue Value,
        bool IsDirectArgument);

    private readonly record struct ConditionalFormulaPairwiseAggregateValues(
        int RowCount,
        int ColCount,
        IReadOnlyList<ConditionalFormulaPairwiseAggregateValue> Values);

    private enum ConditionalFormulaAggregateArgumentKind
    {
        Literal,
        Reference,
        Range,
        Operand
    }

    private sealed class ConditionalFormatEvaluationCache(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells)
    {
        private static readonly IReadOnlyDictionary<string, int> FormulaArabicRomanRemainders =
            BuildFormulaArabicRemainderMap();

        private static readonly CultureInfo FormulaTextScalarNumberCulture = CultureInfo.GetCultureInfo("en-US");

        private readonly Dictionary<ConditionalFormat, Dictionary<string, int>> _valueCounts = new();
        private readonly Dictionary<ConditionalFormat, RangeAverage> _averages = new();
        private readonly Dictionary<ConditionalFormat, HashSet<CellAddress>?> _topBottomMatches = new();
        private readonly Dictionary<ConditionalFormat, ConditionalFormulaExpression?> _formulaExpressions = new();
        private uint _formulaCurrentRow;
        private uint _formulaCurrentCol;

        public bool HasDuplicateValue(ConditionalFormat rule, ScalarValue value) =>
            TryGetValueCount(rule, value, out var count) && count > 1;

        public bool HasUniqueValue(ConditionalFormat rule, ScalarValue value) =>
            TryGetValueCount(rule, value, out var count) && count == 1;

        public bool MatchesAverageRule(ConditionalFormat rule, ScalarValue value)
        {
            if (!TryGetNumber(value, out var number))
                return false;

            var average = GetRangeAverage(rule);
            if (!average.HasValues)
                return false;

            return rule.AboveAverage
                ? number > average.Value
                : number < average.Value;
        }

        public bool MatchesTopBottomRule(ConditionalFormat rule, CellAddress address) =>
            GetTopBottomMatches(rule)?.Contains(address) == true;

        public bool MatchesFormulaRule(ConditionalFormat rule, CellAddress address)
        {
            if (!TryGetFormulaExpression(rule, out var expression))
                return false;

            var rowOffset = (int)address.Row - (int)rule.AppliesTo.Start.Row;
            var colOffset = (int)address.Col - (int)rule.AppliesTo.Start.Col;
            _formulaCurrentRow = address.Row;
            _formulaCurrentCol = address.Col;
            return EvaluateFormulaExpression(expression, rowOffset, colOffset) == true;
        }

        private bool? EvaluateFormulaExpression(
            ConditionalFormulaExpression expression,
            int rowOffset,
            int colOffset) =>
            expression switch
            {
                ConditionalFormulaOperandExpression operand => EvaluateFormulaBooleanOperand(operand.Operand, rowOffset, colOffset),
                ConditionalFormulaComparisonExpression comparison => EvaluateFormulaComparison(comparison.Comparison, rowOffset, colOffset),
                ConditionalFormulaLogicalExpression logical => EvaluateFormulaLogical(logical, rowOffset, colOffset),
                ConditionalFormulaPredicateExpression predicate => EvaluateFormulaPredicate(predicate.Predicate, rowOffset, colOffset),
                ConditionalFormulaIfExpression ifExpression => EvaluateFormulaIf(ifExpression, rowOffset, colOffset),
                _ => null
            };

        private bool? EvaluateFormulaBooleanOperand(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset)
        {
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return null;

            return FormulaBooleanValue(value);
        }

        private static bool? FormulaBooleanValue(ScalarValue value) =>
            value switch
            {
                BoolValue boolean => boolean.Value,
                NumberValue number => number.Value != 0,
                DateTimeValue dateTime => dateTime.Value != 0,
                _ => null
            };

        private bool? EvaluateFormulaLogical(
            ConditionalFormulaLogicalExpression logical,
            int rowOffset,
            int colOffset)
        {
            return logical.Operator switch
            {
                ConditionalFormulaLogicalOperator.And => EvaluateFormulaAnd(logical.Operands, rowOffset, colOffset),
                ConditionalFormulaLogicalOperator.Or => EvaluateFormulaOr(logical.Operands, rowOffset, colOffset),
                ConditionalFormulaLogicalOperator.Xor => EvaluateFormulaXor(logical.Operands, rowOffset, colOffset),
                ConditionalFormulaLogicalOperator.Not => logical.Operands.Count == 1
                    ? Negate(EvaluateFormulaExpression(logical.Operands[0], rowOffset, colOffset))
                    : null,
                _ => null
            };
        }

        private bool? EvaluateFormulaAnd(
            IReadOnlyList<ConditionalFormulaExpression> operands,
            int rowOffset,
            int colOffset)
        {
            var hasUnknown = false;
            for (var i = 0; i < operands.Count; i++)
            {
                var result = EvaluateFormulaExpression(operands[i], rowOffset, colOffset);
                if (result == false)
                    return false;

                hasUnknown |= !result.HasValue;
            }

            return hasUnknown ? null : true;
        }

        private bool? EvaluateFormulaOr(
            IReadOnlyList<ConditionalFormulaExpression> operands,
            int rowOffset,
            int colOffset)
        {
            var hasUnknown = false;
            for (var i = 0; i < operands.Count; i++)
            {
                var result = EvaluateFormulaExpression(operands[i], rowOffset, colOffset);
                if (result == true)
                    return true;

                hasUnknown |= !result.HasValue;
            }

            return hasUnknown ? null : false;
        }

        private bool? EvaluateFormulaXor(
            IReadOnlyList<ConditionalFormulaExpression> operands,
            int rowOffset,
            int colOffset)
        {
            var trueCount = 0;
            for (var i = 0; i < operands.Count; i++)
            {
                var result = EvaluateFormulaExpression(operands[i], rowOffset, colOffset);
                if (!result.HasValue)
                    return null;

                if (result.Value)
                    trueCount++;
            }

            return trueCount % 2 == 1;
        }

        private static bool? Negate(bool? value) =>
            value.HasValue ? !value.Value : null;

        private bool? EvaluateFormulaIf(
            ConditionalFormulaIfExpression ifExpression,
            int rowOffset,
            int colOffset)
        {
            var condition = EvaluateFormulaExpression(ifExpression.Condition, rowOffset, colOffset);
            return condition switch
            {
                true => EvaluateFormulaExpression(ifExpression.WhenTrue, rowOffset, colOffset),
                false => EvaluateFormulaExpression(ifExpression.WhenFalse, rowOffset, colOffset),
                _ => null
            };
        }

        private bool? EvaluateFormulaPredicate(
            ConditionalFormulaPredicate predicate,
            int rowOffset,
            int colOffset)
        {
            if (predicate.Kind == ConditionalFormulaPredicateKind.IsRef)
                return EvaluateFormulaIsRefPredicate(predicate.Operand, rowOffset, colOffset);

            if (predicate.Kind == ConditionalFormulaPredicateKind.IsFormula)
                return EvaluateFormulaIsFormulaPredicate(predicate.Operand, rowOffset, colOffset);

            if (!TryResolveFormulaOperand(predicate.Operand, rowOffset, colOffset, out var value))
                return null;

            if (value is RangeValue)
                return null;

            return predicate.Kind switch
            {
                ConditionalFormulaPredicateKind.IsBlank => value is BlankValue,
                ConditionalFormulaPredicateKind.IsNumber => value is NumberValue or DateTimeValue,
                ConditionalFormulaPredicateKind.IsText => value is TextValue,
                ConditionalFormulaPredicateKind.IsNonText => value is not TextValue,
                ConditionalFormulaPredicateKind.IsLogical => value is BoolValue,
                ConditionalFormulaPredicateKind.IsError => value is ErrorValue,
                ConditionalFormulaPredicateKind.IsErr => value is ErrorValue error && !IsNaError(error),
                ConditionalFormulaPredicateKind.IsNa => value is ErrorValue error && IsNaError(error),
                ConditionalFormulaPredicateKind.IsEven => EvaluateFormulaParityPredicate(value, expectEven: true),
                ConditionalFormulaPredicateKind.IsOdd => EvaluateFormulaParityPredicate(value, expectEven: false),
                _ => null
            };
        }

        private bool? EvaluateFormulaIsRefPredicate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset)
        {
            if (operand.Kind != ConditionalFormulaOperandKind.Reference)
                return false;

            return TryResolveFormulaReference(operand, rowOffset, colOffset, out _, out _, out _)
                ? true
                : null;
        }

        private bool? EvaluateFormulaIsFormulaPredicate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset)
        {
            if (operand.Kind != ConditionalFormulaOperandKind.Reference)
                return null;

            return TryResolveFormulaReference(operand, rowOffset, colOffset, out var targetSheet, out var row, out var col)
                ? targetSheet.GetCell(row, col)?.HasFormula == true
                : null;
        }

        private static bool IsNaError(ErrorValue error) =>
            string.Equals(error.Code, ErrorValue.NA.Code, StringComparison.OrdinalIgnoreCase);

        private static bool? EvaluateFormulaParityPredicate(ScalarValue value, bool expectEven)
        {
            var number = value switch
            {
                NumberValue numeric => numeric.Value,
                DateTimeValue dateTime => dateTime.Value,
                _ => (double?)null
            };

            if (!number.HasValue || !double.IsFinite(number.Value))
                return null;

            var truncated = Math.Truncate(number.Value);
            if (truncated < long.MinValue || truncated > long.MaxValue)
                return null;

            var isEven = ((long)truncated) % 2 == 0;
            return expectEven ? isEven : !isEven;
        }

        private bool? EvaluateFormulaComparison(
            ConditionalFormulaComparison comparison,
            int rowOffset,
            int colOffset)
        {
            if (!TryResolveFormulaOperand(comparison.Left, rowOffset, colOffset, out var left) ||
                !TryResolveFormulaOperand(comparison.Right, rowOffset, colOffset, out var right))
            {
                return null;
            }

            if (left is ErrorValue or RangeValue || right is ErrorValue or RangeValue)
                return null;

            var result = CompareFormulaValues(left, right);
            return comparison.Operator switch
            {
                BinaryOperator.Equal => result == 0,
                BinaryOperator.NotEqual => result != 0,
                BinaryOperator.LessThan => result < 0,
                BinaryOperator.GreaterThan => result > 0,
                BinaryOperator.LessOrEqual => result <= 0,
                BinaryOperator.GreaterOrEqual => result >= 0,
                _ => false
            };
        }

        private bool TryGetFormulaExpression(ConditionalFormat rule, out ConditionalFormulaExpression expression)
        {
            if (_formulaExpressions.TryGetValue(rule, out var cached))
            {
                if (cached is null)
                {
                    expression = default!;
                    return false;
                }

                expression = cached;
                return true;
            }

            if (TryCreateFormulaExpression(rule.FormulaText, out expression))
            {
                _formulaExpressions[rule] = expression;
                return true;
            }

            _formulaExpressions[rule] = null;
            expression = default!;
            return false;
        }

        private bool TryResolveFormulaOperand(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            if (operand.Kind == ConditionalFormulaOperandKind.Literal)
            {
                value = operand.Literal ?? BlankValue.Instance;
                return true;
            }

            if (operand.Kind == ConditionalFormulaOperandKind.Aggregate)
                return TryEvaluateFormulaAggregate(operand, rowOffset, colOffset, out value);

            if (operand.Kind == ConditionalFormulaOperandKind.Unary)
                return TryEvaluateFormulaUnary(operand, rowOffset, colOffset, out value);

            if (operand.Kind == ConditionalFormulaOperandKind.Arithmetic)
                return TryEvaluateFormulaArithmetic(operand, rowOffset, colOffset, out value);

            if (operand.Kind == ConditionalFormulaOperandKind.ScalarFunction)
                return TryEvaluateFormulaScalarFunction(operand, rowOffset, colOffset, out value);

            if (!TryResolveFormulaReference(operand, rowOffset, colOffset, out var targetSheet, out var row, out var col))
            {
                value = ErrorValue.Ref;
                return false;
            }

            value = targetSheet.GetValue(row, col);
            return true;
        }

        private bool TryEvaluateFormulaUnary(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.Unary is not { } unary ||
                !TryResolveFormulaOperand(unary.Operand, rowOffset, colOffset, out var inner) ||
                !TryGetFormulaArithmeticNumber(inner, out var number))
            {
                return false;
            }

            var result = unary.Operator switch
            {
                UnaryOperator.Negate => -number,
                UnaryOperator.Percent => number / 100d,
                _ => double.NaN
            };

            if (!double.IsFinite(result))
                return false;

            value = new NumberValue(result);
            return true;
        }

        private bool TryEvaluateFormulaArithmetic(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.Arithmetic is not { } arithmetic ||
                !TryResolveFormulaOperand(arithmetic.Left, rowOffset, colOffset, out var left) ||
                !TryResolveFormulaOperand(arithmetic.Right, rowOffset, colOffset, out var right) ||
                !TryGetFormulaArithmeticNumber(left, out var leftNumber) ||
                !TryGetFormulaArithmeticNumber(right, out var rightNumber))
            {
                return false;
            }

            var result = arithmetic.Operator switch
            {
                BinaryOperator.Add => leftNumber + rightNumber,
                BinaryOperator.Subtract => leftNumber - rightNumber,
                BinaryOperator.Multiply => leftNumber * rightNumber,
                BinaryOperator.Divide when rightNumber != 0 => leftNumber / rightNumber,
                BinaryOperator.Power => Math.Pow(leftNumber, rightNumber),
                _ => double.NaN
            };

            if (!double.IsFinite(result))
                return false;

            value = new NumberValue(result);
            return true;
        }

        private static bool TryGetFormulaArithmeticNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    break;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    break;
                case BoolValue boolean:
                    number = boolean.Value ? 1 : 0;
                    break;
                default:
                    number = 0;
                    return false;
            }

            return double.IsFinite(number);
        }

        private bool TryEvaluateFormulaScalarFunction(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.ScalarFunction is not { } function ||
                !FormulaScalarFunctionArityMatches(function.Kind, function.Arguments.Count))
            {
                return false;
            }

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.Abs:
                case ConditionalFormulaScalarFunctionKind.Int:
                case ConditionalFormulaScalarFunctionKind.Even:
                case ConditionalFormulaScalarFunctionKind.Odd:
                case ConditionalFormulaScalarFunctionKind.Round:
                case ConditionalFormulaScalarFunctionKind.RoundUp:
                case ConditionalFormulaScalarFunctionKind.RoundDown:
                case ConditionalFormulaScalarFunctionKind.MRound:
                case ConditionalFormulaScalarFunctionKind.Ceiling:
                case ConditionalFormulaScalarFunctionKind.CeilingMath:
                case ConditionalFormulaScalarFunctionKind.IsoCeiling:
                case ConditionalFormulaScalarFunctionKind.Floor:
                case ConditionalFormulaScalarFunctionKind.FloorMath:
                case ConditionalFormulaScalarFunctionKind.FloorPrecise:
                case ConditionalFormulaScalarFunctionKind.Trunc:
                case ConditionalFormulaScalarFunctionKind.Fact:
                case ConditionalFormulaScalarFunctionKind.FactDouble:
                case ConditionalFormulaScalarFunctionKind.Mod:
                case ConditionalFormulaScalarFunctionKind.Quotient:
                case ConditionalFormulaScalarFunctionKind.Combin:
                case ConditionalFormulaScalarFunctionKind.Combina:
                case ConditionalFormulaScalarFunctionKind.Permut:
                case ConditionalFormulaScalarFunctionKind.PermutationA:
                case ConditionalFormulaScalarFunctionKind.Multinomial:
                case ConditionalFormulaScalarFunctionKind.Gcd:
                case ConditionalFormulaScalarFunctionKind.Lcm:
                case ConditionalFormulaScalarFunctionKind.Sqrt:
                case ConditionalFormulaScalarFunctionKind.SqrtPi:
                case ConditionalFormulaScalarFunctionKind.Sign:
                case ConditionalFormulaScalarFunctionKind.Power:
                case ConditionalFormulaScalarFunctionKind.Exp:
                case ConditionalFormulaScalarFunctionKind.Ln:
                case ConditionalFormulaScalarFunctionKind.Log10:
                case ConditionalFormulaScalarFunctionKind.Log:
                case ConditionalFormulaScalarFunctionKind.Degrees:
                case ConditionalFormulaScalarFunctionKind.Radians:
                case ConditionalFormulaScalarFunctionKind.Sin:
                case ConditionalFormulaScalarFunctionKind.Csc:
                case ConditionalFormulaScalarFunctionKind.Csch:
                case ConditionalFormulaScalarFunctionKind.Sinh:
                case ConditionalFormulaScalarFunctionKind.Asinh:
                case ConditionalFormulaScalarFunctionKind.Acosh:
                case ConditionalFormulaScalarFunctionKind.Cosh:
                case ConditionalFormulaScalarFunctionKind.Sech:
                case ConditionalFormulaScalarFunctionKind.Tanh:
                case ConditionalFormulaScalarFunctionKind.Atanh:
                case ConditionalFormulaScalarFunctionKind.Acoth:
                case ConditionalFormulaScalarFunctionKind.Coth:
                case ConditionalFormulaScalarFunctionKind.Asin:
                case ConditionalFormulaScalarFunctionKind.Acos:
                case ConditionalFormulaScalarFunctionKind.Acot:
                case ConditionalFormulaScalarFunctionKind.Atan:
                case ConditionalFormulaScalarFunctionKind.Atan2:
                case ConditionalFormulaScalarFunctionKind.Cos:
                case ConditionalFormulaScalarFunctionKind.Sec:
                case ConditionalFormulaScalarFunctionKind.Cot:
                case ConditionalFormulaScalarFunctionKind.Tan:
                case ConditionalFormulaScalarFunctionKind.Delta:
                case ConditionalFormulaScalarFunctionKind.Erf:
                case ConditionalFormulaScalarFunctionKind.ErfPrecise:
                case ConditionalFormulaScalarFunctionKind.Erfc:
                case ConditionalFormulaScalarFunctionKind.ErfcPrecise:
                case ConditionalFormulaScalarFunctionKind.GeStep:
                case ConditionalFormulaScalarFunctionKind.BitAnd:
                case ConditionalFormulaScalarFunctionKind.BitOr:
                case ConditionalFormulaScalarFunctionKind.BitXor:
                case ConditionalFormulaScalarFunctionKind.BitLShift:
                case ConditionalFormulaScalarFunctionKind.BitRShift:
                    return TryEvaluateFormulaNumericScalarFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Pi:
                    value = new NumberValue(Math.PI);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Arabic:
                    return TryEvaluateFormulaArabicFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Roman:
                    return TryEvaluateFormulaRomanFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Unichar:
                    return TryEvaluateFormulaUnicharFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Unicode:
                    return TryEvaluateFormulaUnicodeFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Char:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaCharScalar, out value);
                case ConditionalFormulaScalarFunctionKind.Code:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaCodeScalar, out value);
                case ConditionalFormulaScalarFunctionKind.Proper:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaProperScalar, out value);
                case ConditionalFormulaScalarFunctionKind.Rept:
                    return TryEvaluateFormulaReptFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Clean:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaCleanScalar, out value);
                case ConditionalFormulaScalarFunctionKind.T:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaTScalar, out value);
                case ConditionalFormulaScalarFunctionKind.Value:
                    return TryEvaluateFormulaValueFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.NumberValue:
                    return TryEvaluateFormulaNumberValueFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Concat:
                    return TryEvaluateFormulaConcatFunction(function, rowOffset, colOffset, concatenate: false, out value);
                case ConditionalFormulaScalarFunctionKind.Concatenate:
                    return TryEvaluateFormulaConcatFunction(function, rowOffset, colOffset, concatenate: true, out value);
                case ConditionalFormulaScalarFunctionKind.TextJoin:
                    return TryEvaluateFormulaTextJoinFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Substitute:
                    return TryEvaluateFormulaSubstituteFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Replace:
                    return TryEvaluateFormulaReplaceFunction(function, rowOffset, colOffset, useBytes: false, out value);
                case ConditionalFormulaScalarFunctionKind.ReplaceB:
                    return TryEvaluateFormulaReplaceFunction(function, rowOffset, colOffset, useBytes: true, out value);
                case ConditionalFormulaScalarFunctionKind.Len:
                    if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var lenText))
                    {
                        return false;
                    }

                    value = new NumberValue(lenText.Length);
                    return true;
                case ConditionalFormulaScalarFunctionKind.LenB:
                    return TryEvaluateFormulaTextScalarUnaryFunction(function, rowOffset, colOffset, FormulaLenBScalar, out value);
                case ConditionalFormulaScalarFunctionKind.Upper:
                    if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var upperText))
                    {
                        return false;
                    }

                    value = new TextValue(upperText.ToUpperInvariant());
                    return true;
                case ConditionalFormulaScalarFunctionKind.Lower:
                    if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var lowerText))
                    {
                        return false;
                    }

                    value = new TextValue(lowerText.ToLowerInvariant());
                    return true;
                case ConditionalFormulaScalarFunctionKind.Trim:
                    if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var trimText))
                    {
                        return false;
                    }

                    value = new TextValue(trimText.Trim());
                    return true;
                case ConditionalFormulaScalarFunctionKind.Left:
                case ConditionalFormulaScalarFunctionKind.Right:
                    return TryEvaluateFormulaTextSliceFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.LeftB:
                case ConditionalFormulaScalarFunctionKind.RightB:
                    return TryEvaluateFormulaTextByteSliceFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Mid:
                    return TryEvaluateFormulaTextMidFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.MidB:
                    return TryEvaluateFormulaTextMidBFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Find:
                case ConditionalFormulaScalarFunctionKind.Search:
                    return TryEvaluateFormulaTextSearchFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.FindB:
                case ConditionalFormulaScalarFunctionKind.SearchB:
                    return TryEvaluateFormulaTextByteSearchFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Exact:
                    if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var firstText) ||
                        !TryResolveFormulaFunctionText(function.Arguments[1], rowOffset, colOffset, out var secondText))
                    {
                        return false;
                    }

                    value = new BoolValue(string.Equals(firstText, secondText, StringComparison.Ordinal));
                    return true;
                case ConditionalFormulaScalarFunctionKind.Date:
                case ConditionalFormulaScalarFunctionKind.DateValue:
                case ConditionalFormulaScalarFunctionKind.Time:
                case ConditionalFormulaScalarFunctionKind.TimeValue:
                case ConditionalFormulaScalarFunctionKind.Year:
                case ConditionalFormulaScalarFunctionKind.Month:
                case ConditionalFormulaScalarFunctionKind.Day:
                case ConditionalFormulaScalarFunctionKind.Hour:
                case ConditionalFormulaScalarFunctionKind.Minute:
                case ConditionalFormulaScalarFunctionKind.Second:
                case ConditionalFormulaScalarFunctionKind.Today:
                case ConditionalFormulaScalarFunctionKind.Now:
                case ConditionalFormulaScalarFunctionKind.Weekday:
                case ConditionalFormulaScalarFunctionKind.Weeknum:
                case ConditionalFormulaScalarFunctionKind.IsoWeeknum:
                case ConditionalFormulaScalarFunctionKind.EDate:
                case ConditionalFormulaScalarFunctionKind.EOMonth:
                case ConditionalFormulaScalarFunctionKind.Days:
                case ConditionalFormulaScalarFunctionKind.Datedif:
                case ConditionalFormulaScalarFunctionKind.Days360:
                case ConditionalFormulaScalarFunctionKind.Yearfrac:
                case ConditionalFormulaScalarFunctionKind.Workday:
                case ConditionalFormulaScalarFunctionKind.WorkdayIntl:
                case ConditionalFormulaScalarFunctionKind.Networkdays:
                case ConditionalFormulaScalarFunctionKind.NetworkdaysIntl:
                    return TryEvaluateFormulaDateScalarFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.N:
                    return TryEvaluateFormulaNFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Type:
                    return TryEvaluateFormulaTypeFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.ErrorType:
                    return TryEvaluateFormulaErrorTypeFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Na:
                    value = ErrorValue.NA;
                    return true;
                case ConditionalFormulaScalarFunctionKind.Row:
                case ConditionalFormulaScalarFunctionKind.Column:
                    return TryEvaluateFormulaRowColumnFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Rows:
                case ConditionalFormulaScalarFunctionKind.Columns:
                case ConditionalFormulaScalarFunctionKind.Areas:
                    return TryEvaluateFormulaReferenceDimensionFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Bin2Dec:
                case ConditionalFormulaScalarFunctionKind.Hex2Dec:
                case ConditionalFormulaScalarFunctionKind.Oct2Dec:
                    return TryEvaluateFormulaBaseToDecimalFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Bin2Hex:
                case ConditionalFormulaScalarFunctionKind.Bin2Oct:
                case ConditionalFormulaScalarFunctionKind.Hex2Bin:
                case ConditionalFormulaScalarFunctionKind.Hex2Oct:
                case ConditionalFormulaScalarFunctionKind.Oct2Bin:
                case ConditionalFormulaScalarFunctionKind.Oct2Hex:
                    return TryEvaluateFormulaBaseToBaseFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Dec2Bin:
                case ConditionalFormulaScalarFunctionKind.Dec2Hex:
                case ConditionalFormulaScalarFunctionKind.Dec2Oct:
                    return TryEvaluateFormulaDecimalToBaseFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Base:
                    return TryEvaluateFormulaBaseFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Decimal:
                    return TryEvaluateFormulaDecimalFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Convert:
                    return TryEvaluateFormulaConvertFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Complex:
                case ConditionalFormulaScalarFunctionKind.ImReal:
                case ConditionalFormulaScalarFunctionKind.Imaginary:
                case ConditionalFormulaScalarFunctionKind.ImAbs:
                case ConditionalFormulaScalarFunctionKind.ImArgument:
                case ConditionalFormulaScalarFunctionKind.ImConjugate:
                case ConditionalFormulaScalarFunctionKind.ImCos:
                case ConditionalFormulaScalarFunctionKind.ImCosh:
                case ConditionalFormulaScalarFunctionKind.ImCot:
                case ConditionalFormulaScalarFunctionKind.ImCsc:
                case ConditionalFormulaScalarFunctionKind.ImCsch:
                case ConditionalFormulaScalarFunctionKind.ImDiv:
                case ConditionalFormulaScalarFunctionKind.ImExp:
                case ConditionalFormulaScalarFunctionKind.ImLn:
                case ConditionalFormulaScalarFunctionKind.ImLog10:
                case ConditionalFormulaScalarFunctionKind.ImLog2:
                case ConditionalFormulaScalarFunctionKind.ImPower:
                case ConditionalFormulaScalarFunctionKind.ImProduct:
                case ConditionalFormulaScalarFunctionKind.ImSin:
                case ConditionalFormulaScalarFunctionKind.ImSinh:
                case ConditionalFormulaScalarFunctionKind.ImSec:
                case ConditionalFormulaScalarFunctionKind.ImSech:
                case ConditionalFormulaScalarFunctionKind.ImSqrt:
                case ConditionalFormulaScalarFunctionKind.ImSub:
                case ConditionalFormulaScalarFunctionKind.ImSum:
                case ConditionalFormulaScalarFunctionKind.ImTan:
                    return TryEvaluateFormulaComplexFunction(function, rowOffset, colOffset, out value);
                default:
                    return false;
            }
        }

        private bool TryEvaluateFormulaNFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            var argument = function.Arguments[0];
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var range))
                {
                    value = ErrorValue.Ref;
                    return false;
                }

                value = MapFormulaRange(range, FormulaNScalar);
                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var source))
            {
                value = ErrorValue.Ref;
                return false;
            }

            value = source is RangeValue rangeValue
                ? MapFormulaRange(rangeValue, FormulaNScalar)
                : FormulaNScalar(source);
            return true;
        }

        private bool TryEvaluateFormulaTypeFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            var argument = function.Arguments[0];
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryResolveFormulaReferenceRange(
                        argument,
                        rowOffset,
                        colOffset,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    value = ErrorValue.Ref;
                    return false;
                }

                value = new NumberValue(64);
                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var source))
            {
                value = ErrorValue.Ref;
                return false;
            }

            value = FormulaTypeScalar(source);
            return true;
        }

        private bool TryEvaluateFormulaErrorTypeFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            var argument = function.Arguments[0];
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var range))
                {
                    value = ErrorValue.Ref;
                    return false;
                }

                value = MapFormulaRange(range, FormulaErrorTypeScalar);
                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var source))
            {
                value = ErrorValue.Ref;
                return false;
            }

            value = source is RangeValue rangeValue
                ? MapFormulaRange(rangeValue, FormulaErrorTypeScalar)
                : FormulaErrorTypeScalar(source);
            return true;
        }

        private static ScalarValue FormulaNScalar(ScalarValue value) =>
            value switch
            {
                NumberValue numeric => numeric,
                DateTimeValue dateTime => new NumberValue(dateTime.Value),
                BoolValue boolean => new NumberValue(boolean.Value ? 1d : 0d),
                ErrorValue error => error,
                _ => new NumberValue(0d)
            };

        private static ScalarValue FormulaTypeScalar(ScalarValue value) =>
            value switch
            {
                ErrorValue => new NumberValue(16),
                RangeValue => new NumberValue(64),
                BoolValue => new NumberValue(4),
                TextValue => new NumberValue(2),
                NumberValue or DateTimeValue => new NumberValue(1),
                BlankValue => new NumberValue(1),
                _ => new NumberValue(1)
            };

        private static ScalarValue FormulaErrorTypeScalar(ScalarValue value)
        {
            if (value is not ErrorValue error)
                return ErrorValue.NA;

            return error.Code switch
            {
                "#NULL!" => new NumberValue(1),
                "#DIV/0!" => new NumberValue(2),
                "#VALUE!" => new NumberValue(3),
                "#REF!" => new NumberValue(4),
                "#NAME?" => new NumberValue(5),
                "#NUM!" => new NumberValue(6),
                "#N/A" => new NumberValue(7),
                "#GETTING_DATA" => new NumberValue(8),
                "#SPILL!" => new NumberValue(9),
                "#CONNECT!" => new NumberValue(10),
                "#BLOCKED!" => new NumberValue(11),
                "#UNKNOWN!" => new NumberValue(12),
                "#FIELD!" => new NumberValue(13),
                "#CALC!" => new NumberValue(14),
                _ => ErrorValue.NA
            };
        }

        private static RangeValue MapFormulaRange(RangeValue range, Func<ScalarValue, ScalarValue> map)
        {
            var cells = new ScalarValue[range.RowCount, range.ColCount];
            for (var row = 0; row < range.RowCount; row++)
                for (var col = 0; col < range.ColCount; col++)
                    cells[row, col] = map(range.Cells[row, col]);

            return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
        }

        private bool TryEvaluateFormulaBaseToDecimalFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            var (fromBase, maxDigits, signThreshold, modulus) = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Bin2Dec => (2, 10, 512L, 1024L),
                ConditionalFormulaScalarFunctionKind.Hex2Dec => (16, 10, 549755813888L, 1099511627776L),
                ConditionalFormulaScalarFunctionKind.Oct2Dec => (8, 10, 536870912L, 1073741824L),
                _ => (0, 0, 0L, 0L)
            };

            if (!TryParseFormulaBaseNumber(source, fromBase, maxDigits, signThreshold, modulus, out var result))
            {
                value = ErrorValue.Num;
                return true;
            }

            value = new NumberValue(result);
            return true;
        }

        private bool TryEvaluateFormulaBaseToBaseFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            ScalarValue? places = null;
            if (function.Arguments.Count == 2)
            {
                if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out places))
                    return false;

                if (places is ErrorValue placesError)
                {
                    value = placesError;
                    return true;
                }
            }

            var (fromBase, maxDigits, signThreshold, modulus, toBase, upper) = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Bin2Hex => (2, 10, 512L, 1024L, 16, true),
                ConditionalFormulaScalarFunctionKind.Bin2Oct => (2, 10, 512L, 1024L, 8, false),
                ConditionalFormulaScalarFunctionKind.Hex2Bin => (16, 10, 549755813888L, 1099511627776L, 2, false),
                ConditionalFormulaScalarFunctionKind.Hex2Oct => (16, 10, 549755813888L, 1099511627776L, 8, false),
                ConditionalFormulaScalarFunctionKind.Oct2Bin => (8, 10, 536870912L, 1073741824L, 2, false),
                ConditionalFormulaScalarFunctionKind.Oct2Hex => (8, 10, 536870912L, 1073741824L, 16, true),
                _ => (0, 0, 0L, 0L, 0, false)
            };

            if (!TryParseFormulaBaseNumber(source, fromBase, maxDigits, signThreshold, modulus, out var number))
            {
                value = ErrorValue.Num;
                return true;
            }

            if (number < 0)
            {
                value = new TextValue(DecimalToFormulaBaseText(
                    number,
                    toBase,
                    FormulaNegativeModulusForBase(toBase),
                    10,
                    upper));
                return true;
            }

            if (places is not null and not BlankValue)
            {
                if (!TryFormatFormulaBaseText(number, toBase, places, upper, out var padded))
                {
                    value = ErrorValue.Num;
                    return true;
                }

                value = new TextValue(padded);
                return true;
            }

            value = new TextValue(FormulaBaseText(number, toBase, upper));
            return true;
        }

        private bool TryEvaluateFormulaDecimalToBaseFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            if (!TryGetFormulaEngineeringTruncatedInteger(source, out var number))
            {
                value = ErrorValue.Num;
                return true;
            }

            ScalarValue? places = null;
            if (function.Arguments.Count == 2)
            {
                if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out places))
                    return false;

                if (places is ErrorValue placesError)
                {
                    value = placesError;
                    return true;
                }
            }

            var (toBase, min, max, modulus, negativeWidth, upper) = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Dec2Bin => (2, -512L, 511L, 1024L, 10, false),
                ConditionalFormulaScalarFunctionKind.Dec2Hex => (16, -549755813888L, 549755813887L, 1099511627776L, 10, true),
                ConditionalFormulaScalarFunctionKind.Dec2Oct => (8, -536870912L, 536870911L, 1073741824L, 10, false),
                _ => (0, 0L, 0L, 0L, 0, false)
            };

            if (number < min || number > max)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (number < 0)
            {
                value = new TextValue(DecimalToFormulaBaseText(number, toBase, modulus, negativeWidth, upper));
                return true;
            }

            if (places is not null)
            {
                if (!TryFormatFormulaBaseText(number, toBase, places, upper, out var padded))
                {
                    value = ErrorValue.Num;
                    return true;
                }

                value = new TextValue(padded);
                return true;
            }

            value = new TextValue(FormulaBaseText(number, toBase, upper));
            return true;
        }

        private const long FormulaBaseFunctionMaxNumber = 9007199254740992L;

        private bool TryEvaluateFormulaBaseFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var numberValue))
                return false;

            if (numberValue is ErrorValue numberError)
            {
                value = numberError;
                return true;
            }

            if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out var radixValue))
                return false;

            if (radixValue is ErrorValue radixError)
            {
                value = radixError;
                return true;
            }

            ScalarValue minLengthValue = BlankValue.Instance;
            if (function.Arguments.Count > 2)
            {
                if (!TryResolveFormulaOperand(function.Arguments[2], rowOffset, colOffset, out minLengthValue))
                    return false;

                if (minLengthValue is ErrorValue minLengthError)
                {
                    value = minLengthError;
                    return true;
                }
            }

            if (!TryGetFormulaEngineeringTruncatedInteger(numberValue, out var number) ||
                !TryGetFormulaEngineeringTruncatedInteger(radixValue, out var radix) ||
                number < 0 ||
                number >= FormulaBaseFunctionMaxNumber ||
                radix is < 2 or > 36)
            {
                value = ErrorValue.Num;
                return true;
            }

            var converted = FormulaUnsignedBaseText(number, (int)radix);
            if (minLengthValue is BlankValue)
            {
                value = new TextValue(converted);
                return true;
            }

            if (!TryGetFormulaEngineeringTruncatedInteger(minLengthValue, out var minLength) ||
                minLength < 0 ||
                minLength > 255)
            {
                value = ErrorValue.Num;
                return true;
            }

            value = new TextValue(converted.PadLeft((int)Math.Max(minLength, converted.Length), '0'));
            return true;
        }

        private bool TryEvaluateFormulaDecimalFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var textValue))
                return false;

            if (textValue is ErrorValue textError)
            {
                value = textError;
                return true;
            }

            if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out var radixValue))
                return false;

            if (radixValue is ErrorValue radixError)
            {
                value = radixError;
                return true;
            }

            if (!TryGetFormulaEngineeringTruncatedInteger(radixValue, out var radix) ||
                radix is < 2 or > 36)
            {
                value = ErrorValue.Num;
                return true;
            }

            var text = FormulaBaseConversionText(textValue).Trim();
            if (text.Length == 0 || text.Length > 255)
            {
                value = ErrorValue.Num;
                return true;
            }

            double result = 0;
            foreach (var ch in text)
            {
                var digit = FormulaBase36DigitValue(ch);
                if (digit < 0 || digit >= radix)
                {
                    value = ErrorValue.Num;
                    return true;
                }

                result = result * radix + digit;
                if (result >= FormulaBaseFunctionMaxNumber)
                {
                    value = ErrorValue.Num;
                    return true;
                }
            }

            value = new NumberValue(result);
            return true;
        }

        private enum FormulaConvertUnitCategory
        {
            Weight,
            Distance,
            Time,
            Pressure,
            Force,
            Energy,
            Power,
            Area,
            Volume,
            Speed,
            Information,
            Temperature
        }

        private static readonly CultureInfo FormulaConvertTextNumberCulture = CultureInfo.GetCultureInfo("en-US");

        private static readonly Dictionary<string, (FormulaConvertUnitCategory Cat, double Factor)> FormulaConvertUnits =
            BuildFormulaConvertUnits();

        private static Dictionary<string, (FormulaConvertUnitCategory Cat, double Factor)> BuildFormulaConvertUnits()
        {
            var units = new Dictionary<string, (FormulaConvertUnitCategory, double)>(StringComparer.Ordinal);
            void Add(FormulaConvertUnitCategory category, string unit, double factor) =>
                units[unit] = (category, factor);

            Add(FormulaConvertUnitCategory.Weight, "g", 1);
            Add(FormulaConvertUnitCategory.Weight, "kg", 1000);
            Add(FormulaConvertUnitCategory.Weight, "lbm", 453.59237);
            Add(FormulaConvertUnitCategory.Weight, "ozm", 28.349523);
            Add(FormulaConvertUnitCategory.Weight, "grain", 0.06479891);
            Add(FormulaConvertUnitCategory.Weight, "stone", 6350.293);
            Add(FormulaConvertUnitCategory.Weight, "ton", 907184.74);
            Add(FormulaConvertUnitCategory.Weight, "uk_ton", 1016046.91);
            Add(FormulaConvertUnitCategory.Weight, "mg", 0.001);
            Add(FormulaConvertUnitCategory.Weight, "ug", 0.000001);
            Add(FormulaConvertUnitCategory.Weight, "ng", 1e-9);
            Add(FormulaConvertUnitCategory.Weight, "sg", 14593.903);
            Add(FormulaConvertUnitCategory.Weight, "cwt", 45359.237);
            Add(FormulaConvertUnitCategory.Weight, "uk_cwt", 50802.345);

            Add(FormulaConvertUnitCategory.Distance, "m", 1);
            Add(FormulaConvertUnitCategory.Distance, "km", 1000);
            Add(FormulaConvertUnitCategory.Distance, "mi", 1609.344);
            Add(FormulaConvertUnitCategory.Distance, "survey_mi", 1609.347218694);
            Add(FormulaConvertUnitCategory.Distance, "Nmi", 1852);
            Add(FormulaConvertUnitCategory.Distance, "in", 0.0254);
            Add(FormulaConvertUnitCategory.Distance, "ft", 0.3048);
            Add(FormulaConvertUnitCategory.Distance, "yd", 0.9144);
            Add(FormulaConvertUnitCategory.Distance, "ang", 1e-10);
            Add(FormulaConvertUnitCategory.Distance, "ell", 1.143);
            Add(FormulaConvertUnitCategory.Distance, "Pica", 0.000423333);
            Add(FormulaConvertUnitCategory.Distance, "Picapt", 0.000352777778);
            Add(FormulaConvertUnitCategory.Distance, "pica", 0.00423333333);
            Add(FormulaConvertUnitCategory.Distance, "cm", 0.01);
            Add(FormulaConvertUnitCategory.Distance, "mm", 0.001);
            Add(FormulaConvertUnitCategory.Distance, "um", 1e-6);
            Add(FormulaConvertUnitCategory.Distance, "nm", 1e-9);
            Add(FormulaConvertUnitCategory.Distance, "ly", 9.4607304725808e15);
            Add(FormulaConvertUnitCategory.Distance, "au", 149597870700.0);
            Add(FormulaConvertUnitCategory.Distance, "pc", 3.085677581491367e16);
            Add(FormulaConvertUnitCategory.Distance, "parsec", 3.085677581491367e16);

            Add(FormulaConvertUnitCategory.Time, "sec", 1);
            Add(FormulaConvertUnitCategory.Time, "s", 1);
            Add(FormulaConvertUnitCategory.Time, "min", 60);
            Add(FormulaConvertUnitCategory.Time, "mn", 60);
            Add(FormulaConvertUnitCategory.Time, "hr", 3600);
            Add(FormulaConvertUnitCategory.Time, "day", 86400);
            Add(FormulaConvertUnitCategory.Time, "d", 86400);
            Add(FormulaConvertUnitCategory.Time, "yr", 31557600);

            Add(FormulaConvertUnitCategory.Pressure, "Pa", 1);
            Add(FormulaConvertUnitCategory.Pressure, "p", 1);
            Add(FormulaConvertUnitCategory.Pressure, "atm", 101325);
            Add(FormulaConvertUnitCategory.Pressure, "at", 101325);
            Add(FormulaConvertUnitCategory.Pressure, "mmHg", 133.322);
            Add(FormulaConvertUnitCategory.Pressure, "psi", 6894.757);
            Add(FormulaConvertUnitCategory.Pressure, "Torr", 133.322);

            Add(FormulaConvertUnitCategory.Force, "N", 1);
            Add(FormulaConvertUnitCategory.Force, "dyn", 1e-5);
            Add(FormulaConvertUnitCategory.Force, "lbf", 4.44822);
            Add(FormulaConvertUnitCategory.Force, "pond", 0.00980665);

            Add(FormulaConvertUnitCategory.Energy, "J", 1);
            Add(FormulaConvertUnitCategory.Energy, "kJ", 1000);
            Add(FormulaConvertUnitCategory.Energy, "e", 1e-7);
            Add(FormulaConvertUnitCategory.Energy, "c", 4.184);
            Add(FormulaConvertUnitCategory.Energy, "cal", 4.184);
            Add(FormulaConvertUnitCategory.Energy, "eV", 1.60218e-19);
            Add(FormulaConvertUnitCategory.Energy, "HPh", 2684519.54);
            Add(FormulaConvertUnitCategory.Energy, "Wh", 3600);
            Add(FormulaConvertUnitCategory.Energy, "flb", 1.35582);
            Add(FormulaConvertUnitCategory.Energy, "BTU", 1055.056);

            Add(FormulaConvertUnitCategory.Power, "W", 1);
            Add(FormulaConvertUnitCategory.Power, "kW", 1000);
            Add(FormulaConvertUnitCategory.Power, "HP", 745.69987);
            Add(FormulaConvertUnitCategory.Power, "PS", 735.49875);

            Add(FormulaConvertUnitCategory.Temperature, "C", double.NaN);
            Add(FormulaConvertUnitCategory.Temperature, "F", double.NaN);
            Add(FormulaConvertUnitCategory.Temperature, "K", double.NaN);
            Add(FormulaConvertUnitCategory.Temperature, "Rank", double.NaN);
            Add(FormulaConvertUnitCategory.Temperature, "Reau", double.NaN);

            Add(FormulaConvertUnitCategory.Area, "m2", 1);
            Add(FormulaConvertUnitCategory.Area, "m^2", 1);
            Add(FormulaConvertUnitCategory.Area, "km2", 1e6);
            Add(FormulaConvertUnitCategory.Area, "km^2", 1e6);
            Add(FormulaConvertUnitCategory.Area, "mi2", 2589988.11);
            Add(FormulaConvertUnitCategory.Area, "mi^2", 2589988.11);
            Add(FormulaConvertUnitCategory.Area, "ft2", 0.092903);
            Add(FormulaConvertUnitCategory.Area, "ft^2", 0.092903);
            Add(FormulaConvertUnitCategory.Area, "in2", 0.000645);
            Add(FormulaConvertUnitCategory.Area, "in^2", 0.000645);
            Add(FormulaConvertUnitCategory.Area, "yd2", 0.836127);
            Add(FormulaConvertUnitCategory.Area, "yd^2", 0.836127);
            Add(FormulaConvertUnitCategory.Area, "ha", 10000);
            Add(FormulaConvertUnitCategory.Area, "acre", 4046.856);

            Add(FormulaConvertUnitCategory.Volume, "l", 1);
            Add(FormulaConvertUnitCategory.Volume, "L", 1);
            Add(FormulaConvertUnitCategory.Volume, "tsp", 0.00492892);
            Add(FormulaConvertUnitCategory.Volume, "tbs", 0.0147868);
            Add(FormulaConvertUnitCategory.Volume, "oz", 0.0295735);
            Add(FormulaConvertUnitCategory.Volume, "cup", 0.236588);
            Add(FormulaConvertUnitCategory.Volume, "pt", 0.473176);
            Add(FormulaConvertUnitCategory.Volume, "qt", 0.946353);
            Add(FormulaConvertUnitCategory.Volume, "gal", 3.785412);
            Add(FormulaConvertUnitCategory.Volume, "m3", 1000);
            Add(FormulaConvertUnitCategory.Volume, "m^3", 1000);
            Add(FormulaConvertUnitCategory.Volume, "mi3", 4168181825441);
            Add(FormulaConvertUnitCategory.Volume, "mi^3", 4168181825441);
            Add(FormulaConvertUnitCategory.Volume, "ft3", 28.3168);
            Add(FormulaConvertUnitCategory.Volume, "ft^3", 28.3168);
            Add(FormulaConvertUnitCategory.Volume, "in3", 0.0163871);
            Add(FormulaConvertUnitCategory.Volume, "in^3", 0.0163871);
            Add(FormulaConvertUnitCategory.Volume, "yd3", 764.555);
            Add(FormulaConvertUnitCategory.Volume, "yd^3", 764.555);
            Add(FormulaConvertUnitCategory.Volume, "ml", 0.001);
            Add(FormulaConvertUnitCategory.Volume, "cl", 0.01);
            Add(FormulaConvertUnitCategory.Volume, "dl", 0.1);
            Add(FormulaConvertUnitCategory.Volume, "Nmi3", 6352182208);
            Add(FormulaConvertUnitCategory.Volume, "Nmi^3", 6352182208);

            Add(FormulaConvertUnitCategory.Speed, "m/s", 1);
            Add(FormulaConvertUnitCategory.Speed, "m/h", 1.0 / 3600);
            Add(FormulaConvertUnitCategory.Speed, "mph", 0.44704);
            Add(FormulaConvertUnitCategory.Speed, "kn", 0.514444);

            Add(FormulaConvertUnitCategory.Information, "bit", 1);
            Add(FormulaConvertUnitCategory.Information, "byte", 8);
            Add(FormulaConvertUnitCategory.Information, "kbit", 1000);
            Add(FormulaConvertUnitCategory.Information, "kbyte", 8000);
            Add(FormulaConvertUnitCategory.Information, "Mbit", 1e6);
            Add(FormulaConvertUnitCategory.Information, "Mbyte", 8e6);
            Add(FormulaConvertUnitCategory.Information, "Gbit", 1e9);
            Add(FormulaConvertUnitCategory.Information, "Gbyte", 8e9);
            Add(FormulaConvertUnitCategory.Information, "Tbit", 1e12);
            Add(FormulaConvertUnitCategory.Information, "Tbyte", 8e12);

            return units;
        }

        private static readonly Dictionary<string, double> FormulaConvertPrefixes = new(StringComparer.Ordinal)
        {
            ["Y"] = 1e24,
            ["Z"] = 1e21,
            ["E"] = 1e18,
            ["P"] = 1e15,
            ["T"] = 1e12,
            ["G"] = 1e9,
            ["M"] = 1e6,
            ["k"] = 1e3,
            ["h"] = 1e2,
            ["da"] = 1e1,
            ["e"] = 1e1,
            ["d"] = 1e-1,
            ["c"] = 1e-2,
            ["m"] = 1e-3,
            ["u"] = 1e-6,
            ["n"] = 1e-9,
            ["p"] = 1e-12,
            ["f"] = 1e-15,
            ["a"] = 1e-18,
            ["z"] = 1e-21,
            ["y"] = 1e-24
        };

        private static readonly Dictionary<string, double> FormulaConvertBinaryPrefixes = new(StringComparer.Ordinal)
        {
            ["Yi"] = Math.Pow(2, 80),
            ["Zi"] = Math.Pow(2, 70),
            ["Ei"] = Math.Pow(2, 60),
            ["Pi"] = Math.Pow(2, 50),
            ["Ti"] = Math.Pow(2, 40),
            ["Gi"] = Math.Pow(2, 30),
            ["Mi"] = Math.Pow(2, 20),
            ["ki"] = Math.Pow(2, 10)
        };

        private bool TryEvaluateFormulaConvertFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var numberValue))
                return false;

            if (numberValue is ErrorValue numberError)
            {
                value = numberError;
                return true;
            }

            if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out var fromValue))
                return false;

            if (fromValue is ErrorValue fromError)
            {
                value = fromError;
                return true;
            }

            if (!TryResolveFormulaOperand(function.Arguments[2], rowOffset, colOffset, out var toValue))
                return false;

            if (toValue is ErrorValue toError)
            {
                value = toError;
                return true;
            }

            if (!TryGetFormulaConvertNumber(numberValue, out var number))
            {
                value = ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(number))
            {
                value = ErrorValue.Num;
                return true;
            }

            value = EvaluateFormulaConvert(number, FormulaConvertText(fromValue), FormulaConvertText(toValue));
            return true;
        }

        private bool TryEvaluateFormulaComplexFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (function.Kind == ConditionalFormulaScalarFunctionKind.Complex)
                return TryEvaluateFormulaComplexConstructor(function, rowOffset, colOffset, out value);

            if (function.Kind is ConditionalFormulaScalarFunctionKind.ImSum or
                ConditionalFormulaScalarFunctionKind.ImProduct)
            {
                return TryEvaluateFormulaComplexAggregateFunction(function, rowOffset, colOffset, out value);
            }

            if (function.Kind is ConditionalFormulaScalarFunctionKind.ImSub or
                ConditionalFormulaScalarFunctionKind.ImDiv or
                ConditionalFormulaScalarFunctionKind.ImPower)
            {
                return TryEvaluateFormulaComplexBinaryFunction(function, rowOffset, colOffset, out value);
            }

            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            var parsed = ParseFormulaComplexArgument(source);
            if (parsed.Error is not null)
            {
                value = parsed.Error;
                return true;
            }

            value = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.ImReal => new NumberValue(parsed.Real),
                ConditionalFormulaScalarFunctionKind.Imaginary => new NumberValue(parsed.Imaginary),
                ConditionalFormulaScalarFunctionKind.ImAbs => FormulaComplexNumberResult(
                    Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary)),
                ConditionalFormulaScalarFunctionKind.ImArgument => EvaluateFormulaComplexArgument(parsed),
                ConditionalFormulaScalarFunctionKind.ImConjugate =>
                    FormulaComplexTextResult(parsed.Real, -parsed.Imaginary, parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImCos => FormulaComplexTextResult(
                    Math.Cos(parsed.Real) * Math.Cosh(parsed.Imaginary),
                    -Math.Sin(parsed.Real) * Math.Sinh(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImCosh => FormulaComplexTextResult(
                    Math.Cosh(parsed.Real) * Math.Cos(parsed.Imaginary),
                    Math.Sinh(parsed.Real) * Math.Sin(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImCot =>
                    FormulaComplexCotResult(parsed.Real, parsed.Imaginary, parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImCsc => FormulaReciprocalComplexTextResult(
                    Math.Sin(parsed.Real) * Math.Cosh(parsed.Imaginary),
                    Math.Cos(parsed.Real) * Math.Sinh(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImCsch => FormulaReciprocalComplexTextResult(
                    Math.Sinh(parsed.Real) * Math.Cos(parsed.Imaginary),
                    Math.Cosh(parsed.Real) * Math.Sin(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImExp => EvaluateFormulaComplexExp(parsed),
                ConditionalFormulaScalarFunctionKind.ImLn => EvaluateFormulaComplexLog(parsed, 1.0),
                ConditionalFormulaScalarFunctionKind.ImLog10 => EvaluateFormulaComplexLog(parsed, Math.Log(10.0)),
                ConditionalFormulaScalarFunctionKind.ImLog2 => EvaluateFormulaComplexLog(parsed, Math.Log(2.0)),
                ConditionalFormulaScalarFunctionKind.ImSin => FormulaComplexTextResult(
                    Math.Sin(parsed.Real) * Math.Cosh(parsed.Imaginary),
                    Math.Cos(parsed.Real) * Math.Sinh(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImSinh => FormulaComplexTextResult(
                    Math.Sinh(parsed.Real) * Math.Cos(parsed.Imaginary),
                    Math.Cosh(parsed.Real) * Math.Sin(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImSec => FormulaReciprocalComplexTextResult(
                    Math.Cos(parsed.Real) * Math.Cosh(parsed.Imaginary),
                    -Math.Sin(parsed.Real) * Math.Sinh(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImSech => FormulaReciprocalComplexTextResult(
                    Math.Cosh(parsed.Real) * Math.Cos(parsed.Imaginary),
                    Math.Sinh(parsed.Real) * Math.Sin(parsed.Imaginary),
                    parsed.Suffix),
                ConditionalFormulaScalarFunctionKind.ImSqrt => EvaluateFormulaComplexSqrt(parsed),
                ConditionalFormulaScalarFunctionKind.ImTan =>
                    FormulaComplexTanResult(parsed.Real, parsed.Imaginary, parsed.Suffix),
                _ => ErrorValue.Value
            };
            return true;
        }

        private bool TryEvaluateFormulaComplexAggregateFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            double real = function.Kind == ConditionalFormulaScalarFunctionKind.ImProduct ? 1d : 0d;
            double imaginary = 0d;
            var suffix = "i";

            foreach (var argument in function.Arguments)
            {
                if (!TryEvaluateFormulaComplexAggregateArgument(
                        argument,
                        rowOffset,
                        colOffset,
                        ref real,
                        ref imaginary,
                        ref suffix,
                        function.Kind,
                        out value))
                {
                    return false;
                }

                if (value is ErrorValue)
                    return true;
            }

            value = FormulaComplexTextResult(real, imaginary, suffix);
            return true;
        }

        private bool TryEvaluateFormulaComplexAggregateArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            ref double real,
            ref double imaginary,
            ref string suffix,
            ConditionalFormulaScalarFunctionKind kind,
            out ScalarValue value)
        {
            value = BlankValue.Instance;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryResolveFormulaReferenceRange(
                        argument,
                        rowOffset,
                        colOffset,
                        out var targetSheet,
                        out var startRow,
                        out var startCol,
                        out var endRow,
                        out var endCol))
                {
                    return false;
                }

                var rowCount = (ulong)endRow - startRow + 1UL;
                var colCount = (ulong)endCol - startCol + 1UL;
                if (rowCount * colCount > MaxFormulaAggregateRangeCells)
                    return false;

                for (var currentRow = startRow; currentRow <= endRow; currentRow++)
                {
                    for (var currentCol = startCol; currentCol <= endCol; currentCol++)
                    {
                        if (!AppendFormulaComplexAggregateValue(
                                targetSheet.GetValue(currentRow, currentCol),
                                ref real,
                                ref imaginary,
                                ref suffix,
                                kind,
                                out value))
                        {
                            return true;
                        }
                    }
                }

                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var source))
                return false;

            if (source is RangeValue range)
            {
                foreach (var cell in range.Flatten())
                {
                    if (!AppendFormulaComplexAggregateValue(cell, ref real, ref imaginary, ref suffix, kind, out value))
                        return true;
                }

                return true;
            }

            AppendFormulaComplexAggregateValue(source, ref real, ref imaginary, ref suffix, kind, out value);
            return true;
        }

        private static bool AppendFormulaComplexAggregateValue(
            ScalarValue source,
            ref double real,
            ref double imaginary,
            ref string suffix,
            ConditionalFormulaScalarFunctionKind kind,
            out ScalarValue value)
        {
            value = BlankValue.Instance;
            var parsed = ParseFormulaComplexArgument(source);
            if (parsed.Error is not null)
            {
                value = parsed.Error;
                return false;
            }

            if (kind == ConditionalFormulaScalarFunctionKind.ImProduct)
            {
                var nextReal = real * parsed.Real - imaginary * parsed.Imaginary;
                var nextImaginary = real * parsed.Imaginary + imaginary * parsed.Real;
                real = nextReal;
                imaginary = nextImaginary;
            }
            else
            {
                real += parsed.Real;
                imaginary += parsed.Imaginary;
            }

            suffix = parsed.Suffix;
            return true;
        }

        private bool TryEvaluateFormulaComplexBinaryFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var leftValue))
                return false;

            if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out var rightValue))
                return false;

            if (leftValue is ErrorValue leftError)
            {
                value = leftError;
                return true;
            }

            if (function.Kind == ConditionalFormulaScalarFunctionKind.ImPower &&
                rightValue is ErrorValue exponentError)
            {
                value = exponentError;
                return true;
            }

            var left = ParseFormulaComplexArgument(leftValue);
            if (left.Error is not null)
            {
                value = left.Error;
                return true;
            }

            if (function.Kind == ConditionalFormulaScalarFunctionKind.ImPower)
            {
                value = EvaluateFormulaComplexPower(left, rightValue);
                return true;
            }

            var right = ParseFormulaComplexArgument(rightValue);
            if (right.Error is not null)
            {
                value = right.Error;
                return true;
            }

            value = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.ImSub =>
                    FormulaComplexTextResult(left.Real - right.Real, left.Imaginary - right.Imaginary, left.Suffix),
                ConditionalFormulaScalarFunctionKind.ImDiv =>
                    EvaluateFormulaComplexDivision(left, right),
                _ => ErrorValue.Value
            };
            return true;
        }

        private static ScalarValue EvaluateFormulaComplexDivision(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) left,
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) right)
        {
            var denominator = right.Real * right.Real + right.Imaginary * right.Imaginary;
            if (denominator == 0)
                return ErrorValue.Num;

            var real = (left.Real * right.Real + left.Imaginary * right.Imaginary) / denominator;
            var imaginary = (left.Imaginary * right.Real - left.Real * right.Imaginary) / denominator;
            return FormulaComplexTextResult(real, imaginary, left.Suffix);
        }

        private static ScalarValue EvaluateFormulaComplexPower(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) source,
            ScalarValue exponentValue)
        {
            if (exponentValue is ErrorValue exponentError)
                return exponentError;

            if (!TryGetFormulaComplexNumber(exponentValue, out var exponent))
                return ErrorValue.Value;

            var modulus = Math.Sqrt(source.Real * source.Real + source.Imaginary * source.Imaginary);
            if (modulus == 0 && exponent <= 0)
                return ErrorValue.Num;

            var magnitude = Math.Pow(modulus, exponent);
            var angle = Math.Atan2(source.Imaginary, source.Real) * exponent;
            if (!double.IsFinite(magnitude) || !double.IsFinite(angle))
                return ErrorValue.Num;

            return FormulaComplexTextResult(
                magnitude * Math.Cos(angle),
                magnitude * Math.Sin(angle),
                source.Suffix);
        }

        private bool TryEvaluateFormulaComplexConstructor(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var realValue))
                return false;

            if (realValue is ErrorValue realError)
            {
                value = realError;
                return true;
            }

            if (!TryResolveFormulaOperand(function.Arguments[1], rowOffset, colOffset, out var imaginaryValue))
                return false;

            if (imaginaryValue is ErrorValue imaginaryError)
            {
                value = imaginaryError;
                return true;
            }

            ScalarValue suffixValue = BlankValue.Instance;
            if (function.Arguments.Count > 2)
            {
                if (!TryResolveFormulaOperand(function.Arguments[2], rowOffset, colOffset, out suffixValue))
                    return false;

                if (suffixValue is ErrorValue suffixError)
                {
                    value = suffixError;
                    return true;
                }
            }

            var suffix = suffixValue is not BlankValue
                ? FormulaComplexText(suffixValue).ToLowerInvariant()
                : "i";
            if (suffix is not ("i" or "j"))
            {
                value = ErrorValue.Value;
                return true;
            }

            if (!TryGetFormulaComplexNumber(realValue, out var real) ||
                !TryGetFormulaComplexNumber(imaginaryValue, out var imaginary))
            {
                value = ErrorValue.Value;
                return true;
            }

            value = FormulaComplexTextResult(real, imaginary, suffix);
            return true;
        }

        private static ScalarValue FormulaComplexTextResult(double real, double imaginary, string suffix) =>
            double.IsFinite(real) && double.IsFinite(imaginary)
                ? new TextValue(FormatFormulaComplex(real, imaginary, suffix))
                : ErrorValue.Num;

        private static ScalarValue FormulaComplexNumberResult(double value) =>
            double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

        private static ScalarValue EvaluateFormulaComplexArgument(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) parsed)
        {
            if (parsed.Real == 0 && parsed.Imaginary == 0)
                return ErrorValue.DivByZero;

            return FormulaComplexNumberResult(Math.Atan2(parsed.Imaginary, parsed.Real));
        }

        private static ScalarValue EvaluateFormulaComplexExp(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) parsed)
        {
            var magnitude = Math.Exp(parsed.Real);
            return FormulaComplexTextResult(
                magnitude * Math.Cos(parsed.Imaginary),
                magnitude * Math.Sin(parsed.Imaginary),
                parsed.Suffix);
        }

        private static ScalarValue EvaluateFormulaComplexLog(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) parsed,
            double divisor)
        {
            var modulus = Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary);
            if (modulus == 0)
                return ErrorValue.Num;

            var angle = Math.Atan2(parsed.Imaginary, parsed.Real);
            return FormulaComplexTextResult(Math.Log(modulus) / divisor, angle / divisor, parsed.Suffix);
        }

        private static ScalarValue EvaluateFormulaComplexSqrt(
            (double Real, double Imaginary, string Suffix, ErrorValue? Error) parsed)
        {
            var modulus = Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary);
            var real = Math.Sqrt((modulus + parsed.Real) / 2.0);
            var imaginary = Math.CopySign(
                Math.Sqrt(Math.Max(0.0, (modulus - parsed.Real) / 2.0)),
                parsed.Imaginary);
            return FormulaComplexTextResult(real, imaginary, parsed.Suffix);
        }

        private static ScalarValue FormulaComplexTanResult(double real, double imaginary, string suffix)
        {
            var denominator = Math.Cos(2.0 * real) + Math.Cosh(2.0 * imaginary);
            if (denominator == 0)
                return ErrorValue.Num;

            return FormulaComplexTextResult(
                Math.Sin(2.0 * real) / denominator,
                Math.Sinh(2.0 * imaginary) / denominator,
                suffix);
        }

        private static ScalarValue FormulaComplexCotResult(double real, double imaginary, string suffix)
        {
            var tanDenominator = Math.Cos(2.0 * real) + Math.Cosh(2.0 * imaginary);
            if (tanDenominator == 0)
                return ErrorValue.Num;

            var tanReal = Math.Sin(2.0 * real) / tanDenominator;
            var tanImaginary = Math.Sinh(2.0 * imaginary) / tanDenominator;
            return FormulaReciprocalComplexTextResult(tanReal, tanImaginary, suffix);
        }

        private static ScalarValue FormulaReciprocalComplexTextResult(double real, double imaginary, string suffix)
        {
            var denominator = real * real + imaginary * imaginary;
            if (denominator == 0)
                return ErrorValue.Num;

            return FormulaComplexTextResult(real / denominator, -imaginary / denominator, suffix);
        }

        private static (double Real, double Imaginary, string Suffix, ErrorValue? Error) ParseFormulaComplexArgument(
            ScalarValue value)
        {
            if (value is ErrorValue error)
                return (0, 0, "i", error);

            if (value is BoolValue)
                return (0, 0, "i", ErrorValue.Value);

            if (TryGetFormulaComplexCellNumber(value, out var number))
            {
                return double.IsFinite(number)
                    ? (number, 0, "i", null)
                    : (0, 0, "i", ErrorValue.Num);
            }

            var text = FormulaComplexText(value).Trim();
            if (text.Length == 0)
                return (0, 0, "i", ErrorValue.Num);

            var suffix = text[^1].ToString().ToLowerInvariant();
            if (suffix is not ("i" or "j"))
            {
                return TryParseFormulaComplexNumber(text, out var realOnly)
                    ? (realOnly, 0, "i", null)
                    : (0, 0, "i", ErrorValue.Num);
            }

            var body = text[..^1];
            TrySplitFormulaComplexBody(body, out var realPart, out var imaginaryPart);
            if (!TryParseFormulaComplexNumber(realPart, out var real) ||
                !TryParseFormulaImaginaryCoefficient(imaginaryPart, out var imaginary))
            {
                return (0, 0, suffix, ErrorValue.Num);
            }

            return (real, imaginary, suffix, null);
        }

        private static void TrySplitFormulaComplexBody(
            string body,
            out string realPart,
            out string imaginaryPart)
        {
            realPart = "0";
            imaginaryPart = body;
            if (body.Length == 0 || body is "+" or "-")
                return;

            for (var i = body.Length - 1; i > 0; i--)
            {
                if ((body[i] == '+' || body[i] == '-') && body[i - 1] is not ('e' or 'E'))
                {
                    realPart = body[..i];
                    imaginaryPart = body[i..];
                    return;
                }
            }
        }

        private static bool TryParseFormulaComplexNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);

        private static bool TryParseFormulaImaginaryCoefficient(string text, out double value)
        {
            if (text.Length == 0 || text == "+")
            {
                value = 1;
                return true;
            }

            if (text == "-")
            {
                value = -1;
                return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                double.IsFinite(value);
        }

        private static string FormatFormulaComplex(double real, double imaginary, string suffix)
        {
            if (Math.Abs(real) < 1e-14)
                real = 0;

            if (Math.Abs(imaginary) < 1e-14)
                imaginary = 0;

            if (real == 0 && imaginary == 0)
                return "0";

            if (imaginary == 0)
                return FormatFormulaComplexNumber(real);

            var coefficient = Math.Abs(imaginary) == 1
                ? string.Empty
                : FormatFormulaComplexNumber(Math.Abs(imaginary));
            var imaginaryText = coefficient + suffix;
            if (real == 0)
                return imaginary < 0 ? "-" + imaginaryText : imaginaryText;

            return FormatFormulaComplexNumber(real) + (imaginary < 0 ? "-" : "+") + imaginaryText;
        }

        private static string FormatFormulaComplexNumber(double value) =>
            value.ToString("G15", CultureInfo.InvariantCulture);

        private static bool TryGetFormulaComplexNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    return true;
                case BoolValue boolean:
                    number = boolean.Value ? 1d : 0d;
                    return true;
                case BlankValue:
                    number = 0d;
                    return true;
                case TextValue text:
                    return TryParseFormulaConvertNumberText(text.Value, out number);
                default:
                    number = 0;
                    return false;
            }
        }

        private static bool TryGetFormulaComplexCellNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        private static string FormulaComplexText(ScalarValue value) =>
            value switch
            {
                TextValue text => text.Value,
                NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                ErrorValue error => error.Code,
                _ => value.ToString() ?? string.Empty
            };

        private static ScalarValue EvaluateFormulaConvert(double number, string from, string to)
        {
            if (!TryResolveFormulaConvertUnit(from, out var fromCategory, out var fromFactor) ||
                !TryResolveFormulaConvertUnit(to, out var toCategory, out var toFactor) ||
                fromCategory != toCategory)
            {
                return ErrorValue.NA;
            }

            if (fromCategory == FormulaConvertUnitCategory.Temperature)
            {
                var kelvin = from switch
                {
                    "C" => number + 273.15,
                    "F" => (number - 32) * 5.0 / 9.0 + 273.15,
                    "K" => number,
                    "Rank" => number * 5.0 / 9.0,
                    "Reau" => number * 5.0 / 4.0 + 273.15,
                    _ => double.NaN
                };
                if (!double.IsFinite(kelvin))
                    return ErrorValue.NA;

                var result = to switch
                {
                    "C" => kelvin - 273.15,
                    "F" => (kelvin - 273.15) * 9.0 / 5.0 + 32,
                    "K" => kelvin,
                    "Rank" => kelvin * 9.0 / 5.0,
                    "Reau" => (kelvin - 273.15) * 4.0 / 5.0,
                    _ => double.NaN
                };
                return double.IsFinite(result) ? new NumberValue(result) : ErrorValue.NA;
            }

            var converted = number * fromFactor / toFactor;
            return double.IsFinite(converted) ? new NumberValue(converted) : ErrorValue.Num;
        }

        private static bool TryResolveFormulaConvertUnit(
            string unit,
            out FormulaConvertUnitCategory category,
            out double factor)
        {
            if (FormulaConvertUnits.TryGetValue(unit, out var entry))
            {
                category = entry.Cat;
                factor = entry.Factor;
                return true;
            }

            if (TryResolveFormulaConvertBinaryPrefixedUnit(unit, out category, out factor))
                return true;

            if (TryResolveFormulaConvertPrefixedUnit(unit, 2, out category, out factor))
                return true;

            if (TryResolveFormulaConvertPrefixedUnit(unit, 1, out category, out factor))
                return true;

            category = default;
            factor = 0;
            return false;
        }

        private static bool TryResolveFormulaConvertBinaryPrefixedUnit(
            string unit,
            out FormulaConvertUnitCategory category,
            out double factor)
        {
            if (unit.Length > 2)
            {
                var prefix = unit[..2];
                var rest = unit[2..];
                if (FormulaConvertBinaryPrefixes.TryGetValue(prefix, out var prefixFactor) &&
                    FormulaConvertUnits.TryGetValue(rest, out var entry) &&
                    entry.Cat == FormulaConvertUnitCategory.Information)
                {
                    category = entry.Cat;
                    factor = entry.Factor * prefixFactor;
                    return true;
                }
            }

            category = default;
            factor = 0;
            return false;
        }

        private static bool TryResolveFormulaConvertPrefixedUnit(
            string unit,
            int prefixLength,
            out FormulaConvertUnitCategory category,
            out double factor)
        {
            if (unit.Length > prefixLength)
            {
                var prefix = unit[..prefixLength];
                var rest = unit[prefixLength..];
                if (FormulaConvertPrefixes.TryGetValue(prefix, out var prefixFactor) &&
                    FormulaConvertUnits.TryGetValue(rest, out var entry) &&
                    entry.Cat != FormulaConvertUnitCategory.Temperature)
                {
                    category = entry.Cat;
                    factor = entry.Factor * prefixFactor;
                    return true;
                }
            }

            category = default;
            factor = 0;
            return false;
        }

        private static bool TryGetFormulaConvertNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    return true;
                case BoolValue boolean:
                    number = boolean.Value ? 1d : 0d;
                    return true;
                case BlankValue:
                    number = 0d;
                    return true;
                case TextValue text:
                    return TryParseFormulaConvertNumberText(text.Value, out number);
                default:
                    number = 0;
                    return false;
            }
        }

        private static bool TryParseFormulaConvertNumberText(string text, out double number)
        {
            var candidate = text.Trim();

            var percentCount = 0;
            while (candidate.EndsWith('%'))
            {
                percentCount++;
                candidate = candidate[..^1].TrimEnd();
            }

            if (percentCount > 0 &&
                double.TryParse(candidate, NumberStyles.Any, FormulaConvertTextNumberCulture, out number))
            {
                for (var i = 0; i < percentCount; i++)
                    number /= 100d;

                return true;
            }

            if (double.TryParse(candidate, NumberStyles.Any, FormulaConvertTextNumberCulture, out number))
                return true;

            if (TryParseFormulaExcelFakeLeapDayValueText(candidate, out number))
                return true;

            if (DateTime.TryParse(candidate, FormulaConvertTextNumberCulture, DateTimeStyles.None, out var dateTime))
            {
                number = IsFormulaConvertTimeOnlyText(candidate)
                    ? dateTime.TimeOfDay.TotalDays
                    : FormulaDateToExcelSerial(dateTime);
                return true;
            }

            number = 0;
            return false;
        }

        private static bool IsFormulaConvertTimeOnlyText(string text) =>
            !text.Contains('/') &&
            !text.Contains('-') &&
            !FormulaDateTimeTextHasMonthNameRegex.IsMatch(text) &&
            (text.Contains(':') || FormulaDateTimeTextHasAmPmRegex.IsMatch(text));

        private static string FormulaConvertText(ScalarValue value) =>
            value switch
            {
                TextValue text => text.Value,
                NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                ErrorValue error => error.Code,
                _ => value.ToString() ?? string.Empty
            };

        private static bool TryParseFormulaBaseNumber(
            ScalarValue source,
            int fromBase,
            int maxDigits,
            long signThreshold,
            long modulus,
            out long value)
        {
            value = 0;
            var text = FormulaBaseConversionText(source).Trim();
            if (text.Length == 0 || text.Length > maxDigits)
                return false;

            foreach (var ch in text)
            {
                var digit = ch switch
                {
                    >= '0' and <= '9' => ch - '0',
                    >= 'A' and <= 'F' => ch - 'A' + 10,
                    >= 'a' and <= 'f' => ch - 'a' + 10,
                    _ => -1
                };
                if (digit < 0 || digit >= fromBase)
                    return false;

                value = value * fromBase + digit;
            }

            if (text.Length == maxDigits && value >= signThreshold)
                value -= modulus;

            return true;
        }

        private static bool TryFormatFormulaBaseText(
            long number,
            int toBase,
            ScalarValue placesValue,
            bool upper,
            out string text)
        {
            text = FormulaBaseText(number, toBase, upper);
            if (!TryGetFormulaEngineeringTruncatedInteger(placesValue, out var places) ||
                places < 0 ||
                places > 255 ||
                places < text.Length)
            {
                return false;
            }

            text = text.PadLeft((int)places, '0');
            return true;
        }

        private static string DecimalToFormulaBaseText(
            long number,
            int toBase,
            long modulus,
            int width,
            bool upper) =>
            FormulaBaseText(number < 0 ? modulus + number : number, toBase, upper).PadLeft(width, '0');

        private static long FormulaNegativeModulusForBase(int toBase) => toBase switch
        {
            2 => 1024L,
            8 => 1073741824L,
            16 => 1099511627776L,
            _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
        };

        private static string FormulaBaseText(long number, int toBase, bool upper)
        {
            var text = System.Convert.ToString(number, toBase);
            return upper ? text.ToUpperInvariant() : text;
        }

        private static string FormulaUnsignedBaseText(long number, int radix)
        {
            const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (number == 0)
                return "0";

            Span<char> buffer = stackalloc char[64];
            var index = buffer.Length;
            var current = number;
            while (current > 0)
            {
                buffer[--index] = digits[(int)(current % radix)];
                current /= radix;
            }

            return new string(buffer[index..]);
        }

        private static int FormulaBase36DigitValue(char ch) => ch switch
        {
            >= '0' and <= '9' => ch - '0',
            >= 'A' and <= 'Z' => ch - 'A' + 10,
            >= 'a' and <= 'z' => ch - 'a' + 10,
            _ => -1
        };

        private static string FormulaBaseConversionText(ScalarValue value) =>
            value switch
            {
                TextValue text => text.Value,
                NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                ErrorValue error => error.Code,
                _ => value.ToString() ?? string.Empty
            };

        private static bool TryGetFormulaEngineeringTruncatedInteger(ScalarValue value, out long integer)
        {
            integer = 0;
            if (!TryGetFormulaBaseConversionNumber(value, out var number) ||
                !double.IsFinite(number))
            {
                return false;
            }

            var truncated = Math.Truncate(number);
            if (truncated < long.MinValue || truncated > long.MaxValue)
                return false;

            integer = (long)truncated;
            return true;
        }

        private static bool TryGetFormulaBaseConversionNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    return true;
                case BoolValue boolean:
                    number = boolean.Value ? 1d : 0d;
                    return true;
                case BlankValue:
                    number = 0d;
                    return true;
                case TextValue text:
                    return TryParseFormulaValueText(text.Value, out number);
                default:
                    number = 0d;
                    return false;
            }
        }

        private bool TryEvaluateFormulaArabicFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source) ||
                source is not TextValue textValue)
            {
                return false;
            }

            var text = textValue.Value.Trim();
            if (text.Length == 0)
            {
                value = new NumberValue(0);
                return true;
            }

            if (text.Length > 255)
                return false;

            var negative = text[0] == '-';
            if (negative)
            {
                text = text[1..].TrimStart();
                if (text.Length == 0 || text.Length > 255)
                    return false;
            }

            if (!TryParseFormulaArabicRoman(text, out var result))
                return false;

            value = new NumberValue(negative ? -result : result);
            return true;
        }

        private bool TryEvaluateFormulaRomanFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionNumber(function.Arguments[0], rowOffset, colOffset, out var number) ||
                !double.IsFinite(number))
            {
                return false;
            }

            var form = 0;
            if (function.Arguments.Count == 2 &&
                !TryResolveFormulaRomanForm(function.Arguments[1], rowOffset, colOffset, out form))
            {
                return false;
            }

            var truncated = (int)Math.Truncate(number);
            if (truncated is < 0 or > 3999)
                return false;

            value = new TextValue(ToFormulaRoman(truncated, form));
            return true;
        }

        private bool TryEvaluateFormulaUnicharFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            if (!TryGetFormulaCoercedNumber(source, out var number) ||
                number < int.MinValue ||
                number > int.MaxValue)
            {
                value = ErrorValue.Value;
                return true;
            }

            var codePoint = (int)Math.Truncate(number);
            if (codePoint <= 0 ||
                codePoint > 0x10FFFF ||
                codePoint is >= 0xD800 and <= 0xDFFF)
            {
                value = ErrorValue.Value;
                return true;
            }

            value = new TextValue(char.ConvertFromUtf32(codePoint));
            return true;
        }

        private bool TryEvaluateFormulaUnicodeFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            if (!TryGetFormulaCoercedText(source, out var text))
                return false;

            if (text.Length == 0)
            {
                value = ErrorValue.Value;
                return true;
            }

            if (char.IsHighSurrogate(text[0]))
            {
                if (text.Length < 2 || !char.IsLowSurrogate(text[1]))
                {
                    value = ErrorValue.Value;
                    return true;
                }

                value = new NumberValue(char.ConvertToUtf32(text[0], text[1]));
                return true;
            }

            if (char.IsLowSurrogate(text[0]))
            {
                value = ErrorValue.Value;
                return true;
            }

            value = new NumberValue(text[0]);
            return true;
        }

        private bool TryEvaluateFormulaTextScalarUnaryFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            Func<ScalarValue, ScalarValue> map,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            value = source is RangeValue range
                ? MapFormulaTextScalarRange(range, map)
                : map(source);
            return true;
        }

        private bool TryEvaluateFormulaReptFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var textSource))
                return false;

            if (textSource is ErrorValue textError)
            {
                value = textError;
                return true;
            }

            if (!TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out var repeatSource))
                return false;

            if (repeatSource is ErrorValue repeatError)
            {
                value = repeatError;
                return true;
            }

            value = MapFormulaTextScalarBinaryArguments(textSource, repeatSource, FormulaReptScalar);
            return true;
        }

        private bool TryEvaluateFormulaConcatFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            bool concatenate,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArguments(function.Arguments, rowOffset, colOffset, out var arguments))
                return false;

            value = concatenate ? FormulaConcatenate(arguments) : FormulaConcat(arguments);
            return true;
        }

        private bool TryEvaluateFormulaTextJoinFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArguments(function.Arguments, rowOffset, colOffset, out var arguments))
                return false;

            value = FormulaTextJoin(arguments);
            return true;
        }

        private bool TryEvaluateFormulaSubstituteFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[4];
            for (var i = 0; i < 3; i++)
            {
                if (!TryResolveFormulaTextScalarArgument(function.Arguments[i], rowOffset, colOffset, out arguments[i]))
                    return false;
            }

            if (function.Arguments.Count == 4)
            {
                if (!TryResolveFormulaTextScalarArgument(function.Arguments[3], rowOffset, colOffset, out arguments[3]))
                    return false;
            }
            else
            {
                arguments[3] = BlankValue.Instance;
            }

            value = MapFormulaTextScalarArguments(arguments, values => FormulaSubstituteScalar(
                values[0],
                values[1],
                values[2],
                values[3]));
            return true;
        }

        private bool TryEvaluateFormulaReplaceFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            bool useBytes,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArguments(function.Arguments, rowOffset, colOffset, out var arguments))
                return false;

            value = MapFormulaTextScalarArguments(arguments, values => FormulaReplaceScalar(
                values[0],
                values[1],
                values[2],
                values[3],
                useBytes));
            return true;
        }

        private bool TryEvaluateFormulaTextByteSliceFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var source))
                return false;

            ScalarValue countSource;
            if (function.Arguments.Count == 2)
            {
                if (!TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out countSource))
                    return false;
            }
            else
            {
                countSource = new NumberValue(1);
            }

            var fromRight = function.Kind == ConditionalFormulaScalarFunctionKind.RightB;
            var arguments = new ScalarValue[] { source, countSource };
            value = MapFormulaTextScalarArguments(arguments, values => FormulaByteSliceScalar(
                values[0],
                values[1],
                fromRight));
            return true;
        }

        private bool TryEvaluateFormulaTextMidBFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArguments(function.Arguments, rowOffset, colOffset, out var arguments))
                return false;

            value = MapFormulaTextScalarArguments(arguments, values => FormulaMidBScalar(
                values[0],
                values[1],
                values[2]));
            return true;
        }

        private bool TryEvaluateFormulaTextByteSearchFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var findSource) ||
                !TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out var withinSource))
            {
                return false;
            }

            ScalarValue startSource;
            if (function.Arguments.Count == 3)
            {
                if (!TryResolveFormulaTextScalarArgument(function.Arguments[2], rowOffset, colOffset, out startSource))
                    return false;
            }
            else
            {
                startSource = new NumberValue(1);
            }

            var useWildcards = function.Kind == ConditionalFormulaScalarFunctionKind.SearchB;
            var arguments = new ScalarValue[] { findSource, withinSource, startSource };
            value = MapFormulaTextScalarArguments(arguments, values => FormulaFindSearchBScalar(
                values[0],
                values[1],
                values[2],
                useWildcards));
            return true;
        }

        private bool TryResolveFormulaTextScalarArguments(
            IReadOnlyList<ConditionalFormulaOperand> operands,
            int rowOffset,
            int colOffset,
            out ScalarValue[] values)
        {
            values = new ScalarValue[operands.Count];
            for (var i = 0; i < operands.Count; i++)
            {
                if (!TryResolveFormulaTextScalarArgument(operands[i], rowOffset, colOffset, out values[i]))
                    return false;
            }

            return true;
        }

        private bool TryResolveFormulaTextScalarArgument(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.Kind != ConditionalFormulaOperandKind.ReferenceRange)
                return TryResolveFormulaOperand(operand, rowOffset, colOffset, out value);

            if (!TryResolveFormulaReferenceRange(
                    operand,
                    rowOffset,
                    colOffset,
                    out var targetSheet,
                    out var startRow,
                    out var startCol,
                    out var endRow,
                    out var endCol))
            {
                value = ErrorValue.Ref;
                return false;
            }

            var rowCount = endRow - startRow + 1;
            var colCount = endCol - startCol + 1;
            if ((ulong)rowCount * colCount > MaxFormulaAggregateRangeCells)
                return false;

            var rowCountInt = (int)rowCount;
            var colCountInt = (int)colCount;
            var cells = new ScalarValue[rowCountInt, colCountInt];
            for (var row = 0; row < rowCountInt; row++)
                for (var col = 0; col < colCountInt; col++)
                    cells[row, col] = targetSheet.GetValue(startRow + (uint)row, startCol + (uint)col);

            value = new RangeValue(cells);
            return true;
        }

        private static RangeValue MapFormulaTextScalarRange(RangeValue range, Func<ScalarValue, ScalarValue> map)
        {
            var cells = new ScalarValue[range.RowCount, range.ColCount];
            for (var row = 0; row < range.RowCount; row++)
                for (var col = 0; col < range.ColCount; col++)
                {
                    var value = range.Cells[row, col];
                    cells[row, col] = value is ErrorValue error ? error : map(value);
                }

            return new RangeValue(cells);
        }

        private static ScalarValue MapFormulaTextScalarBinaryArguments(
            ScalarValue left,
            ScalarValue right,
            Func<ScalarValue, ScalarValue, ScalarValue> map)
        {
            if (left is RangeValue leftRange && right is RangeValue rightRange)
            {
                var shape = leftRange.RowCount == 1 && leftRange.ColCount == 1 ? rightRange : leftRange;
                if (!CanBroadcastFormulaTextScalarRange(leftRange, shape.RowCount, shape.ColCount) ||
                    !CanBroadcastFormulaTextScalarRange(rightRange, shape.RowCount, shape.ColCount))
                {
                    return ErrorValue.Value;
                }

                var cells = new ScalarValue[shape.RowCount, shape.ColCount];
                for (var row = 0; row < shape.RowCount; row++)
                    for (var col = 0; col < shape.ColCount; col++)
                    {
                        cells[row, col] = map(
                            FormulaTextScalarRangeValueAt(leftRange, row, col),
                            FormulaTextScalarRangeValueAt(rightRange, row, col));
                    }

                return new RangeValue(cells);
            }

            if (left is RangeValue leftOnlyRange)
                return MapFormulaTextScalarRange(leftOnlyRange, value => map(value, right));

            if (right is RangeValue rightOnlyRange)
                return MapFormulaTextScalarRange(rightOnlyRange, value => map(left, value));

            return map(left, right);
        }

        private static ScalarValue MapFormulaTextScalarArguments(
            IReadOnlyList<ScalarValue> arguments,
            Func<IReadOnlyList<ScalarValue>, ScalarValue> map)
        {
            var ranges = new RangeValue?[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
                ranges[i] = arguments[i] as RangeValue;

            var shape = ChooseFormulaTextScalarBroadcastShape(ranges);
            if (shape is null)
                return map(arguments);

            for (var i = 0; i < ranges.Length; i++)
            {
                if (ranges[i] is { } range &&
                    !CanBroadcastFormulaTextScalarRange(range, shape.RowCount, shape.ColCount))
                {
                    return ErrorValue.Value;
                }
            }

            var cells = new ScalarValue[shape.RowCount, shape.ColCount];
            var scalarArguments = new ScalarValue[arguments.Count];
            for (var row = 0; row < shape.RowCount; row++)
            {
                for (var col = 0; col < shape.ColCount; col++)
                {
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        scalarArguments[i] = ranges[i] is { } range
                            ? FormulaTextScalarRangeValueAt(range, row, col)
                            : arguments[i];
                    }

                    cells[row, col] = map(scalarArguments);
                }
            }

            return new RangeValue(cells);
        }

        private static RangeValue? ChooseFormulaTextScalarBroadcastShape(IReadOnlyList<RangeValue?> ranges)
        {
            RangeValue? fallback = null;
            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range is null)
                    continue;

                fallback ??= range;
                if (range.RowCount != 1 || range.ColCount != 1)
                    return range;
            }

            return fallback;
        }

        private static bool CanBroadcastFormulaTextScalarRange(RangeValue range, int rows, int cols) =>
            (range.RowCount == rows && range.ColCount == cols) || (range.RowCount == 1 && range.ColCount == 1);

        private static ScalarValue FormulaTextScalarRangeValueAt(RangeValue range, int row, int col) =>
            range.RowCount == 1 && range.ColCount == 1 ? range.Cells[0, 0] : range.Cells[row, col];

        private static ScalarValue FormulaConcat(IReadOnlyList<ScalarValue> arguments)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < arguments.Count; i++)
            {
                if (!TryAppendFormulaConcatArgument(builder, arguments[i], out var error))
                    return error;
            }

            return FormulaTextScalarResult(builder.ToString());
        }

        private static ScalarValue FormulaConcatenate(IReadOnlyList<ScalarValue> arguments)
        {
            var rangeIndex = -1;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (arguments[i] is ErrorValue error)
                    return error;

                if (arguments[i] is not RangeValue)
                    continue;

                if (rangeIndex >= 0)
                    return ErrorValue.Value;

                rangeIndex = i;
            }

            if (rangeIndex >= 0)
                return FormulaMapConcatenateRange((RangeValue)arguments[rangeIndex], arguments, rangeIndex);

            return FormulaConcat(arguments);
        }

        private static RangeValue FormulaMapConcatenateRange(
            RangeValue range,
            IReadOnlyList<ScalarValue> arguments,
            int rangeIndex)
        {
            var cells = new ScalarValue[range.RowCount, range.ColCount];
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    var builder = new System.Text.StringBuilder();
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        var value = i == rangeIndex ? range.Cells[row, col] : arguments[i];
                        if (!TryAppendFormulaScalarText(builder, value, out var error))
                        {
                            cells[row, col] = error;
                            goto NextCell;
                        }
                    }

                    cells[row, col] = FormulaTextScalarResult(builder.ToString());

                NextCell:
                    ;
                }
            }

            return new RangeValue(cells);
        }

        private static bool TryAppendFormulaConcatArgument(
            System.Text.StringBuilder builder,
            ScalarValue value,
            out ScalarValue error)
        {
            error = ErrorValue.Value;
            if (value is RangeValue range)
            {
                for (var row = 0; row < range.RowCount; row++)
                {
                    for (var col = 0; col < range.ColCount; col++)
                    {
                        if (!TryAppendFormulaScalarText(builder, range.Cells[row, col], out error))
                            return false;
                    }
                }

                return true;
            }

            return TryAppendFormulaScalarText(builder, value, out error);
        }

        private static bool TryAppendFormulaScalarText(
            System.Text.StringBuilder builder,
            ScalarValue value,
            out ScalarValue error)
        {
            if (value is ErrorValue scalarError)
            {
                error = scalarError;
                return false;
            }

            if (!TryGetFormulaCoercedText(value, out var text))
            {
                error = ErrorValue.Value;
                return false;
            }

            builder.Append(text);
            error = ErrorValue.Value;
            return true;
        }

        private static ScalarValue FormulaTextJoin(IReadOnlyList<ScalarValue> arguments)
        {
            if (arguments[0] is ErrorValue delimiterError)
                return delimiterError;

            if (!TryGetFormulaScalarControlArgument(arguments[1], out var ignoreEmptyValue, out var controlError))
                return controlError;

            if (!TryGetFormulaControlBool(ignoreEmptyValue, out var ignoreEmpty))
                return ErrorValue.Value;

            if (!TryFlattenFormulaTextJoinArgument(arguments[0], out var delimiters, out var flattenError))
                return flattenError;

            var parts = new List<string>();
            for (var i = 2; i < arguments.Count; i++)
            {
                if (!TryFlattenFormulaTextJoinArgument(arguments[i], out var values, out flattenError))
                    return flattenError;

                for (var valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    if (ignoreEmpty && values[valueIndex].Length == 0)
                        continue;

                    parts.Add(values[valueIndex]);
                }
            }

            return FormulaTextScalarResult(FormulaJoinTextJoinParts(parts, delimiters));
        }

        private static bool TryFlattenFormulaTextJoinArgument(
            ScalarValue value,
            out List<string> text,
            out ErrorValue error)
        {
            text = new List<string>();
            error = ErrorValue.Value;
            if (value is ErrorValue directError)
            {
                error = directError;
                return false;
            }

            if (value is RangeValue range)
            {
                for (var row = 0; row < range.RowCount; row++)
                {
                    for (var col = 0; col < range.ColCount; col++)
                    {
                        var cell = range.Cells[row, col];
                        if (cell is ErrorValue cellError)
                        {
                            error = cellError;
                            return false;
                        }

                        if (!TryGetFormulaCoercedText(cell, out var cellText))
                            return false;

                        text.Add(cellText);
                    }
                }

                return true;
            }

            if (!TryGetFormulaCoercedText(value, out var scalarText))
                return false;

            text.Add(scalarText);
            return true;
        }

        private static string FormulaJoinTextJoinParts(IReadOnlyList<string> parts, IReadOnlyList<string> delimiters)
        {
            if (parts.Count == 0)
                return string.Empty;

            if (delimiters.Count == 0)
                return string.Concat(parts);

            var builder = new System.Text.StringBuilder(parts[0]);
            for (var i = 1; i < parts.Count; i++)
            {
                builder.Append(delimiters[(i - 1) % delimiters.Count]);
                builder.Append(parts[i]);
            }

            return builder.ToString();
        }

        private static bool TryGetFormulaScalarControlArgument(
            ScalarValue value,
            out ScalarValue scalar,
            out ErrorValue error)
        {
            scalar = ErrorValue.Value;
            error = ErrorValue.Value;
            if (value is RangeValue range)
            {
                if (range.RowCount != 1 || range.ColCount != 1)
                    return false;

                scalar = range.Cells[0, 0];
                if (scalar is ErrorValue scalarError)
                {
                    error = scalarError;
                    return false;
                }

                return true;
            }

            scalar = value;
            if (value is ErrorValue directError)
            {
                error = directError;
                return false;
            }

            return true;
        }

        private static bool TryGetFormulaControlBool(ScalarValue value, out bool result)
        {
            result = value switch
            {
                BoolValue boolean => boolean.Value,
                NumberValue number => number.Value != 0d,
                DateTimeValue dateTime => dateTime.Value != 0d,
                BlankValue => false,
                _ => false
            };

            return value is BoolValue or NumberValue or DateTimeValue or BlankValue;
        }

        private static ScalarValue FormulaCharScalar(ScalarValue value)
        {
            if (value is ErrorValue error)
                return error;

            if (!TryGetFormulaTextScalarNumber(value, out var number) || !double.IsFinite(number))
                return ErrorValue.Value;

            var code = (int)Math.Truncate(number);
            return code is <= 0 or > 255
                ? ErrorValue.Value
                : new TextValue(FormulaExcelAnsiCodeToChar(code).ToString());
        }

        private static ScalarValue FormulaCodeScalar(ScalarValue value)
        {
            if (value is ErrorValue error)
                return error;

            if (!TryGetFormulaCoercedText(value, out var text) || text.Length == 0)
                return ErrorValue.Value;

            return new NumberValue(FormulaCharToExcelAnsiCode(text[0]));
        }

        private static ScalarValue FormulaProperScalar(ScalarValue value)
        {
            if (value is ErrorValue error)
                return error;

            if (!TryGetFormulaCoercedText(value, out var text))
                return ErrorValue.Value;

            if (text.Length == 0)
                return new TextValue(string.Empty);

            var builder = new System.Text.StringBuilder(text.Length);
            var capitaliseNext = true;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || !char.IsLetter(ch))
                {
                    capitaliseNext = true;
                    builder.Append(ch);
                }
                else if (capitaliseNext)
                {
                    builder.Append(char.ToUpperInvariant(ch));
                    capitaliseNext = false;
                }
                else
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return FormulaTextScalarResult(builder.ToString());
        }

        private static ScalarValue FormulaReptScalar(ScalarValue value, ScalarValue timesValue)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (timesValue is ErrorValue timesError)
                return timesError;

            if (!TryGetFormulaCoercedText(value, out var text) ||
                !TryGetFormulaTextScalarNumber(timesValue, out var timesNumber) ||
                !double.IsFinite(timesNumber) ||
                timesNumber < 0 ||
                timesNumber > int.MaxValue)
            {
                return ErrorValue.Value;
            }

            var times = (int)timesNumber;
            return FormulaReptText(text, times);
        }

        private static ScalarValue FormulaSubstituteScalar(
            ScalarValue textValue,
            ScalarValue oldTextValue,
            ScalarValue newTextValue,
            ScalarValue instanceValue)
        {
            if (textValue is ErrorValue textError)
                return textError;

            if (oldTextValue is ErrorValue oldTextError)
                return oldTextError;

            if (newTextValue is ErrorValue newTextError)
                return newTextError;

            if (instanceValue is ErrorValue instanceError)
                return instanceError;

            if (!TryGetFormulaCoercedText(textValue, out var text) ||
                !TryGetFormulaCoercedText(oldTextValue, out var oldText) ||
                !TryGetFormulaCoercedText(newTextValue, out var newText))
            {
                return ErrorValue.Value;
            }

            int? instanceNum = null;
            if (instanceValue is not BlankValue)
            {
                if (!TryGetFormulaTextScalarNumber(instanceValue, out var rawInstanceNum) ||
                    !double.IsFinite(rawInstanceNum) ||
                    rawInstanceNum > int.MaxValue)
                {
                    return ErrorValue.Value;
                }

                var instance = (int)rawInstanceNum;
                if (instance < 1)
                    return ErrorValue.Value;

                instanceNum = instance;
            }

            return FormulaSubstituteText(text, oldText, newText, instanceNum);
        }

        private static ScalarValue FormulaSubstituteText(
            string text,
            string oldText,
            string newText,
            int? instanceNum)
        {
            if (oldText.Length == 0)
                return FormulaTextScalarResult(text);

            if (instanceNum is int instance)
            {
                var count = 0;
                var position = 0;
                while (position < text.Length)
                {
                    var index = text.IndexOf(oldText, position, StringComparison.Ordinal);
                    if (index < 0)
                        break;

                    count++;
                    if (count == instance)
                        return FormulaTextScalarResult(text[..index] + newText + text[(index + oldText.Length)..]);

                    position = index + oldText.Length;
                }

                return FormulaTextScalarResult(text);
            }

            return FormulaTextScalarResult(text.Replace(oldText, newText, StringComparison.Ordinal));
        }

        private static ScalarValue FormulaReplaceScalar(
            ScalarValue value,
            ScalarValue startValue,
            ScalarValue countValue,
            ScalarValue newTextValue,
            bool useBytes)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (startValue is ErrorValue startError)
                return startError;

            if (countValue is ErrorValue countError)
                return countError;

            if (newTextValue is ErrorValue newTextError)
                return newTextError;

            if (!TryGetFormulaCoercedText(value, out var text) ||
                !TryGetFormulaTextScalarNumber(startValue, out var rawStart) ||
                !TryGetFormulaTextScalarNumber(countValue, out var rawCount) ||
                !TryGetFormulaCoercedText(newTextValue, out var newText) ||
                !double.IsFinite(rawStart) ||
                !double.IsFinite(rawCount) ||
                rawStart > int.MaxValue ||
                rawCount > int.MaxValue)
            {
                return ErrorValue.Value;
            }

            var start = (int)rawStart;
            var count = (int)rawCount;
            if (start < 1 || count < 0)
                return ErrorValue.Value;

            return useBytes
                ? FormulaReplaceBText(text, start, count, newText)
                : FormulaReplaceText(text, start, count, newText);
        }

        private static ScalarValue FormulaReplaceText(string text, int startNum, int numChars, string newText)
        {
            var hasSurrogatePair = FormulaTextScalarContainsSurrogatePair(text);
            var length = hasSurrogatePair ? FormulaTextScalarCountTextElements(text) : text.Length;
            if (startNum > length + 1)
                return ErrorValue.Value;

            var start = hasSurrogatePair
                ? FormulaTextElementIndexFromOneBasedPosition(text, startNum)
                : Math.Min(startNum - 1, text.Length);
            var end = hasSurrogatePair
                ? FormulaAdvanceTextElements(text, start, numChars)
                : start + Math.Min(numChars, text.Length - start);
            return FormulaTextScalarResult(text[..start] + newText + text[end..]);
        }

        private static ScalarValue FormulaReplaceBText(string text, int startByte, int numBytes, string newText)
        {
            if (startByte > FormulaCountDbcsBytes(text) + 1)
                return ErrorValue.Value;

            var start = FormulaDbcsByteOffsetToUtf16Index(text, startByte - 1);
            var byteCount = FormulaCountDbcsBytes(text);
            var endByteOffset = startByte - 1 + Math.Min(numBytes, byteCount - (startByte - 1));
            var end = FormulaDbcsByteOffsetToUtf16Index(text, endByteOffset);
            return FormulaTextScalarResult(text[..start] + newText + text[end..]);
        }

        private static ScalarValue FormulaLenBScalar(ScalarValue value)
        {
            if (value is ErrorValue error)
                return error;

            return TryGetFormulaCoercedText(value, out var text)
                ? new NumberValue(FormulaCountDbcsBytes(text))
                : ErrorValue.Value;
        }

        private static ScalarValue FormulaByteSliceScalar(
            ScalarValue value,
            ScalarValue countValue,
            bool fromRight)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (countValue is ErrorValue countError)
                return countError;

            if (!TryGetFormulaCoercedText(value, out var text) ||
                !TryGetFormulaTextScalarNumber(countValue, out var rawCount) ||
                !double.IsFinite(rawCount) ||
                rawCount < 0 ||
                rawCount > int.MaxValue)
            {
                return ErrorValue.Value;
            }

            var byteCount = (int)rawCount;
            return fromRight
                ? FormulaTextScalarResult(FormulaSliceDbcsBytes(
                    text,
                    Math.Max(0, FormulaCountDbcsBytes(text) - byteCount),
                    byteCount))
                : FormulaTextScalarResult(FormulaSliceDbcsBytes(text, 0, byteCount));
        }

        private static ScalarValue FormulaMidBScalar(
            ScalarValue value,
            ScalarValue startValue,
            ScalarValue lengthValue)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (startValue is ErrorValue startError)
                return startError;

            if (lengthValue is ErrorValue lengthError)
                return lengthError;

            if (!TryGetFormulaCoercedText(value, out var text) ||
                !TryGetFormulaTextScalarNumber(startValue, out var rawStart) ||
                !TryGetFormulaTextScalarNumber(lengthValue, out var rawLength) ||
                !double.IsFinite(rawStart) ||
                !double.IsFinite(rawLength) ||
                rawStart < 1 ||
                rawLength < 0 ||
                rawStart > int.MaxValue ||
                rawLength > int.MaxValue)
            {
                return ErrorValue.Value;
            }

            return FormulaTextScalarResult(FormulaSliceDbcsBytes(text, (int)rawStart - 1, (int)rawLength));
        }

        private static ScalarValue FormulaFindSearchBScalar(
            ScalarValue findValue,
            ScalarValue withinValue,
            ScalarValue startValue,
            bool useWildcards)
        {
            if (findValue is ErrorValue findError)
                return findError;

            if (withinValue is ErrorValue withinError)
                return withinError;

            if (startValue is ErrorValue startError)
                return startError;

            if (!TryGetFormulaCoercedText(findValue, out var findText) ||
                !TryGetFormulaCoercedText(withinValue, out var withinText) ||
                !TryGetFormulaTextScalarNumber(startValue, out var rawStart) ||
                !double.IsFinite(rawStart) ||
                rawStart > int.MaxValue)
            {
                return ErrorValue.Value;
            }

            var startByte = (int)rawStart;
            if (startByte < 1)
                return ErrorValue.Value;

            return useWildcards
                ? FormulaSearchBText(findText, withinText, startByte)
                : FormulaFindBText(findText, withinText, startByte);
        }

        private static ScalarValue FormulaFindBText(string findText, string withinText, int startByte)
        {
            if (findText.Length == 0)
            {
                return startByte <= FormulaCountDbcsBytes(withinText) + 1
                    ? new NumberValue(startByte)
                    : ErrorValue.Value;
            }

            var startIndex = FormulaDbcsByteOffsetToUtf16Index(withinText, startByte - 1);
            if (startIndex >= withinText.Length)
                return ErrorValue.Value;

            var position = withinText.IndexOf(findText, startIndex, StringComparison.Ordinal);
            return position < 0
                ? ErrorValue.Value
                : new NumberValue(FormulaDbcsBytePositionFromUtf16Index(withinText, position));
        }

        private static ScalarValue FormulaSearchBText(string findText, string withinText, int startByte)
        {
            if (findText.Length == 0)
            {
                return startByte <= FormulaCountDbcsBytes(withinText) + 1
                    ? new NumberValue(startByte)
                    : ErrorValue.Value;
            }

            var startIndex = FormulaDbcsByteOffsetToUtf16Index(withinText, startByte - 1);
            if (startIndex >= withinText.Length)
                return ErrorValue.Value;

            Match match;
            try
            {
                match = new Regex(
                    FormulaWildcardToRegexPattern(findText, anchored: false),
                    RegexOptions.IgnoreCase,
                    FormulaTextSearchRegexTimeout).Match(withinText, startIndex);
            }
            catch (RegexMatchTimeoutException)
            {
                return ErrorValue.Value;
            }

            return match.Success
                ? new NumberValue(FormulaDbcsBytePositionFromUtf16Index(withinText, match.Index))
                : ErrorValue.Value;
        }

        private static ScalarValue FormulaReptText(string text, int times)
        {
            var characterCount = FormulaTextScalarContainsSurrogatePair(text)
                ? FormulaTextScalarCountTextElements(text)
                : text.Length;
            var outputCharacterCount = (long)characterCount * times;
            if (outputCharacterCount > MaxFormulaTextSliceLength)
                return ErrorValue.Value;

            if (outputCharacterCount == 0)
                return new TextValue(string.Empty);

            var outputLength = (long)text.Length * times;
            var repeated = string.Create((int)outputLength, (text, times), static (span, state) =>
            {
                var source = state.text.AsSpan();
                for (var i = 0; i < state.times; i++)
                {
                    source.CopyTo(span);
                    span = span[source.Length..];
                }
            });

            return new TextValue(repeated);
        }

        private static ScalarValue FormulaCleanScalar(ScalarValue value)
        {
            if (value is ErrorValue error)
                return error;

            if (!TryGetFormulaCoercedText(value, out var text))
                return ErrorValue.Value;

            var builder = new System.Text.StringBuilder();
            foreach (var ch in text)
            {
                if (ch >= 32)
                    builder.Append(ch);
            }

            return FormulaTextScalarResult(builder.ToString());
        }

        private static ScalarValue FormulaTScalar(ScalarValue value) =>
            value switch
            {
                ErrorValue error => error,
                TextValue text => FormulaTextScalarResult(text.Value),
                _ => new TextValue(string.Empty)
            };

        private static ScalarValue FormulaTextScalarResult(string text) =>
            FormulaTextScalarExceedsExcelTextLimit(text) ? ErrorValue.Value : new TextValue(text);

        private static bool FormulaTextScalarExceedsExcelTextLimit(string text) =>
            (FormulaTextScalarContainsSurrogatePair(text)
                ? FormulaTextScalarCountTextElements(text)
                : text.Length) > MaxFormulaTextSliceLength;

        private static bool FormulaTextScalarContainsSurrogatePair(string text)
        {
            for (var i = 0; i + 1 < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                    return true;
            }

            return false;
        }

        private static int FormulaTextScalarCountTextElements(string text)
        {
            var count = 0;
            for (var index = 0; index < text.Length; count++)
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;

            return count;
        }

        private static bool FormulaTextScalarIsSurrogatePairAt(string text, int index) =>
            index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]);

        private static int FormulaTextElementIndexFromOneBasedPosition(string text, int position)
        {
            var index = 0;
            for (var current = 1; current < position && index < text.Length; current++)
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;

            return index;
        }

        private static int FormulaAdvanceTextElements(string text, int index, int count)
        {
            for (var taken = 0; taken < count && index < text.Length; taken++)
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;

            return index;
        }

        private static int FormulaCountDbcsBytes(string text)
        {
            var bytes = 0;
            for (var index = 0; index < text.Length;)
            {
                bytes += FormulaDbcsByteWidthAt(text, index);
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;
            }

            return bytes;
        }

        private static int FormulaDbcsByteWidthAt(string text, int index)
        {
            if (FormulaTextScalarIsSurrogatePairAt(text, index))
                return 2;

            var ch = text[index];
            return ch <= '\u00ff' || (ch >= '\uff61' && ch <= '\uff9f') ? 1 : 2;
        }

        private static int FormulaDbcsByteOffsetToUtf16Index(string text, int byteOffset)
        {
            var bytes = 0;
            for (var index = 0; index < text.Length;)
            {
                var width = FormulaDbcsByteWidthAt(text, index);
                if (bytes + width > byteOffset)
                    return bytes == byteOffset ? index : index + (FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1);

                bytes += width;
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;
            }

            return text.Length;
        }

        private static int FormulaDbcsBytePositionFromUtf16Index(string text, int utf16Index)
        {
            var bytes = 0;
            for (var index = 0; index < utf16Index && index < text.Length;)
            {
                bytes += FormulaDbcsByteWidthAt(text, index);
                index += FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1;
            }

            return bytes + 1;
        }

        private static string FormulaSliceDbcsBytes(string text, int startByteOffset, int byteCount)
        {
            var endByteOffset = startByteOffset + byteCount;
            var start = text.Length;
            var end = text.Length;
            var bytes = 0;
            for (var index = 0; index < text.Length;)
            {
                var width = FormulaDbcsByteWidthAt(text, index);
                var nextBytes = bytes + width;
                var nextIndex = index + (FormulaTextScalarIsSurrogatePairAt(text, index) ? 2 : 1);
                if (start == text.Length && bytes >= startByteOffset)
                    start = index;

                if (nextBytes > endByteOffset)
                {
                    end = index;
                    break;
                }

                if (nextBytes <= endByteOffset)
                    end = nextIndex;

                bytes = nextBytes;
                index = nextIndex;
            }

            if (startByteOffset >= bytes && start == text.Length)
                start = end = text.Length;

            if (end < start)
                end = start;

            return text[start..end];
        }

        private const string FormulaRegexTextElement = @"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^\uD800-\uDFFF])";

        private static string FormulaWildcardToRegexPattern(string pattern, bool anchored)
        {
            var builder = new System.Text.StringBuilder(anchored ? "^" : "");
            for (var i = 0; i < pattern.Length; i++)
            {
                var ch = pattern[i];
                if (ch == '~' && i + 1 < pattern.Length && pattern[i + 1] is '*' or '?' or '~')
                {
                    builder.Append(Regex.Escape(pattern[++i].ToString()));
                    continue;
                }

                switch (ch)
                {
                    case '*':
                        builder.Append(FormulaRegexTextElement).Append('*');
                        break;
                    case '?':
                        builder.Append(FormulaRegexTextElement);
                        break;
                    default:
                        builder.Append(Regex.Escape(ch.ToString()));
                        break;
                }
            }

            if (anchored)
                builder.Append('$');

            return builder.ToString();
        }

        private static bool TryGetFormulaTextScalarNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    return true;
                case BoolValue boolean:
                    number = boolean.Value ? 1d : 0d;
                    return true;
                case BlankValue:
                    number = 0d;
                    return true;
                case TextValue text:
                    return TryParseFormulaTextScalarNumber(text.Value, out number);
                default:
                    number = 0d;
                    return false;
            }
        }

        private static bool TryParseFormulaTextScalarNumber(string text, out double number)
        {
            var candidate = text.Trim();
            var percentCount = 0;
            while (candidate.EndsWith('%'))
            {
                percentCount++;
                candidate = candidate[..^1].TrimEnd();
            }

            if (percentCount > 0 &&
                double.TryParse(candidate, NumberStyles.Any, FormulaTextScalarNumberCulture, out number))
            {
                for (var i = 0; i < percentCount; i++)
                    number /= 100d;

                return true;
            }

            if (double.TryParse(candidate, NumberStyles.Any, FormulaTextScalarNumberCulture, out number))
                return true;

            if (TryParseFormulaExcelFakeLeapDayValueText(candidate, out number))
                return true;

            if (DateTime.TryParse(candidate, FormulaTextScalarNumberCulture, DateTimeStyles.None, out var dateTime))
            {
                number = IsFormulaTextScalarTimeOnlyText(candidate)
                    ? dateTime.TimeOfDay.TotalDays
                    : FormulaDateToExcelSerial(dateTime);
                return true;
            }

            number = 0d;
            return false;
        }

        private static bool IsFormulaTextScalarTimeOnlyText(string text) =>
            !text.Contains('/') &&
            !text.Contains('-') &&
            !FormulaDateTimeTextHasMonthNameRegex.IsMatch(text) &&
            (text.Contains(':') || FormulaDateTimeTextHasAmPmRegex.IsMatch(text));

        private static char FormulaExcelAnsiCodeToChar(int code) => code switch
        {
            128 => '\u20AC',
            130 => '\u201A',
            131 => '\u0192',
            132 => '\u201E',
            133 => '\u2026',
            134 => '\u2020',
            135 => '\u2021',
            136 => '\u02C6',
            137 => '\u2030',
            138 => '\u0160',
            139 => '\u2039',
            140 => '\u0152',
            142 => '\u017D',
            145 => '\u2018',
            146 => '\u2019',
            147 => '\u201C',
            148 => '\u201D',
            149 => '\u2022',
            150 => '\u2013',
            151 => '\u2014',
            152 => '\u02DC',
            153 => '\u2122',
            154 => '\u0161',
            155 => '\u203A',
            156 => '\u0153',
            158 => '\u017E',
            159 => '\u0178',
            _ => (char)code
        };

        private static int FormulaCharToExcelAnsiCode(char ch) => ch switch
        {
            '\u20AC' => 128,
            '\u201A' => 130,
            '\u0192' => 131,
            '\u201E' => 132,
            '\u2026' => 133,
            '\u2020' => 134,
            '\u2021' => 135,
            '\u02C6' => 136,
            '\u2030' => 137,
            '\u0160' => 138,
            '\u2039' => 139,
            '\u0152' => 140,
            '\u017D' => 142,
            '\u2018' => 145,
            '\u2019' => 146,
            '\u201C' => 147,
            '\u201D' => 148,
            '\u2022' => 149,
            '\u2013' => 150,
            '\u2014' => 151,
            '\u02DC' => 152,
            '\u2122' => 153,
            '\u0161' => 154,
            '\u203A' => 155,
            '\u0153' => 156,
            '\u017E' => 158,
            '\u0178' => 159,
            <= '\u00FF' => ch,
            _ => 63
        };

        private bool TryResolveFormulaRomanForm(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out int form)
        {
            form = 0;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            if (value is BoolValue boolean)
            {
                form = boolean.Value ? 0 : 4;
                return true;
            }

            if (!TryGetFormulaArithmeticNumber(value, out var number) ||
                !double.IsFinite(number))
            {
                return false;
            }

            form = (int)Math.Truncate(number);
            return form is >= 0 and <= 4;
        }

        private bool TryEvaluateFormulaRowColumnFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (function.Arguments.Count == 0)
            {
                value = new NumberValue(function.Kind == ConditionalFormulaScalarFunctionKind.Row
                    ? _formulaCurrentRow
                    : _formulaCurrentCol);
                return true;
            }

            var reference = function.Arguments[0];
            if (reference.Kind != ConditionalFormulaOperandKind.Reference ||
                !TryResolveFormulaReference(reference, rowOffset, colOffset, out _, out var row, out var col))
            {
                return false;
            }

            value = new NumberValue(function.Kind == ConditionalFormulaScalarFunctionKind.Row ? row : col);
            return true;
        }

        private bool TryEvaluateFormulaReferenceDimensionFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (function.Arguments.Count != 1 ||
                !TryResolveFormulaReferenceRange(
                    function.Arguments[0],
                    rowOffset,
                    colOffset,
                    out _,
                    out var startRow,
                    out var startCol,
                    out var endRow,
                    out var endCol))
            {
                return false;
            }

            value = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Rows => new NumberValue(endRow - startRow + 1),
                ConditionalFormulaScalarFunctionKind.Columns => new NumberValue(endCol - startCol + 1),
                ConditionalFormulaScalarFunctionKind.Areas => new NumberValue(1),
                _ => ErrorValue.Value
            };

            return value is NumberValue;
        }

        private bool TryEvaluateFormulaNumericScalarFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionNumber(function.Arguments[0], rowOffset, colOffset, out var first))
                return false;

            double result;
            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.Abs:
                    result = Math.Abs(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Int:
                    result = Math.Floor(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Even:
                    result = EvenFormulaNumber(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Odd:
                    result = OddFormulaNumber(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Round:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var digitsNumber) ||
                        !TryGetFormulaRoundDigits(digitsNumber, out var digits))
                    {
                        return false;
                    }

                    result = RoundFormulaNumber(first, digits);
                    break;
                case ConditionalFormulaScalarFunctionKind.RoundUp:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var roundUpDigitsNumber) ||
                        !TryGetFormulaRoundDigits(roundUpDigitsNumber, out var roundUpDigits))
                    {
                        return false;
                    }

                    result = RoundUpFormulaNumber(first, roundUpDigits);
                    break;
                case ConditionalFormulaScalarFunctionKind.RoundDown:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var roundDownDigitsNumber) ||
                        !TryGetFormulaRoundDigits(roundDownDigitsNumber, out var roundDownDigits))
                    {
                        return false;
                    }

                    result = RoundDownFormulaNumber(first, roundDownDigits);
                    break;
                case ConditionalFormulaScalarFunctionKind.MRound:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var multiple) ||
                        !TryMRoundFormulaNumber(first, multiple, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Ceiling:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var significance) ||
                        !TryCeilingFormulaNumber(first, significance, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.CeilingMath:
                    var ceilingMathSignificance = 1d;
                    if (function.Arguments.Count >= 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out ceilingMathSignificance))
                    {
                        return false;
                    }

                    var ceilingMathMode = 0d;
                    if (function.Arguments.Count == 3 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out ceilingMathMode))
                    {
                        return false;
                    }

                    if (!TryCeilingMathFormulaNumber(first, ceilingMathSignificance, ceilingMathMode, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.IsoCeiling:
                    var isoSignificance = 1d;
                    if (function.Arguments.Count == 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out isoSignificance))
                    {
                        return false;
                    }

                    if (!TryIsoCeilingFormulaNumber(first, isoSignificance, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Floor:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var floorSignificance) ||
                        !TryFloorFormulaNumber(first, floorSignificance, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.FloorMath:
                    var floorMathSignificance = 1d;
                    if (function.Arguments.Count >= 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out floorMathSignificance))
                    {
                        return false;
                    }

                    var floorMathMode = 0d;
                    if (function.Arguments.Count == 3 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out floorMathMode))
                    {
                        return false;
                    }

                    if (!TryFloorMathFormulaNumber(first, floorMathSignificance, floorMathMode, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.FloorPrecise:
                    var floorPreciseSignificance = 1d;
                    if (function.Arguments.Count == 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out floorPreciseSignificance))
                    {
                        return false;
                    }

                    if (!TryFloorPreciseFormulaNumber(first, floorPreciseSignificance, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Trunc:
                    var truncDigits = 0;
                    if (function.Arguments.Count == 2 &&
                        (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var truncDigitsNumber) ||
                         !TryGetFormulaRoundDigits(truncDigitsNumber, out truncDigits)))
                    {
                        return false;
                    }

                    result = RoundDownFormulaNumber(first, truncDigits);
                    break;
                case ConditionalFormulaScalarFunctionKind.Fact:
                    if (first < 0)
                        return false;

                    var factorialInput = Math.Truncate(first);
                    if (factorialInput > MaxFormulaFactorialInput)
                        return false;

                    result = FactorialFormulaNumber((int)factorialInput);
                    break;
                case ConditionalFormulaScalarFunctionKind.FactDouble:
                    if (first < 0)
                        return false;

                    var doubleFactorialInput = Math.Truncate(first);
                    if (doubleFactorialInput > MaxFormulaDoubleFactorialInput ||
                        !TryDoubleFactorialFormulaNumber((int)doubleFactorialInput, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Mod:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var divisor) ||
                        divisor == 0)
                    {
                        return false;
                    }

                    result = first - divisor * Math.Floor(first / divisor);
                    break;
                case ConditionalFormulaScalarFunctionKind.Quotient:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var denominator) ||
                        denominator == 0)
                    {
                        return false;
                    }

                    result = Math.Truncate(first / denominator);
                    break;
                case ConditionalFormulaScalarFunctionKind.Combin:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var numberChosen) ||
                        !TryCombinFormulaNumber(first, numberChosen, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Combina:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var combinaNumberChosen) ||
                        !TryCombinaFormulaNumber(first, combinaNumberChosen, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Permut:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var permutNumberChosen) ||
                        !TryPermutFormulaNumber(first, permutNumberChosen, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.PermutationA:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var permutationANumberChosen) ||
                        !TryPermutationAFormulaNumber(first, permutationANumberChosen, out result))
                    {
                        return false;
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.Multinomial:
                    if (!TryMultinomialFormulaNumber(function, first, rowOffset, colOffset, out var multinomialResult))
                        return false;

                    result = multinomialResult;
                    break;
                case ConditionalFormulaScalarFunctionKind.Gcd:
                    if (!TryGcdFormulaNumber(function, first, rowOffset, colOffset, out var gcdResult))
                        return false;

                    result = gcdResult;
                    break;
                case ConditionalFormulaScalarFunctionKind.Lcm:
                    if (!TryLcmFormulaNumber(function, first, rowOffset, colOffset, out var lcmResult))
                        return false;

                    result = lcmResult;
                    break;
                case ConditionalFormulaScalarFunctionKind.Sqrt:
                    if (first < 0)
                        return false;

                    result = Math.Sqrt(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.SqrtPi:
                    if (first < 0)
                        return false;

                    result = Math.Sqrt(first * Math.PI);
                    break;
                case ConditionalFormulaScalarFunctionKind.Sign:
                    result = first > 0
                        ? 1
                        : first < 0
                            ? -1
                            : 0;
                    break;
                case ConditionalFormulaScalarFunctionKind.Power:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var exponent))
                    {
                        return false;
                    }

                    result = Math.Pow(first, exponent);
                    break;
                case ConditionalFormulaScalarFunctionKind.Exp:
                    result = Math.Exp(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Ln:
                    if (first <= 0)
                        return false;

                    result = Math.Log(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Log10:
                    if (first <= 0)
                        return false;

                    result = Math.Log10(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Log:
                    if (first <= 0)
                        return false;

                    var logBase = 10d;
                    if (function.Arguments.Count == 2 &&
                        (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out logBase) ||
                         logBase <= 0 ||
                         logBase == 1))
                    {
                        return false;
                    }

                    result = Math.Log(first, logBase);
                    break;
                case ConditionalFormulaScalarFunctionKind.Degrees:
                    result = first * 180d / Math.PI;
                    break;
                case ConditionalFormulaScalarFunctionKind.Radians:
                    result = first * Math.PI / 180d;
                    break;
                case ConditionalFormulaScalarFunctionKind.Sin:
                    result = Math.Sin(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Csc:
                    var sine = Math.Sin(first);
                    if (sine == 0d || !double.IsFinite(sine))
                        return false;

                    result = 1d / sine;
                    break;
                case ConditionalFormulaScalarFunctionKind.Csch:
                    var hyperbolicSine = Math.Sinh(first);
                    if (hyperbolicSine == 0d || !double.IsFinite(hyperbolicSine))
                        return false;

                    result = 1d / hyperbolicSine;
                    break;
                case ConditionalFormulaScalarFunctionKind.Sinh:
                    result = Math.Sinh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Asinh:
                    result = Math.Asinh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Acosh:
                    if (first < 1d)
                        return false;

                    result = Math.Acosh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Cosh:
                    result = Math.Cosh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Sech:
                    var hyperbolicCosine = Math.Cosh(first);
                    if (!double.IsFinite(hyperbolicCosine))
                        return false;

                    result = 1d / hyperbolicCosine;
                    break;
                case ConditionalFormulaScalarFunctionKind.Tanh:
                    result = Math.Tanh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Atanh:
                    if (first <= -1d || first >= 1d)
                        return false;

                    result = Math.Atanh(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Acoth:
                    if (!double.IsFinite(first) || Math.Abs(first) <= 1d)
                        return false;

                    var acothRatio = (first + 1d) / (first - 1d);
                    if (!double.IsFinite(acothRatio) || acothRatio <= 0d)
                        return false;

                    result = 0.5d * Math.Log(acothRatio);
                    break;
                case ConditionalFormulaScalarFunctionKind.Coth:
                    var hyperbolicTangent = Math.Tanh(first);
                    if (hyperbolicTangent == 0d || !double.IsFinite(hyperbolicTangent))
                        return false;

                    result = 1d / hyperbolicTangent;
                    break;
                case ConditionalFormulaScalarFunctionKind.Asin:
                    if (first < -1d || first > 1d)
                        return false;

                    result = Math.Asin(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Acos:
                    if (first < -1d || first > 1d)
                        return false;

                    result = Math.Acos(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Acot:
                    if (first == 0d)
                    {
                        result = Math.PI / 2d;
                        break;
                    }

                    var reciprocal = 1d / first;
                    if (!double.IsFinite(reciprocal))
                        return false;

                    result = Math.Atan(reciprocal);
                    if (first < 0d)
                        result += Math.PI;

                    break;
                case ConditionalFormulaScalarFunctionKind.Atan:
                    result = Math.Atan(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Atan2:
                    if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var second) ||
                        first == 0d && second == 0d)
                    {
                        return false;
                    }

                    result = Math.Atan2(second, first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Cos:
                    result = Math.Cos(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Sec:
                    var cosine = Math.Cos(first);
                    if (Math.Abs(cosine) <= FormulaSecZeroCosineTolerance || !double.IsFinite(cosine))
                        return false;

                    result = 1d / cosine;
                    break;
                case ConditionalFormulaScalarFunctionKind.Cot:
                    var tangent = Math.Tan(first);
                    if (tangent == 0d || !double.IsFinite(tangent))
                        return false;

                    result = 1d / tangent;
                    break;
                case ConditionalFormulaScalarFunctionKind.Tan:
                    result = Math.Tan(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Delta:
                    var deltaSecond = 0d;
                    if (function.Arguments.Count == 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out deltaSecond))
                    {
                        return false;
                    }

                    result = first == deltaSecond ? 1d : 0d;
                    break;
                case ConditionalFormulaScalarFunctionKind.Erf:
                    if (function.Arguments.Count == 2)
                    {
                        if (!TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var upper))
                        {
                            return false;
                        }

                        result = FormulaErfApprox(upper) - FormulaErfApprox(first);
                    }
                    else
                    {
                        result = FormulaErfApprox(first);
                    }

                    break;
                case ConditionalFormulaScalarFunctionKind.ErfPrecise:
                    result = FormulaErfApprox(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.Erfc:
                case ConditionalFormulaScalarFunctionKind.ErfcPrecise:
                    result = 1d - FormulaErfApprox(first);
                    break;
                case ConditionalFormulaScalarFunctionKind.GeStep:
                    var step = 0d;
                    if (function.Arguments.Count == 2 &&
                        !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out step))
                    {
                        return false;
                    }

                    result = first >= step ? 1d : 0d;
                    break;
                case ConditionalFormulaScalarFunctionKind.BitAnd:
                case ConditionalFormulaScalarFunctionKind.BitOr:
                case ConditionalFormulaScalarFunctionKind.BitXor:
                    if (!TryEvaluateFormulaBitwiseBinaryFunction(
                            function,
                            first,
                            rowOffset,
                            colOffset,
                            out var bitwiseBinaryResult))
                    {
                        return false;
                    }

                    result = bitwiseBinaryResult;
                    break;
                case ConditionalFormulaScalarFunctionKind.BitLShift:
                case ConditionalFormulaScalarFunctionKind.BitRShift:
                    if (!TryEvaluateFormulaBitwiseShiftFunction(
                            function,
                            first,
                            rowOffset,
                            colOffset,
                            out var bitwiseShiftResult))
                    {
                        return false;
                    }

                    result = bitwiseShiftResult;
                    break;
                default:
                    return false;
            }

            if (!double.IsFinite(result))
                return false;

            value = new NumberValue(result);
            return true;
        }

        private bool TryEvaluateFormulaBitwiseBinaryFunction(
            ConditionalFormulaScalarFunction function,
            double first,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0d;
            if (!TryGetFormulaBitwiseInteger(first, out var left) ||
                !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var second) ||
                !TryGetFormulaBitwiseInteger(second, out var right))
            {
                return false;
            }

            var bitwiseResult = function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.BitAnd => left & right,
                ConditionalFormulaScalarFunctionKind.BitOr => left | right,
                ConditionalFormulaScalarFunctionKind.BitXor => left ^ right,
                _ => 0UL
            };

            result = bitwiseResult;
            return true;
        }

        private static double FormulaErfApprox(double x)
        {
            if (x == 0d)
                return 0d;

            var sign = Math.Sign(x);
            var ax = Math.Abs(x);
            const double p = 0.3275911d;
            var t = 1d / (1d + p * ax);
            var y = 1d - (((((1.061405429d * t - 1.453152027d) * t) + 1.421413741d) * t - 0.284496736d) * t + 0.254829592d) * t * Math.Exp(-ax * ax);
            return sign * y;
        }

        private bool TryEvaluateFormulaBitwiseShiftFunction(
            ConditionalFormulaScalarFunction function,
            double first,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0d;
            if (!TryGetFormulaBitwiseInteger(first, out var value) ||
                !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var shiftNumber) ||
                !TryGetFormulaBitwiseShift(shiftNumber, out var shift))
            {
                return false;
            }

            var shiftLeft = function.Kind == ConditionalFormulaScalarFunctionKind.BitLShift;
            if (shift < 0)
            {
                shiftLeft = !shiftLeft;
                shift = -shift;
            }

            if (shiftLeft)
            {
                if (shift > 0 && value > (MaxFormulaBitwiseInput >> shift))
                    return false;

                result = value << shift;
                return true;
            }

            result = value >> shift;
            return true;
        }

        private static bool TryGetFormulaBitwiseInteger(double value, out ulong integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                Math.Truncate(value) != value ||
                value < 0d ||
                value > MaxFormulaBitwiseInput)
            {
                return false;
            }

            integer = (ulong)value;
            return true;
        }

        private static bool TryGetFormulaBitwiseShift(double value, out int shift)
        {
            shift = 0;
            if (!double.IsFinite(value) ||
                Math.Truncate(value) != value ||
                Math.Abs(value) > MaxFormulaBitwiseShift)
            {
                return false;
            }

            shift = (int)value;
            return true;
        }

        private bool TryEvaluateFormulaValueFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var text) ||
                !TryParseFormulaValueText(text, out var number))
            {
                return false;
            }

            value = new NumberValue(number);
            return true;
        }

        private static bool TryParseFormulaValueText(string text, out double number)
        {
            number = 0;
            var candidate = text.Trim();
            if (candidate.Length == 0)
                return false;

            var isPercent = candidate.EndsWith('%');
            if (isPercent)
            {
                candidate = candidate[..^1].TrimEnd();
                if (candidate.Length == 0)
                    return false;
            }

            var styles = System.Globalization.NumberStyles.Float |
                System.Globalization.NumberStyles.AllowThousands;
            if (!double.TryParse(candidate, styles, System.Globalization.CultureInfo.InvariantCulture, out number) ||
                !double.IsFinite(number))
            {
                return false;
            }

            if (isPercent)
            {
                number /= 100;
                if (!double.IsFinite(number))
                    return false;
            }

            return true;
        }

        private bool TryEvaluateFormulaNumberValueFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaNumberValueArgument(
                    function.Arguments[0],
                    rowOffset,
                    colOffset,
                    out var text,
                    out var argumentError))
            {
                return false;
            }

            if (argumentError is not null)
            {
                value = argumentError;
                return true;
            }

            var decimalSeparator = ".";
            if (function.Arguments.Count > 1 &&
                !TryResolveFormulaNumberValueArgument(
                    function.Arguments[1],
                    rowOffset,
                    colOffset,
                    out decimalSeparator,
                    out argumentError))
            {
                return false;
            }

            if (argumentError is not null)
            {
                value = argumentError;
                return true;
            }

            var groupSeparator = ",";
            if (function.Arguments.Count > 2 &&
                !TryResolveFormulaNumberValueArgument(
                    function.Arguments[2],
                    rowOffset,
                    colOffset,
                    out groupSeparator,
                    out argumentError))
            {
                return false;
            }

            if (argumentError is not null)
            {
                value = argumentError;
                return true;
            }

            if (!TryParseFormulaNumberValueText(text, decimalSeparator, groupSeparator, out var number))
            {
                value = ErrorValue.Value;
                return true;
            }

            value = new NumberValue(number);
            return true;
        }

        private bool TryResolveFormulaNumberValueArgument(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out string text,
            out ErrorValue? error)
        {
            text = string.Empty;
            error = null;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return true;
            }

            text = value switch
            {
                TextValue textValue => textValue.Value,
                NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                _ => value.ToString() ?? string.Empty
            };
            return true;
        }

        private static bool TryParseFormulaNumberValueText(
            string text,
            string decimalSeparator,
            string groupSeparator,
            out double number)
        {
            number = 0;
            var candidate = text.Trim();
            if (decimalSeparator.Length == 0 || groupSeparator.Length == 0)
                return false;

            decimalSeparator = decimalSeparator[..1];
            groupSeparator = groupSeparator[..1];
            if (decimalSeparator == groupSeparator)
                return false;

            candidate = candidate
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty);

            var accountingNegative = candidate.StartsWith('(') && candidate.EndsWith(')');
            if (accountingNegative)
                candidate = candidate[1..^1];

            var percentCount = 0;
            while (candidate.EndsWith('%'))
            {
                percentCount++;
                candidate = candidate[..^1];
            }

            var decimalIndex = candidate.IndexOf(decimalSeparator, StringComparison.Ordinal);
            if (decimalIndex >= 0 &&
                candidate.IndexOf(groupSeparator, decimalIndex + decimalSeparator.Length, StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            candidate = candidate.Replace(groupSeparator, string.Empty, StringComparison.Ordinal);
            if (decimalSeparator != ".")
                candidate = candidate.Replace(decimalSeparator, ".", StringComparison.Ordinal);

            if (candidate.Length == 0)
                return true;

            if (!double.TryParse(
                    candidate,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            for (var i = 0; i < percentCount; i++)
                number /= 100d;

            if (accountingNegative)
                number = -number;

            return double.IsFinite(number);
        }

        private bool TryEvaluateFormulaDateScalarFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.Today:
                    value = DateTimeValue.FromDateTime(DateTime.Today);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Now:
                    value = DateTimeValue.FromDateTime(DateTime.Now);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Date:
                    if (!TryResolveFormulaDatePart(function.Arguments[0], rowOffset, colOffset, out var year) ||
                        !TryResolveFormulaDatePart(function.Arguments[1], rowOffset, colOffset, out var month) ||
                        !TryResolveFormulaDatePart(function.Arguments[2], rowOffset, colOffset, out var day) ||
                        !TryCreateFormulaDateValue(year, month, day, out var dateValue))
                    {
                        return false;
                    }

                    value = dateValue;
                    return true;
                case ConditionalFormulaScalarFunctionKind.DateValue:
                    if (!TryEvaluateFormulaDateValueFunction(function.Arguments[0], rowOffset, colOffset, out value))
                        return false;

                    return true;
                case ConditionalFormulaScalarFunctionKind.Time:
                    if (!TryResolveFormulaTimePart(function.Arguments[0], rowOffset, colOffset, out var timeHour) ||
                        !TryResolveFormulaTimePart(function.Arguments[1], rowOffset, colOffset, out var timeMinute) ||
                        !TryResolveFormulaTimePart(function.Arguments[2], rowOffset, colOffset, out var timeSecond))
                    {
                        return false;
                    }

                    var timeFraction = (timeHour * 3600d + timeMinute * 60d + timeSecond) / 86400d;
                    value = new NumberValue(timeFraction - Math.Floor(timeFraction));
                    return true;
                case ConditionalFormulaScalarFunctionKind.TimeValue:
                    if (!TryEvaluateFormulaTimeValueFunction(function.Arguments[0], rowOffset, colOffset, out value))
                        return false;

                    return true;
                case ConditionalFormulaScalarFunctionKind.Year:
                case ConditionalFormulaScalarFunctionKind.Month:
                case ConditionalFormulaScalarFunctionKind.Day:
                    if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var date))
                        return false;

                    value = function.Kind switch
                    {
                        ConditionalFormulaScalarFunctionKind.Year => new NumberValue(date.Year),
                        ConditionalFormulaScalarFunctionKind.Month => new NumberValue(date.Month),
                        ConditionalFormulaScalarFunctionKind.Day => new NumberValue(date.Day),
                        _ => ErrorValue.Value
                    };
                    return value is NumberValue;
                case ConditionalFormulaScalarFunctionKind.Hour:
                case ConditionalFormulaScalarFunctionKind.Minute:
                case ConditionalFormulaScalarFunctionKind.Second:
                    if (!TryResolveFormulaFunctionTimeParts(
                            function.Arguments[0],
                            rowOffset,
                            colOffset,
                            out var hour,
                            out var minute,
                            out var second))
                    {
                        return false;
                    }

                    value = function.Kind switch
                    {
                        ConditionalFormulaScalarFunctionKind.Hour => new NumberValue(hour),
                        ConditionalFormulaScalarFunctionKind.Minute => new NumberValue(minute),
                        ConditionalFormulaScalarFunctionKind.Second => new NumberValue(second),
                        _ => ErrorValue.Value
                    };
                    return value is NumberValue;
                case ConditionalFormulaScalarFunctionKind.Weekday:
                    if (!TryResolveFormulaFunctionDateSerial(function.Arguments[0], rowOffset, colOffset, out var weekdaySerial) ||
                        !TryResolveFormulaOptionalReturnType(function, 1, rowOffset, colOffset, defaultValue: 1, out var weekdayReturnType) ||
                        !TryEvaluateFormulaWeekday(weekdaySerial, weekdayReturnType, out var weekday))
                    {
                        return false;
                    }

                    value = new NumberValue(weekday);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Weeknum:
                    if (!TryResolveFormulaFunctionDateSerial(function.Arguments[0], rowOffset, colOffset, out var weeknumSerial) ||
                        !TryResolveFormulaOptionalReturnType(function, 1, rowOffset, colOffset, defaultValue: 1, out var weeknumReturnType) ||
                        !TryEvaluateFormulaWeeknum(weeknumSerial, weeknumReturnType, out var weeknum))
                    {
                        return false;
                    }

                    value = new NumberValue(weeknum);
                    return true;
                case ConditionalFormulaScalarFunctionKind.IsoWeeknum:
                    if (!TryResolveFormulaFunctionDateSerial(function.Arguments[0], rowOffset, colOffset, out var isoSerial) ||
                        !TryEvaluateFormulaIsoWeeknum(isoSerial, out var isoWeeknum))
                    {
                        return false;
                    }

                    value = new NumberValue(isoWeeknum);
                    return true;
                case ConditionalFormulaScalarFunctionKind.EDate:
                    if (!TryEvaluateFormulaEDate(function, rowOffset, colOffset, out var eDateSerial))
                        return false;

                    value = new NumberValue(eDateSerial);
                    return true;
                case ConditionalFormulaScalarFunctionKind.EOMonth:
                    if (!TryEvaluateFormulaEOMonth(function, rowOffset, colOffset, out var eoMonthSerial))
                        return false;

                    value = new NumberValue(eoMonthSerial);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Days:
                    if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var endDate) ||
                        !TryResolveFormulaFunctionDate(function.Arguments[1], rowOffset, colOffset, out var startDate))
                    {
                        return false;
                    }

                    value = new NumberValue(FormulaDateToExcelSerial(endDate) - FormulaDateToExcelSerial(startDate));
                    return true;
                case ConditionalFormulaScalarFunctionKind.Datedif:
                    if (!TryEvaluateFormulaDatedif(function, rowOffset, colOffset, out var datedif))
                        return false;

                    value = new NumberValue(datedif);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Days360:
                    if (!TryEvaluateFormulaDays360(function, rowOffset, colOffset, out var days360))
                        return false;

                    value = new NumberValue(days360);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Yearfrac:
                    if (!TryEvaluateFormulaYearfrac(function, rowOffset, colOffset, out var yearfrac))
                        return false;

                    value = new NumberValue(yearfrac);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Workday:
                case ConditionalFormulaScalarFunctionKind.WorkdayIntl:
                    return TryEvaluateFormulaWorkday(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Networkdays:
                case ConditionalFormulaScalarFunctionKind.NetworkdaysIntl:
                    return TryEvaluateFormulaNetworkdays(function, rowOffset, colOffset, out value);
                default:
                    return false;
            }
        }

        private bool TryEvaluateFormulaDateValueFunction(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var source))
            {
                if (source is ErrorValue unresolvedError)
                {
                    value = unresolvedError;
                    return true;
                }

                return false;
            }

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            if (!TryEvaluateFormulaDateValue(FormulaDateTimeValueText(source), out var serial))
            {
                value = ErrorValue.Value;
                return true;
            }

            value = new NumberValue(serial);
            return true;
        }

        private bool TryEvaluateFormulaTimeValueFunction(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var source))
            {
                if (source is ErrorValue unresolvedError)
                {
                    value = unresolvedError;
                    return true;
                }

                return false;
            }

            if (source is ErrorValue sourceError)
            {
                value = sourceError;
                return true;
            }

            if (!TryEvaluateFormulaTimeValue(FormulaDateTimeValueText(source), out var fraction))
            {
                value = ErrorValue.Value;
                return true;
            }

            value = new NumberValue(fraction);
            return true;
        }

        private static string FormulaDateTimeValueText(ScalarValue value) =>
            value switch
            {
                TextValue text => text.Value,
                NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                ErrorValue error => error.Code,
                _ => value.ToString() ?? string.Empty
            };

        private static bool TryEvaluateFormulaDateValue(string text, out double serial)
        {
            serial = 0;
            if (TryParseFormulaExcelFakeLeapDayValueText(text, out _))
            {
                serial = 60;
                return true;
            }

            if (!FormulaTextHasDateComponent(text))
                return false;

            if (TryParseFormulaMonthYearDateValueText(text, out var monthYearDate))
            {
                serial = FormulaDateToExcelSerial(monthYearDate);
                return double.IsFinite(serial);
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            {
                serial = Math.Floor(FormulaDateToExcelSerial(dateTime));
                return double.IsFinite(serial);
            }

            return false;
        }

        private static bool TryEvaluateFormulaTimeValue(string text, out double fraction)
        {
            fraction = 0;
            if (!FormulaTextHasTimeComponent(text))
                return false;

            if (TryParseFormulaExcelFakeLeapDayValueText(text, out var fakeLeapSerial))
            {
                fraction = fakeLeapSerial - Math.Floor(fakeLeapSerial);
                return double.IsFinite(fraction);
            }

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var timeSpan) &&
                timeSpan.Days == 0)
            {
                fraction = timeSpan.TotalDays;
                return double.IsFinite(fraction);
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            {
                fraction = dateTime.TimeOfDay.TotalDays;
                return double.IsFinite(fraction);
            }

            return false;
        }

        private static bool FormulaTextHasTimeComponent(string text) =>
            FormulaDateTimeTextHasTimeSeparatorRegex.IsMatch(text) ||
            FormulaDateTimeTextHasAmPmRegex.IsMatch(text);

        private static bool FormulaTextHasDateComponent(string text) =>
            FormulaDateTimeTextHasDateSeparatorRegex.IsMatch(text) ||
            FormulaDateTimeTextHasMonthNameRegex.IsMatch(text);

        private static bool TryParseFormulaMonthYearDateValueText(string text, out DateTime date) =>
            DateTime.TryParseExact(
                text.Trim(),
                ["MMMM yyyy", "MMM yyyy", "MMMM, yyyy", "MMM, yyyy", "MMMM-yyyy", "MMM-yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);

        private static bool TryParseFormulaExcelFakeLeapDayValueText(string text, out double serial)
        {
            serial = 0;
            var match = FormulaDateTimeFakeLeapDayTextRegex.Match(text.Trim());
            if (!match.Success)
                return false;

            serial = 60;
            if (match.Groups[1].Success)
            {
                if (!DateTime.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    return false;

                serial += time.TimeOfDay.TotalDays;
            }

            return true;
        }

        private bool TryEvaluateFormulaDatedif(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0;
            if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var startDate) ||
                !TryResolveFormulaFunctionDate(function.Arguments[1], rowOffset, colOffset, out var endDate) ||
                !TryResolveFormulaFunctionText(function.Arguments[2], rowOffset, colOffset, out var unit))
            {
                return false;
            }

            var start = startDate.Date;
            var end = endDate.Date;
            if (end < start)
                return false;

            switch (unit.ToUpperInvariant())
            {
                case "D":
                    result = FormulaDateToExcelSerial(end) - FormulaDateToExcelSerial(start);
                    return double.IsFinite(result);
                case "M":
                    result = FormulaDatedifMonthDiff(start, end);
                    return true;
                case "Y":
                    result = FormulaDatedifYearDiff(start, end);
                    return true;
                case "YM":
                    result = FormulaDatedifMonthDiff(start, end) % 12;
                    return true;
                case "YD":
                    return TryEvaluateFormulaDatedifYD(start, end, out result);
                case "MD":
                    result = FormulaDatedifMD(start, end);
                    return true;
                default:
                    return false;
            }
        }

        private static int FormulaDatedifMonthDiff(DateTime start, DateTime end)
        {
            var months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
            if (end.Day < start.Day)
                months--;

            return months;
        }

        private static int FormulaDatedifYearDiff(DateTime start, DateTime end)
        {
            var years = end.Year - start.Year;
            if (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day))
                years--;

            return years;
        }

        private static bool TryEvaluateFormulaDatedifYD(DateTime start, DateTime end, out double result)
        {
            result = 0;
            try
            {
                var anchor = new DateTime(end.Year, start.Month, start.Day);
                var adjustedStart = anchor > end ? anchor.AddYears(-1) : anchor;
                result = FormulaDateToExcelSerial(end) - FormulaDateToExcelSerial(adjustedStart);
                return double.IsFinite(result);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static int FormulaDatedifMD(DateTime start, DateTime end)
        {
            if (end.Day >= start.Day)
                return end.Day - start.Day;

            var previousMonth = end.AddMonths(-1);
            return FormulaDaysInExcelMonth(previousMonth.Year, previousMonth.Month) + end.Day - start.Day;
        }

        private static int FormulaDaysInExcelMonth(int year, int month) =>
            year == 1900 && month == 2 ? 29 : DateTime.DaysInMonth(year, month);

        private bool TryEvaluateFormulaDays360(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out int days)
        {
            days = 0;
            if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var startDate) ||
                !TryResolveFormulaFunctionDate(function.Arguments[1], rowOffset, colOffset, out var endDate))
            {
                return false;
            }

            var european = false;
            if (function.Arguments.Count == 3)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out var method) ||
                    !double.IsFinite(method))
                {
                    return false;
                }

                european = method != 0;
            }

            var start = startDate.Date;
            var end = endDate.Date;
            var startDay = start.Day;
            var endDay = end.Day;

            if (european)
            {
                if (startDay == 31)
                    startDay = 30;
                if (endDay == 31)
                    endDay = 30;
            }
            else
            {
                if (IsLastDayOfFebruary(start))
                    startDay = 30;
                if (IsLastDayOfFebruary(end) && startDay == 30)
                    endDay = 30;
                if (startDay == 31)
                    startDay = 30;
                if (endDay == 31 && startDay == 30)
                    endDay = 30;
            }

            days = 360 * (end.Year - start.Year) +
                30 * (end.Month - start.Month) +
                (endDay - startDay);
            return true;
        }

        private bool TryEvaluateFormulaWorkday(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryPropagateFormulaWorkdayArgumentErrors(function, rowOffset, colOffset, out var argumentError))
            {
                if (argumentError is not null)
                {
                    value = argumentError;
                    return true;
                }

                return false;
            }

            if (!TryResolveFormulaWorkdayWeekendMask(function, rowOffset, colOffset, out var weekendMask, out var weekendError))
            {
                if (weekendError is not null)
                {
                    value = weekendError;
                    return true;
                }

                return false;
            }

            var holidayArgumentIndex = function.Kind == ConditionalFormulaScalarFunctionKind.WorkdayIntl ? 3 : 2;
            if (!TryCollectFormulaWorkdayHolidays(function, holidayArgumentIndex, rowOffset, colOffset, out var holidays, out var holidayError))
            {
                value = holidayError ?? ErrorValue.Value;
                return holidayError is not null;
            }

            if (!TryResolveFormulaWorkdayDate(function.Arguments[0], rowOffset, colOffset, out var current, out var startError))
            {
                if (startError is not null)
                {
                    value = startError;
                    return true;
                }

                return false;
            }

            if (!TryResolveFormulaWorkdayNumber(function.Arguments[1], rowOffset, colOffset, out var rawDays, out var daysError))
            {
                if (daysError is not null)
                {
                    value = daysError;
                    return true;
                }

                return false;
            }

            if (!double.IsFinite(rawDays) ||
                rawDays < int.MinValue + 1d ||
                rawDays > int.MaxValue)
            {
                value = ErrorValue.Num;
                return true;
            }

            var sign = rawDays < 0 ? -1 : 1;
            var remaining = Math.Abs((int)rawDays);
            var workdaysPerWeek = CountFormulaWorkdaysPerWeek(weekendMask);
            try
            {
                if (remaining > workdaysPerWeek && holidays.Count == 0)
                {
                    var fullWeeks = (remaining - 1) / workdaysPerWeek;
                    current = current.AddDays((long)sign * fullWeeks * 7);
                    remaining -= fullWeeks * workdaysPerWeek;
                }

                while (remaining > 0)
                {
                    current = current.AddDays(sign);
                    if (!weekendMask[FormulaExcelDowToMonIndex(current)] &&
                        !holidays.Contains(current.Date))
                    {
                        remaining--;
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryGetFormulaDateSerial(current, out var serial))
            {
                value = ErrorValue.Num;
                return true;
            }

            value = new NumberValue(serial);
            return true;
        }

        private bool TryEvaluateFormulaNetworkdays(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryPropagateFormulaWorkdayArgumentErrors(function, rowOffset, colOffset, out var argumentError))
            {
                if (argumentError is not null)
                {
                    value = argumentError;
                    return true;
                }

                return false;
            }

            if (!TryResolveFormulaWorkdayWeekendMask(function, rowOffset, colOffset, out var weekendMask, out var weekendError))
            {
                if (weekendError is not null)
                {
                    value = weekendError;
                    return true;
                }

                return false;
            }

            var holidayArgumentIndex = function.Kind == ConditionalFormulaScalarFunctionKind.NetworkdaysIntl ? 3 : 2;
            if (!TryCollectFormulaWorkdayHolidays(function, holidayArgumentIndex, rowOffset, colOffset, out var holidays, out var holidayError))
            {
                value = holidayError ?? ErrorValue.Value;
                return holidayError is not null;
            }

            if (!TryResolveFormulaWorkdayDate(function.Arguments[0], rowOffset, colOffset, out var startRaw, out var startError))
            {
                if (startError is not null)
                {
                    value = startError;
                    return true;
                }

                return false;
            }

            if (!TryResolveFormulaWorkdayDate(function.Arguments[1], rowOffset, colOffset, out var endRaw, out var endError))
            {
                if (endError is not null)
                {
                    value = endError;
                    return true;
                }

                return false;
            }

            var start = startRaw.Date;
            var end = endRaw.Date;
            var sign = start <= end ? 1 : -1;
            var lo = start <= end ? start : end;
            var hi = start <= end ? end : start;
            var count = CountFormulaWorkdaysInclusive(lo, hi, weekendMask);
            foreach (var holiday in holidays)
            {
                if (holiday >= lo &&
                    holiday <= hi &&
                    !weekendMask[FormulaExcelDowToMonIndex(holiday)])
                {
                    count--;
                }
            }

            value = new NumberValue(sign * count);
            return true;
        }

        private bool TryPropagateFormulaWorkdayArgumentErrors(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ErrorValue? error)
        {
            error = null;
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                var argument = function.Arguments[i];
                if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                    continue;

                if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var value))
                    return false;

                if (value is ErrorValue argumentError)
                {
                    error = argumentError;
                    return false;
                }
            }

            return true;
        }

        private bool TryResolveFormulaWorkdayWeekendMask(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out bool[] weekendMask,
            out ErrorValue? error)
        {
            weekendMask = CreateDefaultFormulaWorkdayWeekendMask();
            error = null;
            if (function.Kind is not ConditionalFormulaScalarFunctionKind.WorkdayIntl and
                not ConditionalFormulaScalarFunctionKind.NetworkdaysIntl)
            {
                return true;
            }

            if (function.Arguments.Count < 3)
                return true;

            if (!TryResolveFormulaOperand(function.Arguments[2], rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return false;
            }

            return TryGetFormulaWorkdayWeekendMask(value, out weekendMask, out error);
        }

        private bool TryResolveFormulaWorkdayDate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out DateTime date,
            out ErrorValue? error)
        {
            date = default;
            error = null;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return false;
            }

            if (value is DateTimeValue dateTime)
            {
                if (TrySerialToDate(dateTime.Value, out date) &&
                    IsValidFormulaWorkdaySerial(dateTime.Value))
                {
                    return true;
                }

                error = ErrorValue.Num;
                return false;
            }

            if (!TryGetFormulaWorkdayNumber(value, out var serial))
            {
                error = ErrorValue.Value;
                return false;
            }

            if (!IsValidFormulaWorkdaySerial(serial))
            {
                error = ErrorValue.Num;
                return false;
            }

            try
            {
                date = FormulaExcelSerialToDate(serial).Date;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                error = ErrorValue.Num;
                return false;
            }
        }

        private bool TryResolveFormulaWorkdayNumber(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out double number,
            out ErrorValue? error)
        {
            number = 0;
            error = null;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return false;
            }

            if (TryGetFormulaWorkdayNumber(value, out number))
                return true;

            error = ErrorValue.Value;
            return false;
        }

        private bool TryCollectFormulaWorkdayHolidays(
            ConditionalFormulaScalarFunction function,
            int argumentIndex,
            int rowOffset,
            int colOffset,
            out HashSet<DateTime> holidays,
            out ErrorValue? error)
        {
            holidays = [];
            error = null;
            if (function.Arguments.Count <= argumentIndex)
                return true;

            var operand = function.Arguments[argumentIndex];
            if (operand.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryResolveFormulaReferenceRange(
                        operand,
                        rowOffset,
                        colOffset,
                        out var targetSheet,
                        out var startRow,
                        out var startCol,
                        out var endRow,
                        out var endCol))
                {
                    return false;
                }

                var rowCount = (ulong)endRow - startRow + 1UL;
                var colCount = (ulong)endCol - startCol + 1UL;
                if (rowCount * colCount > MaxFormulaAggregateRangeCells)
                    return false;

                for (var currentRow = startRow; currentRow <= endRow; currentRow++)
                {
                    for (var currentCol = startCol; currentCol <= endCol; currentCol++)
                    {
                        if (!TryAppendFormulaWorkdayHoliday(targetSheet.GetValue(currentRow, currentCol), holidays, out error))
                            return false;
                    }
                }

                return true;
            }

            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            return TryAppendFormulaWorkdayHoliday(value, holidays, out error);
        }

        private static bool TryAppendFormulaWorkdayHoliday(
            ScalarValue value,
            HashSet<DateTime> holidays,
            out ErrorValue? error)
        {
            error = null;
            if (value is ErrorValue valueError)
            {
                error = valueError;
                return false;
            }

            if (value is DateTimeValue dateTime)
            {
                if (!TrySerialToDate(dateTime.Value, out var holiday) ||
                    !IsValidFormulaWorkdaySerial(dateTime.Value))
                {
                    error = ErrorValue.Num;
                    return false;
                }

                holidays.Add(holiday.Date);
                return true;
            }

            if (value is not NumberValue numeric)
                return true;

            if (!IsValidFormulaWorkdaySerial(numeric.Value))
            {
                error = ErrorValue.Num;
                return false;
            }

            try
            {
                holidays.Add(FormulaExcelSerialToDate(numeric.Value).Date);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                error = ErrorValue.Num;
                return false;
            }
        }

        private static bool TryGetFormulaWorkdayNumber(ScalarValue value, out double number) =>
            value switch
            {
                NumberValue numeric => TryFiniteFormulaWorkdayNumber(numeric.Value, out number),
                DateTimeValue dateTime => TryFiniteFormulaWorkdayNumber(dateTime.Value, out number),
                BoolValue boolean => TryFiniteFormulaWorkdayNumber(boolean.Value ? 1d : 0d, out number),
                BlankValue => TryFiniteFormulaWorkdayNumber(0d, out number),
                TextValue text when TryParseFormulaValueText(text.Value, out var parsed) =>
                    TryFiniteFormulaWorkdayNumber(parsed, out number),
                _ => TryFiniteFormulaWorkdayNumber(double.NaN, out number)
            };

        private static bool TryFiniteFormulaWorkdayNumber(double candidate, out double number)
        {
            number = candidate;
            return double.IsFinite(number);
        }

        private static bool IsValidFormulaWorkdaySerial(double serial) =>
            double.IsFinite(serial) && serial >= 0 && serial <= 2958465.0;

        private static bool[] CreateDefaultFormulaWorkdayWeekendMask()
        {
            var mask = new bool[7];
            mask[5] = true;
            mask[6] = true;
            return mask;
        }

        private static bool TryGetFormulaWorkdayWeekendMask(
            ScalarValue value,
            out bool[] mask,
            out ErrorValue? error)
        {
            mask = new bool[7];
            error = null;
            if (value is BlankValue)
            {
                mask = CreateDefaultFormulaWorkdayWeekendMask();
                return true;
            }

            if (value is TextValue text)
            {
                var pattern = text.Value;
                if (pattern.Length != 7 ||
                    pattern.Any(static c => c is not '0' and not '1') ||
                    pattern.All(static c => c == '1'))
                {
                    error = ErrorValue.Value;
                    return false;
                }

                for (var i = 0; i < pattern.Length; i++)
                    mask[i] = pattern[i] == '1';

                return true;
            }

            if (!TryGetFormulaWorkdayWeekendNumber(value, out var rawCode))
            {
                error = ErrorValue.Value;
                return false;
            }

            if (!double.IsFinite(rawCode))
            {
                error = ErrorValue.Value;
                return false;
            }

            var code = (int)rawCode;
            switch (code)
            {
                case 1:
                    mask[5] = true;
                    mask[6] = true;
                    break;
                case 2:
                    mask[6] = true;
                    mask[0] = true;
                    break;
                case 3:
                    mask[0] = true;
                    mask[1] = true;
                    break;
                case 4:
                    mask[1] = true;
                    mask[2] = true;
                    break;
                case 5:
                    mask[2] = true;
                    mask[3] = true;
                    break;
                case 6:
                    mask[3] = true;
                    mask[4] = true;
                    break;
                case 7:
                    mask[4] = true;
                    mask[5] = true;
                    break;
                case 11:
                    mask[6] = true;
                    break;
                case 12:
                    mask[0] = true;
                    break;
                case 13:
                    mask[1] = true;
                    break;
                case 14:
                    mask[2] = true;
                    break;
                case 15:
                    mask[3] = true;
                    break;
                case 16:
                    mask[4] = true;
                    break;
                case 17:
                    mask[5] = true;
                    break;
                default:
                    error = ErrorValue.Num;
                    return false;
            }

            return true;
        }

        private static bool TryGetFormulaWorkdayWeekendNumber(ScalarValue value, out double number) =>
            value switch
            {
                NumberValue numeric => TryFiniteFormulaWorkdayNumber(numeric.Value, out number),
                DateTimeValue dateTime => TryFiniteFormulaWorkdayNumber(dateTime.Value, out number),
                BoolValue boolean => TryFiniteFormulaWorkdayNumber(boolean.Value ? 1d : 0d, out number),
                _ => TryFiniteFormulaWorkdayNumber(double.NaN, out number)
            };

        private static int CountFormulaWorkdaysPerWeek(IReadOnlyList<bool> weekendMask)
        {
            var count = 0;
            for (var i = 0; i < weekendMask.Count; i++)
            {
                if (!weekendMask[i])
                    count++;
            }

            return count;
        }

        private static int CountFormulaWorkdaysInclusive(DateTime lo, DateTime hi, IReadOnlyList<bool> weekendMask)
        {
            var totalDays = (int)(hi - lo).TotalDays + 1;
            var fullWeeks = totalDays / 7;
            var count = fullWeeks * CountFormulaWorkdaysPerWeek(weekendMask);
            var startDow = FormulaExcelDowToMonIndex(lo);
            for (var i = 0; i < totalDays % 7; i++)
            {
                var dow = (startDow + i) % 7;
                if (!weekendMask[dow])
                    count++;
            }

            return count;
        }

        private bool TryEvaluateFormulaYearfrac(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0;
            if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var startDate) ||
                !TryResolveFormulaFunctionDate(function.Arguments[1], rowOffset, colOffset, out var endDate))
            {
                return false;
            }

            var basis = 0;
            if (function.Arguments.Count == 3)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out var rawBasis) ||
                    !double.IsFinite(rawBasis))
                {
                    return false;
                }

                basis = (int)rawBasis;
                if (basis is < 0 or > 4)
                    return false;
            }

            var start = startDate.Date;
            var end = endDate.Date;
            var totalDays = FormulaDateToExcelSerial(end) - FormulaDateToExcelSerial(start);
            result = basis switch
            {
                1 => totalDays / FormulaActualActualYearfracDenominator(start, end),
                2 => totalDays / 360.0,
                3 => totalDays / 365.0,
                4 => FormulaDays30E360(start, end) / 360.0,
                _ => FormulaDays30US360(start, end) / 360.0
            };

            return double.IsFinite(result);
        }

        private static double FormulaActualActualYearfracDenominator(DateTime start, DateTime end)
        {
            if (start > end)
                (start, end) = (end, start);

            if (start.Year == end.Year)
                return DateTime.IsLeapYear(start.Year) ? 366.0 : 365.0;

            var total = 0.0;
            for (var year = start.Year; year <= end.Year; year++)
                total += DateTime.IsLeapYear(year) ? 366.0 : 365.0;

            return total / (end.Year - start.Year + 1);
        }

        private static double FormulaDays30US360(DateTime start, DateTime end)
        {
            var startDay = start.Day;
            var endDay = end.Day;

            if (IsFormulaYearfracNasdLastDayOfFebruary(start))
                startDay = 30;
            if (IsFormulaYearfracNasdLastDayOfFebruary(end) && startDay == 30)
                endDay = 30;
            if (startDay == 31)
                startDay = 30;
            if (endDay == 31 && startDay == 30)
                endDay = 30;

            return 360.0 * (end.Year - start.Year) +
                30.0 * (end.Month - start.Month) +
                (endDay - startDay);
        }

        private static bool IsFormulaYearfracNasdLastDayOfFebruary(DateTime date) =>
            date.Year != 1900 &&
            date.Month == 2 &&
            date.Day == DateTime.DaysInMonth(date.Year, date.Month);

        private static double FormulaDays30E360(DateTime start, DateTime end)
        {
            var startDay = start.Day == 31 ? 30 : start.Day;
            var endDay = end.Day == 31 ? 30 : end.Day;

            return 360.0 * (end.Year - start.Year) +
                30.0 * (end.Month - start.Month) +
                (endDay - startDay);
        }

        private bool TryEvaluateFormulaEDate(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out double serial)
        {
            serial = 0;
            if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var startDate) ||
                !TryResolveFormulaFunctionMonthOffset(function.Arguments[1], rowOffset, colOffset, int.MaxValue, out var months))
            {
                return false;
            }

            try
            {
                return TryGetFormulaDateSerial(startDate.AddMonths(months), out serial);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private bool TryEvaluateFormulaEOMonth(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out double serial)
        {
            serial = 0;
            if (!TryResolveFormulaFunctionDate(function.Arguments[0], rowOffset, colOffset, out var startDate) ||
                !TryResolveFormulaFunctionMonthOffset(function.Arguments[1], rowOffset, colOffset, int.MaxValue - 1d, out var months))
            {
                return false;
            }

            try
            {
                var monthStart = new DateTime(startDate.Year, startDate.Month, 1);
                return TryGetFormulaDateSerial(monthStart.AddMonths(months + 1).AddDays(-1), out serial);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private bool TryEvaluateFormulaTextSliceFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var text) ||
                !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var lengthNumber) ||
                !TryGetFormulaTextSliceLength(lengthNumber, out var length))
            {
                return false;
            }

            var actualLength = Math.Min(length, text.Length);
            value = function.Kind == ConditionalFormulaScalarFunctionKind.Left
                ? new TextValue(text[..actualLength])
                : new TextValue(text[(text.Length - actualLength)..]);
            return true;
        }

        private bool TryEvaluateFormulaTextMidFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var text) ||
                !TryResolveFormulaFunctionNumber(function.Arguments[1], rowOffset, colOffset, out var startNumber) ||
                !TryGetFormulaTextSearchStart(startNumber, out var startIndex) ||
                !TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out var lengthNumber) ||
                !TryGetFormulaTextSliceLength(lengthNumber, out var length))
            {
                return false;
            }

            if (startIndex >= text.Length || length == 0)
            {
                value = new TextValue(string.Empty);
                return true;
            }

            var actualLength = Math.Min(length, text.Length - startIndex);
            value = new TextValue(text.Substring(startIndex, actualLength));
            return true;
        }

        private bool TryEvaluateFormulaTextSearchFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFunctionText(function.Arguments[0], rowOffset, colOffset, out var findText) ||
                string.IsNullOrEmpty(findText) ||
                !TryResolveFormulaFunctionText(function.Arguments[1], rowOffset, colOffset, out var withinText))
            {
                return false;
            }

            var startIndex = 0;
            if (function.Arguments.Count == 3)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[2], rowOffset, colOffset, out var startNumber) ||
                    !TryGetFormulaTextSearchStart(startNumber, out startIndex))
                {
                    return false;
                }
            }

            if (startIndex > withinText.Length)
                return false;

            var comparison = function.Kind == ConditionalFormulaScalarFunctionKind.Find
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            var foundIndex = withinText.IndexOf(findText, startIndex, comparison);
            if (foundIndex < 0)
                return false;

            value = new NumberValue(foundIndex + 1);
            return true;
        }

        private static bool TryParseFormulaArabicRoman(string text, out int result)
        {
            result = 0;
            var normalized = text.ToUpperInvariant();
            if (normalized.Any(static c => c is not ('I' or 'V' or 'X' or 'L' or 'C' or 'D' or 'M')))
                return false;

            var thousands = 0;
            while (thousands < normalized.Length && normalized[thousands] == 'M')
                thousands++;

            var remainder = normalized[thousands..];
            if (!FormulaArabicRomanRemainders.TryGetValue(remainder, out var remainderValue))
                return false;

            result = thousands * 1000 + remainderValue;
            return result <= 255000;
        }

        private static IReadOnlyDictionary<string, int> BuildFormulaArabicRemainderMap()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var number = 0; number < 1000; number++)
            {
                for (var form = 0; form <= 4; form++)
                    map.TryAdd(ToFormulaRoman(number, form), number);
            }

            return map;
        }

        private static string ToFormulaRoman(int number, int form)
        {
            if (number == 0)
                return string.Empty;

            var remaining = number;
            var builder = new System.Text.StringBuilder();
            foreach (var (value, symbol) in FormulaRomanTokens(form))
            {
                while (remaining >= value)
                {
                    builder.Append(symbol);
                    remaining -= value;
                }
            }

            return builder.ToString();
        }

        private static (int Value, string Symbol)[] FormulaRomanTokens(int form)
        {
            var tokens = new List<(int Value, string Symbol)>
            {
                (1000, "M"),
                (900, "CM"),
                (500, "D"),
                (400, "CD"),
                (100, "C"),
                (90, "XC"),
                (50, "L"),
                (40, "XL"),
                (10, "X"),
                (9, "IX"),
                (5, "V"),
                (4, "IV"),
                (1, "I")
            };

            if (form >= 1)
            {
                tokens.Add((950, "LM"));
                tokens.Add((450, "LD"));
                tokens.Add((95, "VC"));
                tokens.Add((45, "VL"));
            }

            if (form >= 2)
            {
                tokens.Add((990, "XM"));
                tokens.Add((490, "XD"));
                tokens.Add((99, "IC"));
                tokens.Add((49, "IL"));
            }

            if (form >= 3)
            {
                tokens.Add((995, "VM"));
                tokens.Add((495, "VD"));
            }

            if (form >= 4)
            {
                tokens.Add((999, "IM"));
                tokens.Add((499, "ID"));
            }

            return tokens
                .OrderByDescending(static token => token.Value)
                .ThenBy(static token => token.Symbol.Length)
                .ToArray();
        }

        private bool TryResolveFormulaFunctionNumber(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out double number)
        {
            number = 0;
            return TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value) &&
                TryGetFormulaArithmeticNumber(value, out number);
        }

        private bool TryResolveFormulaFunctionText(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out string text)
        {
            text = string.Empty;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value) ||
                value is not TextValue textValue)
            {
                return false;
            }

            text = textValue.Value;
            return true;
        }

        private static bool TryGetFormulaCoercedNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    break;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    break;
                case BoolValue boolean:
                    number = boolean.Value ? 1 : 0;
                    break;
                case BlankValue:
                    number = 0;
                    break;
                case TextValue text when TryParseFormulaValueText(text.Value, out var parsed):
                    number = parsed;
                    break;
                default:
                    number = 0;
                    return false;
            }

            return double.IsFinite(number);
        }

        private static bool TryGetFormulaCoercedText(ScalarValue value, out string text)
        {
            text = value switch
            {
                TextValue textValue => textValue.Value,
                NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DateTimeValue dateTime => dateTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                BlankValue => string.Empty,
                _ => string.Empty
            };

            return value is not ErrorValue && value is not RangeValue;
        }

        private bool TryResolveFormulaDatePart(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out int part)
        {
            part = 0;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value) ||
                value is not NumberValue number)
            {
                return false;
            }

            return TryGetFormulaInteger(number.Value, out part);
        }

        private bool TryResolveFormulaTimePart(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out int part)
        {
            part = 0;
            if (!TryResolveFormulaFunctionNumber(operand, rowOffset, colOffset, out var number) ||
                !double.IsFinite(number) ||
                number < 0 ||
                number > 32767)
            {
                return false;
            }

            part = (int)Math.Truncate(number);
            return true;
        }

        private bool TryResolveFormulaFunctionDate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out DateTime date)
        {
            date = default;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value))
                return false;

            return value switch
            {
                DateTimeValue dateTime => TrySerialToDate(dateTime.Value, out date),
                NumberValue number => TrySerialToDate(number.Value, out date),
                _ => false
            };
        }

        private bool TryResolveFormulaFunctionDateSerial(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out double serial)
        {
            serial = 0;
            return TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value) &&
                TryGetFormulaArithmeticNumber(value, out serial) &&
                IsValidFormulaWeekDateSerial(serial);
        }

        private bool TryResolveFormulaFunctionMonthOffset(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            double maxValue,
            out int months)
        {
            months = 0;
            if (!TryResolveFormulaFunctionNumber(operand, rowOffset, colOffset, out var number) ||
                !double.IsFinite(number) ||
                number < int.MinValue ||
                number > maxValue)
            {
                return false;
            }

            months = (int)number;
            return true;
        }

        private bool TryResolveFormulaFunctionTimeParts(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out int hour,
            out int minute,
            out int second)
        {
            hour = 0;
            minute = 0;
            second = 0;
            if (!TryResolveFormulaOperand(operand, rowOffset, colOffset, out var value) ||
                !TryGetFormulaArithmeticNumber(value, out var serial) ||
                !IsValidFormulaTimeSerial(serial))
            {
                return false;
            }

            var fraction = serial - Math.Floor(serial);
            var totalSeconds = (int)Math.Floor(fraction * 86400.0 + 1e-9) % 86400;
            hour = totalSeconds / 3600;
            minute = totalSeconds % 3600 / 60;
            second = totalSeconds % 60;
            return true;
        }

        private bool TryResolveFormulaOptionalReturnType(
            ConditionalFormulaScalarFunction function,
            int argumentIndex,
            int rowOffset,
            int colOffset,
            int defaultValue,
            out int returnType)
        {
            returnType = defaultValue;
            if (function.Arguments.Count <= argumentIndex)
                return true;

            if (!TryResolveFormulaFunctionNumber(function.Arguments[argumentIndex], rowOffset, colOffset, out var rawReturnType) ||
                !double.IsFinite(rawReturnType) ||
                rawReturnType < int.MinValue ||
                rawReturnType > int.MaxValue)
            {
                return false;
            }

            returnType = (int)rawReturnType;
            return true;
        }

        private static bool TryEvaluateFormulaWeekday(double serial, int returnType, out int weekday)
        {
            weekday = 0;
            var daySerial = (int)Math.Floor(serial);
            var dow = ((daySerial - 1) % 7 + 7) % 7;
            weekday = returnType switch
            {
                1 => dow + 1,
                2 or 11 => dow == 0 ? 7 : dow,
                3 => dow == 0 ? 6 : dow - 1,
                >= 12 and <= 17 => ((dow - (returnType - 10) + 7) % 7) + 1,
                _ => 0
            };

            return weekday != 0 || returnType == 3;
        }

        private static bool TryEvaluateFormulaWeeknum(double serial, int returnType, out int weeknum)
        {
            weeknum = 0;
            if (returnType == 21)
                return TryEvaluateFormulaIsoWeeknum(serial, out weeknum);

            var daySerial = (int)Math.Floor(serial);
            if (daySerial == 0)
                return true;

            var firstDay = returnType switch
            {
                1 or 17 => 6,
                2 or 11 => 0,
                12 => 1,
                13 => 2,
                14 => 3,
                15 => 4,
                16 => 5,
                _ => -1
            };
            if (firstDay < 0)
                return false;

            try
            {
                var date = FormulaExcelSerialToDate(serial);
                var jan1 = new DateTime(date.Year, 1, 1);
                var jan1Dow = (FormulaExcelDowToMonIndex(jan1) - firstDay + 7) % 7;
                var dayOfYear = (date.Date - jan1).Days;
                weeknum = (dayOfYear + jan1Dow) / 7 + 1;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static bool TryEvaluateFormulaIsoWeeknum(double serial, out int weeknum)
        {
            weeknum = 0;
            if (!IsValidFormulaWeekDateSerial(serial))
                return false;

            try
            {
                var daySerial = (int)Math.Floor(serial);
                var dowMon0 = FormulaExcelDowToMonIndex(daySerial);
                var thursdaySerial = daySerial + (3 - dowMon0);
                var weekYear = FormulaExcelSerialToDate(thursdaySerial).Year;
                var jan4Serial = (int)Math.Floor(FormulaDateToExcelSerial(new DateTime(weekYear, 1, 4)));
                var week1MondaySerial = jan4Serial - FormulaExcelDowToMonIndex(jan4Serial);
                weeknum = (daySerial - week1MondaySerial) / 7 + 1;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static bool IsValidFormulaWeekDateSerial(double serial) =>
            double.IsFinite(serial) && serial >= 0 && serial < 2958466.0;

        private static bool IsValidFormulaTimeSerial(double serial) =>
            double.IsFinite(serial) && serial >= 0 && serial <= 2958465.0;

        private static int FormulaExcelDowToMonIndex(DateTime date)
        {
            var serial = (int)Math.Floor(FormulaDateToExcelSerial(date));
            return FormulaExcelDowToMonIndex(serial);
        }

        private static int FormulaExcelDowToMonIndex(int serial) => ((serial + 5) % 7 + 7) % 7;

        private static DateTime FormulaExcelSerialToDate(double serial) =>
            new DateTime(1899, 12, 30).AddDays(serial < 60 ? serial + 1 : serial);

        private static double FormulaDateToExcelSerial(DateTime date)
        {
            var serial = (date - new DateTime(1899, 12, 30)).TotalDays;
            return date < new DateTime(1900, 3, 1) ? serial - 1 : serial;
        }

        private static bool TryGetFormulaDateSerial(DateTime date, out double serial)
        {
            serial = FormulaDateToExcelSerial(date.Date);
            return double.IsFinite(serial);
        }

        private static bool TryCreateFormulaDateValue(
            int year,
            int month,
            int day,
            out DateTimeValue value)
        {
            value = default!;
            if (year is < 1 or > 9999 ||
                month is < 1 or > 12 ||
                day < 1 ||
                day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            var date = new DateTime(year, month, day);
            try
            {
                var serial = date.ToOADate();
                if (!double.IsFinite(serial))
                    return false;

                value = new DateTimeValue(serial);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TrySerialToDate(double serial, out DateTime date)
        {
            date = default;
            if (!double.IsFinite(serial))
                return false;

            try
            {
                date = DateTime.FromOADate(serial).Date;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsLastDayOfFebruary(DateTime date) =>
            date.Year != 1900 &&
            date.Month == 2 &&
            date.Day == DateTime.DaysInMonth(date.Year, date.Month);

        private static bool TryGetFormulaInteger(double value, out int integer)
        {
            integer = 0;
            var rounded = Math.Round(value);
            if (!double.IsFinite(rounded) ||
                Math.Abs(value - rounded) > 1e-9 ||
                rounded < int.MinValue ||
                rounded > int.MaxValue)
            {
                return false;
            }

            integer = (int)rounded;
            return true;
        }

        private static bool TryGetFormulaRoundDigits(double value, out int digits)
        {
            digits = 0;
            var rounded = Math.Round(value);
            if (!double.IsFinite(rounded) ||
                Math.Abs(value - rounded) > 1e-9 ||
                rounded < -MaxFormulaRoundDigits ||
                rounded > MaxFormulaRoundDigits)
            {
                return false;
            }

            digits = (int)rounded;
            return true;
        }

        private static bool TryGetFormulaTextSliceLength(double value, out int length)
        {
            length = 0;
            var rounded = Math.Round(value);
            if (!double.IsFinite(rounded) ||
                Math.Abs(value - rounded) > 1e-9 ||
                rounded < 0 ||
                rounded > MaxFormulaTextSliceLength)
            {
                return false;
            }

            length = (int)rounded;
            return true;
        }

        private static bool TryGetFormulaTextSearchStart(double value, out int startIndex)
        {
            startIndex = 0;
            var rounded = Math.Round(value);
            if (!double.IsFinite(rounded) ||
                Math.Abs(value - rounded) > 1e-9 ||
                rounded < 1 ||
                rounded > MaxFormulaTextSliceLength)
            {
                return false;
            }

            startIndex = (int)rounded - 1;
            return true;
        }

        private static double RoundFormulaNumber(double value, int digits)
        {
            if (digits >= 0)
                return Math.Round(value, digits, MidpointRounding.AwayFromZero);

            var factor = Math.Pow(10d, -digits);
            return Math.Round(value / factor, 0, MidpointRounding.AwayFromZero) * factor;
        }

        private static double EvenFormulaNumber(double value)
        {
            if (value == 0d)
                return 0d;

            var magnitude = Math.Abs(value);
            var evenMagnitude = magnitude <= 2d
                ? 2d
                : Math.Ceiling(magnitude / 2d) * 2d;
            return Math.Sign(value) * evenMagnitude;
        }

        private static double OddFormulaNumber(double value)
        {
            if (value == 0d)
                return 1d;

            var magnitude = Math.Abs(value);
            var oddMagnitude = Math.Ceiling(magnitude);
            if (oddMagnitude % 2d == 0d)
                oddMagnitude += 1d;

            return Math.Sign(value) * oddMagnitude;
        }

        private static double RoundUpFormulaNumber(double value, int digits)
        {
            if (value == 0)
                return value;

            var magnitude = Math.Abs(value);
            var factor = Math.Pow(10d, Math.Abs(digits));
            if (digits >= 0)
            {
                if (magnitude > double.MaxValue / factor)
                    return value;

                return Math.Sign(value) * RoundUpFormulaScaledMagnitude(magnitude * factor) / factor;
            }

            return Math.Sign(value) * RoundUpFormulaScaledMagnitude(magnitude / factor) * factor;
        }

        private static double RoundUpFormulaScaledMagnitude(double value)
        {
            var nearest = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (double.IsFinite(nearest) &&
                Math.Abs(value - nearest) <= 1e-12 * Math.Max(1d, Math.Abs(value)))
            {
                return nearest;
            }

            return Math.Ceiling(value);
        }

        private static double RoundDownFormulaNumber(double value, int digits)
        {
            if (value == 0)
                return value;

            var magnitude = Math.Abs(value);
            var factor = Math.Pow(10d, Math.Abs(digits));
            if (digits >= 0)
            {
                if (magnitude > double.MaxValue / factor)
                    return value;

                return Math.Sign(value) * RoundDownFormulaScaledMagnitude(magnitude * factor) / factor;
            }

            return Math.Sign(value) * RoundDownFormulaScaledMagnitude(magnitude / factor) * factor;
        }

        private static double RoundDownFormulaScaledMagnitude(double value)
        {
            var nearest = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (double.IsFinite(nearest) &&
                Math.Abs(value - nearest) <= 1e-12 * Math.Max(1d, Math.Abs(value)))
            {
                return nearest;
            }

            return Math.Floor(value);
        }

        private static bool TryMRoundFormulaNumber(double number, double multiple, out double result)
        {
            result = 0d;
            if (multiple == 0d || number == 0d)
                return true;

            if (number > 0d && multiple < 0d ||
                number < 0d && multiple > 0d)
            {
                return false;
            }

            var roundedMultiple = Math.Round(number / multiple, 0, MidpointRounding.AwayFromZero);
            result = roundedMultiple * multiple;
            return double.IsFinite(result);
        }

        private static bool TryCeilingFormulaNumber(double number, double significance, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance))
                return false;

            if (significance == 0d)
                return true;

            if (number > 0d && significance < 0d)
                return false;

            result = Math.Ceiling(number / significance) * significance;
            return double.IsFinite(result);
        }

        private static bool TryCeilingMathFormulaNumber(double number, double significance, double mode, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance) || !double.IsFinite(mode))
                return false;

            if (number == 0d || significance == 0d)
                return true;

            var multiple = Math.Abs(significance);
            result = number < 0d && mode != 0d
                ? Math.Floor(number / multiple) * multiple
                : Math.Ceiling(number / multiple) * multiple;
            return double.IsFinite(result);
        }

        private static bool TryIsoCeilingFormulaNumber(double number, double significance, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance))
                return false;

            if (number == 0d || significance == 0d)
                return true;

            var multiple = Math.Abs(significance);
            result = Math.Ceiling(number / multiple) * multiple;
            return double.IsFinite(result);
        }

        private static bool TryFloorFormulaNumber(double number, double significance, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance))
                return false;

            if (significance == 0d)
                return true;

            if (number * significance < 0d)
                return false;

            result = Math.Floor(number / significance) * significance;
            return double.IsFinite(result);
        }

        private static bool TryFloorPreciseFormulaNumber(double number, double significance, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance))
                return false;

            if (number == 0d || significance == 0d)
                return true;

            var multiple = Math.Abs(significance);
            result = Math.Floor(number / multiple) * multiple;
            return double.IsFinite(result);
        }

        private static bool TryFloorMathFormulaNumber(double number, double significance, double mode, out double result)
        {
            result = 0d;
            if (!double.IsFinite(number) || !double.IsFinite(significance) || !double.IsFinite(mode))
                return false;

            if (number == 0d || significance == 0d)
                return true;

            var multiple = Math.Abs(significance);
            result = number < 0d && mode != 0d
                ? Math.Truncate(number / multiple) * multiple
                : Math.Floor(number / multiple) * multiple;
            return double.IsFinite(result);
        }

        private static double FactorialFormulaNumber(int value)
        {
            var result = 1d;
            for (var factor = 2; factor <= value; factor++)
            {
                result *= factor;
            }

            return result;
        }

        private static bool TryDoubleFactorialFormulaNumber(int value, out double result)
        {
            result = 1d;
            for (var factor = value; factor > 1; factor -= 2)
            {
                if (result > double.MaxValue / factor)
                    return false;

                result *= factor;
            }

            return double.IsFinite(result);
        }

        private static bool TryCombinFormulaNumber(double number, double numberChosen, out double result)
        {
            result = 0d;
            if (!TryGetFormulaCombinInteger(number, out var n) ||
                !TryGetFormulaCombinInteger(numberChosen, out var k) ||
                k > n)
            {
                return false;
            }

            return TryCombinFormulaIntegers(n, k, out result);
        }

        private static bool TryCombinaFormulaNumber(double number, double numberChosen, out double result)
        {
            result = 0d;
            if (!TryGetFormulaCombinInteger(number, out var n) ||
                !TryGetFormulaCombinInteger(numberChosen, out var k))
            {
                return false;
            }

            if (n == 0 && k > 0)
                return false;

            if (k == 0)
            {
                result = 1d;
                return true;
            }

            if (k == 1)
            {
                result = n;
                return true;
            }

            if (n > int.MaxValue - k + 1)
                return false;

            var repetitionsN = n + k - 1;
            if (repetitionsN > MaxFormulaCombinaCombinationInput)
                return false;

            return TryCombinFormulaIntegers(repetitionsN, k, out result);
        }

        private static bool TryCombinFormulaIntegers(int n, int k, out double result)
        {
            result = 0d;
            if (n < 0 ||
                k < 0 ||
                k > n)
            {
                return false;
            }

            k = Math.Min(k, n - k);
            if (k > MaxFormulaCombinIterations)
                return false;

            result = 1d;
            for (var i = 1; i <= k; i++)
            {
                var numerator = n - k + i;
                if (numerator <= 0 ||
                    result > double.MaxValue / numerator)
                {
                    return false;
                }

                result *= numerator;
                if (!double.IsFinite(result))
                    return false;

                result /= i;
                if (!double.IsFinite(result))
                    return false;
            }

            return true;
        }

        private static bool TryGetFormulaCombinInteger(double value, out int integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                value < 0d)
                return false;

            var truncated = Math.Truncate(value);
            if (!double.IsFinite(truncated) ||
                truncated > MaxFormulaCombinInput)
            {
                return false;
            }

            integer = (int)truncated;
            return true;
        }

        private static bool TryPermutFormulaNumber(double number, double numberChosen, out double result)
        {
            result = 0d;
            if (!TryGetFormulaPermutInteger(number, out var n) ||
                !TryGetFormulaPermutInteger(numberChosen, out var k) ||
                k > n ||
                k > MaxFormulaPermutIterations)
            {
                return false;
            }

            result = 1d;
            for (var factor = n - k + 1; factor <= n; factor++)
            {
                if (factor <= 0 ||
                    result > double.MaxValue / factor)
                {
                    return false;
                }

                result *= factor;
                if (!double.IsFinite(result))
                    return false;
            }

            return true;
        }

        private static bool TryGetFormulaPermutInteger(double value, out int integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                value < 0d)
                return false;

            var truncated = Math.Truncate(value);
            if (!double.IsFinite(truncated) ||
                truncated > MaxFormulaPermutInput)
            {
                return false;
            }

            integer = (int)truncated;
            return true;
        }

        private static bool TryPermutationAFormulaNumber(double number, double numberChosen, out double result)
        {
            result = 0d;
            if (!TryGetFormulaPermutationAInteger(number, out var n) ||
                !TryGetFormulaPermutationAInteger(numberChosen, out var k))
            {
                return false;
            }

            if (n == 0 && k > 0)
                return false;

            result = Math.Pow(n, k);
            return double.IsFinite(result);
        }

        private static bool TryGetFormulaPermutationAInteger(double value, out int integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                value < 0d ||
                value > MaxFormulaPermutationAInput)
            {
                return false;
            }

            var truncated = Math.Truncate(value);
            if (!double.IsFinite(truncated))
                return false;

            integer = (int)truncated;
            return true;
        }

        private bool TryMultinomialFormulaNumber(
            ConditionalFormulaScalarFunction function,
            double first,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 1d;
            if (!TryGetFormulaMultinomialInteger(first, out var firstInteger))
                return false;

            long runningSum = 0;
            if (!TryAppendMultinomialTerm(firstInteger, ref runningSum, ref result))
                return false;

            for (var i = 1; i < function.Arguments.Count; i++)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[i], rowOffset, colOffset, out var number) ||
                    !TryGetFormulaMultinomialInteger(number, out var next) ||
                    !TryAppendMultinomialTerm(next, ref runningSum, ref result))
                {
                    return false;
                }
            }

            return double.IsFinite(result);
        }

        private static bool TryGetFormulaMultinomialInteger(double value, out int integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                value < 0d)
            {
                return false;
            }

            var truncated = Math.Truncate(value);
            if (!double.IsFinite(truncated) ||
                truncated > int.MaxValue)
            {
                return false;
            }

            integer = (int)truncated;
            return true;
        }

        private static bool TryAppendMultinomialTerm(
            int argument,
            ref long runningSum,
            ref double result)
        {
            var nextSum = runningSum + argument;
            if (nextSum > int.MaxValue ||
                !TryCombinFormulaIntegers((int)nextSum, argument, out var term) ||
                result > double.MaxValue / term)
            {
                return false;
            }

            result *= term;
            if (!double.IsFinite(result))
                return false;

            runningSum = nextSum;
            return true;
        }

        private bool TryGcdFormulaNumber(
            ConditionalFormulaScalarFunction function,
            double first,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0d;
            if (!TryGetFormulaGcdInteger(first, out var gcd))
                return false;

            for (var i = 1; i < function.Arguments.Count; i++)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[i], rowOffset, colOffset, out var number) ||
                    !TryGetFormulaGcdInteger(number, out var next))
                {
                    return false;
                }

                gcd = GcdFormulaIntegers(gcd, next);
            }

            result = gcd;
            return double.IsFinite(result);
        }

        private static bool TryGetFormulaGcdInteger(double value, out long integer)
        {
            integer = 0;
            if (!double.IsFinite(value) ||
                value < 0d ||
                value >= MaxFormulaGcdInputExclusive)
            {
                return false;
            }

            var truncated = Math.Truncate(value);
            if (!double.IsFinite(truncated))
                return false;

            integer = (long)truncated;
            return true;
        }

        private static long GcdFormulaIntegers(long first, long second)
        {
            while (second != 0)
            {
                var next = second;
                second = first % second;
                first = next;
            }

            return first;
        }

        private bool TryLcmFormulaNumber(
            ConditionalFormulaScalarFunction function,
            double first,
            int rowOffset,
            int colOffset,
            out double result)
        {
            result = 0d;
            if (!TryGetFormulaGcdInteger(first, out var lcm))
                return false;

            var hasZeroOperand = lcm == 0;
            for (var i = 1; i < function.Arguments.Count; i++)
            {
                if (!TryResolveFormulaFunctionNumber(function.Arguments[i], rowOffset, colOffset, out var number) ||
                    !TryGetFormulaGcdInteger(number, out var next))
                {
                    return false;
                }

                if (next == 0)
                {
                    hasZeroOperand = true;
                    continue;
                }

                if (hasZeroOperand)
                    continue;

                var gcd = GcdFormulaIntegers(lcm, next);
                var quotient = lcm / gcd;
                if (quotient > long.MaxValue / next)
                    return false;

                lcm = quotient * next;
            }

            result = hasZeroOperand ? 0d : lcm;
            return double.IsFinite(result);
        }

        private bool TryEvaluateFormulaPairwiseAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: 2 } arguments)
                return false;

            if (!TryResolveFormulaPairwiseAggregateValues(arguments[0], rowOffset, colOffset, out var left) ||
                !TryResolveFormulaPairwiseAggregateValues(arguments[1], rowOffset, colOffset, out var right) ||
                left.RowCount != right.RowCount ||
                left.ColCount != right.ColCount ||
                left.Values.Count != right.Values.Count)
            {
                return false;
            }

            var total = 0d;
            for (var i = 0; i < left.Values.Count; i++)
            {
                if (!TryGetFormulaPairwiseAggregateNumber(left.Values[i], out var x, out var skipLeft))
                    return false;

                if (skipLeft)
                    continue;

                if (!TryGetFormulaPairwiseAggregateNumber(right.Values[i], out var y, out var skipRight))
                    return false;

                if (skipRight)
                    continue;

                if (!TryGetFormulaPairwiseAggregateTerm(operand.AggregateKind, x, y, out var term))
                    return false;

                total += term;
                if (!double.IsFinite(total))
                    return false;
            }

            value = new NumberValue(total);
            return true;
        }

        private bool TryEvaluateFormulaSumProductAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: > 0 } arguments ||
                arguments.Count > MaxFormulaSumProductArgumentCount)
            {
                return false;
            }

            var arrays = new ConditionalFormulaPairwiseAggregateValues[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                if (!TryResolveFormulaPairwiseAggregateValues(arguments[i], rowOffset, colOffset, out arrays[i]))
                    return false;

                if (i > 0 &&
                    (arrays[i].RowCount != arrays[0].RowCount ||
                     arrays[i].ColCount != arrays[0].ColCount ||
                     arrays[i].Values.Count != arrays[0].Values.Count))
                {
                    return false;
                }
            }

            var total = 0d;
            for (var valueIndex = 0; valueIndex < arrays[0].Values.Count; valueIndex++)
            {
                var product = 1d;
                for (var arrayIndex = 0; arrayIndex < arrays.Length; arrayIndex++)
                {
                    if (!TryGetFormulaSumProductNumber(arrays[arrayIndex].Values[valueIndex].Value, out var number))
                        return false;

                    product *= number;
                    if (!double.IsFinite(product))
                        return false;
                }

                total += product;
                if (!double.IsFinite(total))
                    return false;
            }

            value = new NumberValue(total);
            return true;
        }

        private static bool TryGetFormulaSumProductNumber(ScalarValue value, out double number)
        {
            switch (value)
            {
                case NumberValue numeric when double.IsFinite(numeric.Value):
                    number = numeric.Value;
                    return true;
                case DateTimeValue dateTime when double.IsFinite(dateTime.Value):
                    number = dateTime.Value;
                    return true;
                case ErrorValue:
                    number = 0d;
                    return false;
                default:
                    number = 0d;
                    return true;
            }
        }

        private bool TryResolveFormulaPairwiseAggregateValues(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out ConditionalFormulaPairwiseAggregateValues values)
        {
            values = default;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    values = SingleFormulaPairwiseAggregateValue(argument.Literal ?? BlankValue.Instance, isDirectArgument: true);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    values = SingleFormulaPairwiseAggregateValue(targetSheet.GetValue(row, col), isDirectArgument: false);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Range:
                    if (!TryResolveFormulaAggregateRange(
                            argument,
                            rowOffset,
                            colOffset,
                            out var rangeSheet,
                            out var startRow,
                            out var startCol,
                            out var endRow,
                            out var endCol))
                    {
                        return false;
                    }

                    var rowCount = (int)(endRow - startRow + 1);
                    var colCount = (int)(endCol - startCol + 1);
                    var cells = new ConditionalFormulaPairwiseAggregateValue[rowCount * colCount];
                    var index = 0;
                    for (var currentRow = startRow; currentRow <= endRow; currentRow++)
                    {
                        for (var currentCol = startCol; currentCol <= endCol; currentCol++)
                        {
                            cells[index++] = new ConditionalFormulaPairwiseAggregateValue(
                                rangeSheet.GetValue(currentRow, currentCol),
                                IsDirectArgument: false);
                        }
                    }

                    values = new ConditionalFormulaPairwiseAggregateValues(rowCount, colCount, cells);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue ||
                        !TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out var operandValue))
                    {
                        return false;
                    }

                    if (operandValue is RangeValue operandRange)
                    {
                        values = FormulaPairwiseAggregateValuesFromRange(operandRange);
                        return true;
                    }

                    values = SingleFormulaPairwiseAggregateValue(operandValue, isDirectArgument: true);
                    return true;
                default:
                    return false;
            }
        }

        private static ConditionalFormulaPairwiseAggregateValues SingleFormulaPairwiseAggregateValue(
            ScalarValue value,
            bool isDirectArgument)
        {
            var values = new[]
            {
                new ConditionalFormulaPairwiseAggregateValue(value, isDirectArgument)
            };

            return new ConditionalFormulaPairwiseAggregateValues(1, 1, values);
        }

        private static ConditionalFormulaPairwiseAggregateValues FormulaPairwiseAggregateValuesFromRange(RangeValue range)
        {
            var values = new ConditionalFormulaPairwiseAggregateValue[range.RowCount * range.ColCount];
            var index = 0;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    values[index++] = new ConditionalFormulaPairwiseAggregateValue(
                        range.Cells[row, col],
                        IsDirectArgument: false);
                }
            }

            return new ConditionalFormulaPairwiseAggregateValues(range.RowCount, range.ColCount, values);
        }

        private static bool TryGetFormulaPairwiseAggregateNumber(
            ConditionalFormulaPairwiseAggregateValue value,
            out double number,
            out bool skipPair)
        {
            number = 0;
            skipPair = false;
            if (value.Value is ErrorValue)
                return false;

            if (TryGetFormulaAggregateNumber(
                    value.Value,
                    ConditionalFormulaAggregateKind.SumXMy2,
                    value.IsDirectArgument,
                    out number,
                    out var unsupported))
            {
                return true;
            }

            if (unsupported || value.IsDirectArgument)
                return false;

            skipPair = true;
            return true;
        }

        private static bool TryGetFormulaPairwiseAggregateTerm(
            ConditionalFormulaAggregateKind aggregateKind,
            double x,
            double y,
            out double term)
        {
            term = aggregateKind switch
            {
                ConditionalFormulaAggregateKind.SumXMy2 => (x - y) * (x - y),
                ConditionalFormulaAggregateKind.SumX2My2 => x * x - y * y,
                ConditionalFormulaAggregateKind.SumX2Py2 => x * x + y * y,
                _ => double.NaN
            };

            return double.IsFinite(term);
        }

        private bool TryEvaluateFormulaAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateKind == ConditionalFormulaAggregateKind.SumProduct)
                return TryEvaluateFormulaSumProductAggregate(operand, rowOffset, colOffset, out value);

            if (IsFormulaPairwiseAggregate(operand.AggregateKind))
                return TryEvaluateFormulaPairwiseAggregate(operand, rowOffset, colOffset, out value);

            if (operand.AggregateArguments is not { Count: > 0 } arguments)
                return false;

            var numericValues = new List<double>();
            var nonBlankCount = 0;
            var blankCount = 0;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (!AppendFormulaAggregateValues(
                        arguments[i],
                        operand.AggregateKind,
                        rowOffset,
                        colOffset,
                        numericValues,
                        ref nonBlankCount,
                        ref blankCount))
                {
                    return false;
                }
            }

            value = operand.AggregateKind switch
            {
                ConditionalFormulaAggregateKind.Sum => new NumberValue(numericValues.Sum()),
                ConditionalFormulaAggregateKind.SumSq => new NumberValue(numericValues.Sum(number => number * number)),
                ConditionalFormulaAggregateKind.DevSq when numericValues.Count > 0 => new NumberValue(DevSqFormulaNumbers(numericValues)),
                ConditionalFormulaAggregateKind.StdDevSample when numericValues.Count > 1 => new NumberValue(StandardDeviationFormulaNumbers(numericValues, sample: true)),
                ConditionalFormulaAggregateKind.StdDevPopulation when numericValues.Count > 0 => new NumberValue(StandardDeviationFormulaNumbers(numericValues, sample: false)),
                ConditionalFormulaAggregateKind.VarianceSample when numericValues.Count > 1 => new NumberValue(VarianceFormulaNumbers(numericValues, sample: true)),
                ConditionalFormulaAggregateKind.VariancePopulation when numericValues.Count > 0 => new NumberValue(VarianceFormulaNumbers(numericValues, sample: false)),
                ConditionalFormulaAggregateKind.AveDev when numericValues.Count > 0 => new NumberValue(AveDevFormulaNumbers(numericValues)),
                ConditionalFormulaAggregateKind.GeoMean when AreFormulaNumbersPositive(numericValues) => new NumberValue(GeoMeanFormulaNumbers(numericValues)),
                ConditionalFormulaAggregateKind.HarMean when AreFormulaNumbersPositive(numericValues) => new NumberValue(HarMeanFormulaNumbers(numericValues)),
                ConditionalFormulaAggregateKind.Product => new NumberValue(numericValues.Aggregate(1d, (product, number) => product * number)),
                ConditionalFormulaAggregateKind.Average when numericValues.Count > 0 => new NumberValue(numericValues.Average()),
                ConditionalFormulaAggregateKind.AverageA when numericValues.Count > 0 => new NumberValue(numericValues.Average()),
                ConditionalFormulaAggregateKind.Median when numericValues.Count > 0 => new NumberValue(MedianFormulaNumbers(numericValues)),
                ConditionalFormulaAggregateKind.Min when numericValues.Count > 0 => new NumberValue(numericValues.Min()),
                ConditionalFormulaAggregateKind.MinA => new NumberValue(numericValues.Count > 0 ? numericValues.Min() : 0d),
                ConditionalFormulaAggregateKind.Max when numericValues.Count > 0 => new NumberValue(numericValues.Max()),
                ConditionalFormulaAggregateKind.MaxA => new NumberValue(numericValues.Count > 0 ? numericValues.Max() : 0d),
                ConditionalFormulaAggregateKind.Count => new NumberValue(numericValues.Count),
                ConditionalFormulaAggregateKind.CountA => new NumberValue(nonBlankCount),
                ConditionalFormulaAggregateKind.CountBlank => new NumberValue(blankCount),
                _ => ErrorValue.Value
            };

            return value is not ErrorValue && TryGetNumber(value, out var number) && double.IsFinite(number);
        }

        private static double DevSqFormulaNumbers(List<double> numericValues)
        {
            var average = numericValues.Average();
            var sum = 0d;
            for (var i = 0; i < numericValues.Count; i++)
            {
                var deviation = numericValues[i] - average;
                sum += deviation * deviation;
            }

            return sum;
        }

        private static double StandardDeviationFormulaNumbers(List<double> numericValues, bool sample)
        {
            return Math.Sqrt(VarianceFormulaNumbers(numericValues, sample));
        }

        private static double VarianceFormulaNumbers(List<double> numericValues, bool sample)
        {
            var denominator = sample ? numericValues.Count - 1 : numericValues.Count;
            return DevSqFormulaNumbers(numericValues) / denominator;
        }

        private static double AveDevFormulaNumbers(List<double> numericValues)
        {
            var average = numericValues.Average();
            var sum = 0d;
            for (var i = 0; i < numericValues.Count; i++)
                sum += Math.Abs(numericValues[i] - average);

            return sum / numericValues.Count;
        }

        private static bool AreFormulaNumbersPositive(List<double> numericValues)
        {
            if (numericValues.Count == 0)
                return false;

            for (var i = 0; i < numericValues.Count; i++)
            {
                if (numericValues[i] <= 0d || !double.IsFinite(numericValues[i]))
                    return false;
            }

            return true;
        }

        private static double GeoMeanFormulaNumbers(List<double> numericValues)
        {
            var product = 1d;
            for (var i = 0; i < numericValues.Count; i++)
                product *= numericValues[i];

            return Math.Pow(product, 1d / numericValues.Count);
        }

        private static double HarMeanFormulaNumbers(List<double> numericValues)
        {
            var reciprocalSum = 0d;
            for (var i = 0; i < numericValues.Count; i++)
            {
                reciprocalSum += 1d / numericValues[i];
                if (!double.IsFinite(reciprocalSum))
                    return double.PositiveInfinity;
            }

            return numericValues.Count / reciprocalSum;
        }

        private static double MedianFormulaNumbers(List<double> numericValues)
        {
            numericValues.Sort();
            var middle = numericValues.Count / 2;
            return numericValues.Count % 2 == 1
                ? numericValues[middle]
                : (numericValues[middle - 1] + numericValues[middle]) / 2d;
        }

        private bool AppendFormulaAggregateValues(
            ConditionalFormulaAggregateArgument argument,
            ConditionalFormulaAggregateKind aggregateKind,
            int rowOffset,
            int colOffset,
            List<double> numericValues,
            ref int nonBlankCount,
            ref int blankCount)
        {
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    return AppendFormulaAggregateValue(
                        argument.Literal ?? BlankValue.Instance,
                        aggregateKind,
                        isDirectArgument: true,
                        numericValues,
                        ref nonBlankCount,
                        ref blankCount);
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    return AppendFormulaAggregateValue(
                        targetSheet.GetValue(row, col),
                        aggregateKind,
                        isDirectArgument: false,
                        numericValues,
                        ref nonBlankCount,
                        ref blankCount);
                case ConditionalFormulaAggregateArgumentKind.Range:
                    if (!TryResolveFormulaAggregateRange(
                            argument,
                            rowOffset,
                            colOffset,
                            out var rangeSheet,
                            out var startRow,
                            out var startCol,
                            out var endRow,
                            out var endCol))
                    {
                        return false;
                    }

                    for (var currentRow = startRow; currentRow <= endRow; currentRow++)
                    {
                        for (var currentCol = startCol; currentCol <= endCol; currentCol++)
                        {
                            if (!AppendFormulaAggregateValue(
                                    rangeSheet.GetValue(currentRow, currentCol),
                                    aggregateKind,
                                    isDirectArgument: false,
                                    numericValues,
                                    ref nonBlankCount,
                                    ref blankCount))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue ||
                        !TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out var operandValue))
                    {
                        return false;
                    }

                    if (operandValue is RangeValue operandRange)
                        return AppendFormulaAggregateRangeValue(
                            operandRange,
                            aggregateKind,
                            numericValues,
                            ref nonBlankCount,
                            ref blankCount);

                    return AppendFormulaAggregateValue(
                        operandValue,
                        aggregateKind,
                        isDirectArgument: true,
                        numericValues,
                        ref nonBlankCount,
                        ref blankCount);
                default:
                    return false;
            }
        }

        private static bool AppendFormulaAggregateRangeValue(
            RangeValue range,
            ConditionalFormulaAggregateKind aggregateKind,
            List<double> numericValues,
            ref int nonBlankCount,
            ref int blankCount)
        {
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    if (!AppendFormulaAggregateValue(
                            range.Cells[row, col],
                            aggregateKind,
                            isDirectArgument: false,
                            numericValues,
                            ref nonBlankCount,
                            ref blankCount))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool AppendFormulaAggregateValue(
            ScalarValue value,
            ConditionalFormulaAggregateKind aggregateKind,
            bool isDirectArgument,
            List<double> numericValues,
            ref int nonBlankCount,
            ref int blankCount)
        {
            if (value is RangeValue range)
            {
                for (var row = 0; row < range.RowCount; row++)
                    for (var col = 0; col < range.ColCount; col++)
                    {
                        if (!AppendFormulaAggregateValue(
                                range.Cells[row, col],
                                aggregateKind,
                                isDirectArgument: false,
                                numericValues,
                                ref nonBlankCount,
                                ref blankCount))
                        {
                            return false;
                        }
                    }

                return true;
            }

            if (value is ErrorValue)
                return false;

            if (value is BlankValue)
                blankCount++;
            else
                nonBlankCount++;

            if (TryGetFormulaAggregateNumber(
                    value,
                    aggregateKind,
                    isDirectArgument,
                    out var number,
                    out var unsupported))
            {
                numericValues.Add(number);
                return true;
            }

            return !unsupported;
        }

        private static bool TryGetFormulaAggregateNumber(
            ScalarValue value,
            ConditionalFormulaAggregateKind aggregateKind,
            bool isDirectArgument,
            out double number,
            out bool unsupported)
        {
            unsupported = false;
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    break;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    break;
                case BoolValue boolean when isDirectArgument || IsFormulaAValueAggregate(aggregateKind):
                    number = boolean.Value ? 1 : 0;
                    break;
                case TextValue text when IsFormulaAValueAggregate(aggregateKind):
                    if (TryParseFormulaValueText(text.Value, out var parsedA))
                    {
                        number = parsedA;
                        break;
                    }

                    if (isDirectArgument && text.Value.Length > 0)
                    {
                        number = 0;
                        unsupported = true;
                        return false;
                    }

                    number = 0;
                    break;
                case TextValue text when isDirectArgument &&
                    double.TryParse(
                        text.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed):
                    number = parsed;
                    break;
                case TextValue when isDirectArgument && IsFormulaNumericAggregate(aggregateKind):
                    number = 0;
                    unsupported = true;
                    return false;
                default:
                    number = 0;
                    return false;
            }

            if (double.IsFinite(number))
                return true;

            unsupported = true;
            return false;
        }

        private static bool IsFormulaAValueAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
            aggregateKind is
                ConditionalFormulaAggregateKind.AverageA or
                ConditionalFormulaAggregateKind.MinA or
                ConditionalFormulaAggregateKind.MaxA;

        private static bool IsFormulaNumericAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
            aggregateKind is
                ConditionalFormulaAggregateKind.Sum or
                ConditionalFormulaAggregateKind.SumSq or
                ConditionalFormulaAggregateKind.SumXMy2 or
                ConditionalFormulaAggregateKind.SumX2My2 or
                ConditionalFormulaAggregateKind.SumX2Py2 or
                ConditionalFormulaAggregateKind.DevSq or
                ConditionalFormulaAggregateKind.StdDevSample or
                ConditionalFormulaAggregateKind.StdDevPopulation or
                ConditionalFormulaAggregateKind.VarianceSample or
                ConditionalFormulaAggregateKind.VariancePopulation or
                ConditionalFormulaAggregateKind.AveDev or
                ConditionalFormulaAggregateKind.GeoMean or
                ConditionalFormulaAggregateKind.HarMean or
                ConditionalFormulaAggregateKind.Product or
                ConditionalFormulaAggregateKind.Average or
                ConditionalFormulaAggregateKind.AverageA or
                ConditionalFormulaAggregateKind.Median or
                ConditionalFormulaAggregateKind.Min or
                ConditionalFormulaAggregateKind.MinA or
                ConditionalFormulaAggregateKind.Max or
                ConditionalFormulaAggregateKind.MaxA;

        private bool TryResolveFormulaReference(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out Sheet targetSheet,
            out uint row,
            out uint col)
        {
            targetSheet = default!;
            row = 0;
            col = 0;

            if (operand.Kind != ConditionalFormulaOperandKind.Reference)
                return false;

            var shiftedRow = ShiftFormulaRow(operand.Row, operand.IsRowAbsolute, rowOffset);
            var shiftedCol = ShiftFormulaColumn(operand.Col, operand.IsColAbsolute, colOffset);
            if (!shiftedRow.HasValue || !shiftedCol.HasValue)
                return false;

            var resolvedSheet = operand.SheetName is null ? sheet : workbook.GetSheet(operand.SheetName);
            if (resolvedSheet is null)
                return false;

            targetSheet = resolvedSheet;
            row = shiftedRow.Value;
            col = shiftedCol.Value;
            return true;
        }

        private bool TryResolveFormulaReferenceRange(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out Sheet targetSheet,
            out uint startRow,
            out uint startCol,
            out uint endRow,
            out uint endCol)
        {
            targetSheet = default!;
            startRow = 0;
            startCol = 0;
            endRow = 0;
            endCol = 0;

            if (operand.Kind != ConditionalFormulaOperandKind.ReferenceRange ||
                operand.ReferenceRange is not { } range)
            {
                return false;
            }

            var shiftedStartRow = ShiftFormulaRow(operand.Row, operand.IsRowAbsolute, rowOffset);
            var shiftedStartCol = ShiftFormulaColumn(operand.Col, operand.IsColAbsolute, colOffset);
            var shiftedEndRow = ShiftFormulaRow(range.EndRow, range.IsEndRowAbsolute, rowOffset);
            var shiftedEndCol = ShiftFormulaColumn(range.EndCol, range.IsEndColAbsolute, colOffset);
            if (!shiftedStartRow.HasValue ||
                !shiftedStartCol.HasValue ||
                !shiftedEndRow.HasValue ||
                !shiftedEndCol.HasValue)
            {
                return false;
            }

            var resolvedSheet = operand.SheetName is null ? sheet : workbook.GetSheet(operand.SheetName);
            if (resolvedSheet is null)
                return false;

            targetSheet = resolvedSheet;
            startRow = Math.Min(shiftedStartRow.Value, shiftedEndRow.Value);
            startCol = Math.Min(shiftedStartCol.Value, shiftedEndCol.Value);
            endRow = Math.Max(shiftedStartRow.Value, shiftedEndRow.Value);
            endCol = Math.Max(shiftedStartCol.Value, shiftedEndCol.Value);
            return true;
        }

        private bool TryMaterializeFormulaReferenceRange(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out RangeValue range)
        {
            range = default!;
            if (!TryResolveFormulaReferenceRange(
                    operand,
                    rowOffset,
                    colOffset,
                    out var targetSheet,
                    out var startRow,
                    out var startCol,
                    out var endRow,
                    out var endCol))
            {
                return false;
            }

            var rowCount = (ulong)endRow - startRow + 1UL;
            var colCount = (ulong)endCol - startCol + 1UL;
            if (rowCount * colCount > MaxFormulaAggregateRangeCells)
                return false;

            var cells = new ScalarValue[(int)rowCount, (int)colCount];
            for (var currentRow = startRow; currentRow <= endRow; currentRow++)
            {
                for (var currentCol = startCol; currentCol <= endCol; currentCol++)
                {
                    cells[(int)(currentRow - startRow), (int)(currentCol - startCol)] =
                        targetSheet.GetValue(currentRow, currentCol);
                }
            }

            range = new RangeValue(cells, startRow, startCol) { SheetName = targetSheet.Name };
            return true;
        }

        private bool TryResolveFormulaAggregateReference(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out Sheet targetSheet,
            out uint row,
            out uint col)
        {
            targetSheet = default!;
            row = 0;
            col = 0;

            var shiftedRow = ShiftFormulaRow(argument.Row, argument.IsRowAbsolute, rowOffset);
            var shiftedCol = ShiftFormulaColumn(argument.Col, argument.IsColAbsolute, colOffset);
            if (!shiftedRow.HasValue || !shiftedCol.HasValue)
                return false;

            var resolvedSheet = argument.SheetName is null ? sheet : workbook.GetSheet(argument.SheetName);
            if (resolvedSheet is null)
                return false;

            targetSheet = resolvedSheet;
            row = shiftedRow.Value;
            col = shiftedCol.Value;
            return true;
        }

        private bool TryResolveFormulaAggregateRange(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out Sheet targetSheet,
            out uint startRow,
            out uint startCol,
            out uint endRow,
            out uint endCol)
        {
            targetSheet = default!;
            startRow = 0;
            startCol = 0;
            endRow = 0;
            endCol = 0;

            if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out targetSheet, out var firstRow, out var firstCol))
                return false;

            var shiftedEndRow = ShiftFormulaRow(argument.EndRow, argument.IsEndRowAbsolute, rowOffset);
            var shiftedEndCol = ShiftFormulaColumn(argument.EndCol, argument.IsEndColAbsolute, colOffset);
            if (!shiftedEndRow.HasValue || !shiftedEndCol.HasValue)
                return false;

            startRow = Math.Min(firstRow, shiftedEndRow.Value);
            startCol = Math.Min(firstCol, shiftedEndCol.Value);
            endRow = Math.Max(firstRow, shiftedEndRow.Value);
            endCol = Math.Max(firstCol, shiftedEndCol.Value);
            var rowCount = (ulong)endRow - startRow + 1UL;
            var colCount = (ulong)endCol - startCol + 1UL;
            if (rowCount * colCount > MaxFormulaAggregateRangeCells)
                return false;

            return true;
        }

        private bool TryGetValueCount(ConditionalFormat rule, ScalarValue value, out int count)
        {
            var key = ValueText(value);
            if (string.IsNullOrWhiteSpace(key))
            {
                count = 0;
                return false;
            }

            return GetValueCounts(rule).TryGetValue(key, out count);
        }

        private Dictionary<string, int> GetValueCounts(ConditionalFormat rule)
        {
            if (_valueCounts.TryGetValue(rule, out var counts))
                return counts;

            counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (entry, cell) in occupiedCells)
            {
                var key = ValueText(cell.Value);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var address = new CellAddress(sheet.Id, entry.Row, entry.Col);
                if (!rule.AppliesTo.Contains(address))
                    continue;

                counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
            }

            _valueCounts[rule] = counts;
            return counts;
        }

        private RangeAverage GetRangeAverage(ConditionalFormat rule)
        {
            if (_averages.TryGetValue(rule, out var average))
                return average;

            double sum = 0;
            var count = 0;
            foreach (var (entry, cell) in occupiedCells)
            {
                if (!TryGetNumber(cell.Value, out var number))
                    continue;

                var address = new CellAddress(sheet.Id, entry.Row, entry.Col);
                if (!rule.AppliesTo.Contains(address))
                    continue;

                sum += number;
                count++;
            }

            average = count == 0
                ? new RangeAverage(0, HasValues: false)
                : new RangeAverage(sum / count, HasValues: true);
            _averages[rule] = average;
            return average;
        }

        private HashSet<CellAddress>? GetTopBottomMatches(ConditionalFormat rule)
        {
            if (_topBottomMatches.TryGetValue(rule, out var matches))
                return matches;

            var rankedValues = new List<(CellAddress Address, double Value, int Index)>();
            foreach (var (entry, cell) in occupiedCells)
            {
                if (!TryGetNumber(cell.Value, out var number))
                    continue;

                var address = new CellAddress(sheet.Id, entry.Row, entry.Col);
                if (!rule.AppliesTo.Contains(address))
                    continue;

                rankedValues.Add((address, number, rankedValues.Count));
            }

            if (rankedValues.Count == 0)
            {
                _topBottomMatches[rule] = null;
                return null;
            }

            var take = Math.Clamp(
                rule.TopBottomPercent
                    ? (int)Math.Ceiling(rankedValues.Count * Math.Max(1, rule.TopBottomRank) / 100d)
                    : rule.TopBottomRank,
                1,
                rankedValues.Count);
            rankedValues.Sort(rule.AboveAverage
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

            matches = new HashSet<CellAddress>(take);
            for (var i = 0; i < take; i++)
                matches.Add(rankedValues[i].Address);

            _topBottomMatches[rule] = matches;
            return matches;
        }
    }

    private readonly record struct RangeAverage(double Value, bool HasValues);

    private sealed class CellStyleReferenceComparer : IEqualityComparer<CellStyle>
    {
        public static readonly CellStyleReferenceComparer Instance = new();

        public bool Equals(CellStyle? x, CellStyle? y) => ReferenceEquals(x, y);

        public int GetHashCode(CellStyle obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
