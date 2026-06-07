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
                !TryCreateFormulaSelectorExpression(function.Arguments[1], out var whenTrue) ||
                !TryCreateFormulaSelectorExpression(function.Arguments[2], out var whenFalse))
            {
                return false;
            }

            expression = new ConditionalFormulaIfExpression(condition, whenTrue, whenFalse);
            return true;
        }

        if (TryCreateFormulaErrorFallbackExpression(function, out expression))
            return true;

        if (TryCreateFormulaIfsExpression(function, out expression))
            return true;

        if (TryCreateFormulaSwitchExpression(function, out expression))
            return true;

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

    private static bool TryCreateFormulaSelectorExpression(FormulaNode ast, out ConditionalFormulaExpression expression)
    {
        if (TryCreateFormulaExpression(ast, out expression))
            return true;

        if (TryCreateFormulaOperand(ast, out var operand))
        {
            expression = new ConditionalFormulaOperandExpression(operand);
            return true;
        }

        return false;
    }

    private static bool TryCreateFormulaErrorFallbackExpression(
        FunctionCallNode function,
        out ConditionalFormulaExpression expression)
    {
        expression = default!;

        var kind = string.Equals(function.FunctionName, "IFERROR", StringComparison.OrdinalIgnoreCase)
            ? ConditionalFormulaErrorFallbackKind.IfError
            : string.Equals(function.FunctionName, "IFNA", StringComparison.OrdinalIgnoreCase)
                ? ConditionalFormulaErrorFallbackKind.IfNa
                : (ConditionalFormulaErrorFallbackKind?)null;
        if (!kind.HasValue)
            return false;

        if (function.Arguments.Count != 2 ||
            !TryCreateFormulaSelectorExpression(function.Arguments[0], out var value) ||
            !TryCreateFormulaSelectorExpression(function.Arguments[1], out var fallback))
        {
            return false;
        }

        expression = new ConditionalFormulaErrorFallbackExpression(kind.Value, value, fallback);
        return true;
    }

    private static bool TryCreateFormulaIfsExpression(
        FunctionCallNode function,
        out ConditionalFormulaExpression expression)
    {
        expression = default!;
        if (!string.Equals(function.FunctionName, "IFS", StringComparison.OrdinalIgnoreCase) ||
            function.Arguments.Count < 2 ||
            function.Arguments.Count > MaxFormulaSelectorArgumentCount ||
            function.Arguments.Count % 2 != 0)
        {
            return false;
        }

        var branches = new ConditionalFormulaIfsBranch[function.Arguments.Count / 2];
        for (var i = 0; i < branches.Length; i++)
        {
            if (!TryCreateFormulaExpression(function.Arguments[i * 2], out var condition) ||
                !TryCreateFormulaSelectorExpression(function.Arguments[i * 2 + 1], out var value))
            {
                return false;
            }

            branches[i] = new ConditionalFormulaIfsBranch(condition, value);
        }

        expression = new ConditionalFormulaIfsExpression(branches);
        return true;
    }

    private static bool TryCreateFormulaSwitchExpression(
        FunctionCallNode function,
        out ConditionalFormulaExpression expression)
    {
        expression = default!;
        if (!string.Equals(function.FunctionName, "SWITCH", StringComparison.OrdinalIgnoreCase) ||
            function.Arguments.Count < 3 ||
            function.Arguments.Count > MaxFormulaSelectorArgumentCount ||
            !TryCreateFormulaSelectorExpression(function.Arguments[0], out var selector))
        {
            return false;
        }

        var hasDefault = function.Arguments.Count % 2 == 0;
        var caseCount = (function.Arguments.Count - 1) / 2;
        var cases = new ConditionalFormulaSwitchCase[caseCount];
        for (var i = 0; i < caseCount; i++)
        {
            if (!TryCreateFormulaSelectorExpression(function.Arguments[i * 2 + 1], out var matchValue) ||
                !TryCreateFormulaSelectorExpression(function.Arguments[i * 2 + 2], out var result))
            {
                return false;
            }

            cases[i] = new ConditionalFormulaSwitchCase(matchValue, result);
        }

        ConditionalFormulaExpression? defaultValue = null;
        if (hasDefault &&
            !TryCreateFormulaSelectorExpression(function.Arguments[^1], out defaultValue))
        {
            return false;
        }

        expression = new ConditionalFormulaSwitchExpression(selector, cases, defaultValue);
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
    private const int MaxFormulaFinancialDepreciationIterations = 10_000;
    private const int MaxFormulaFinancialBondCouponIterations = 50_000;
    private const int MaxFormulaFinancialBondYieldIterations = 200;
    private const int MaxFormulaGcdArgumentCount = 255;
    private const int MaxFormulaMultinomialArgumentCount = 255;
    private const int MaxFormulaModeArgumentCount = 255;
    private const int MaxFormulaSumProductArgumentCount = 255;
    private const int MaxFormulaConditionalAggregateArgumentCount = 255;
    private const int MaxFormulaSelectorArgumentCount = 255;
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
            case "NORMDIST":
            case "NORM.DIST":
                kind = ConditionalFormulaScalarFunctionKind.NormDist;
                return true;
            case "NORMINV":
            case "NORM.INV":
                kind = ConditionalFormulaScalarFunctionKind.NormInv;
                return true;
            case "NORMSDIST":
                kind = ConditionalFormulaScalarFunctionKind.NormSDistCompat;
                return true;
            case "NORM.S.DIST":
                kind = ConditionalFormulaScalarFunctionKind.NormSDist;
                return true;
            case "NORMSINV":
            case "NORM.S.INV":
                kind = ConditionalFormulaScalarFunctionKind.NormSInv;
                return true;
            case "PHI":
                kind = ConditionalFormulaScalarFunctionKind.Phi;
                return true;
            case "GAUSS":
                kind = ConditionalFormulaScalarFunctionKind.Gauss;
                return true;
            case "STANDARDIZE":
                kind = ConditionalFormulaScalarFunctionKind.Standardize;
                return true;
            case "TDIST":
                kind = ConditionalFormulaScalarFunctionKind.TDistCompat;
                return true;
            case "T.DIST":
                kind = ConditionalFormulaScalarFunctionKind.TDist;
                return true;
            case "T.DIST.RT":
                kind = ConditionalFormulaScalarFunctionKind.TDistRt;
                return true;
            case "T.DIST.2T":
                kind = ConditionalFormulaScalarFunctionKind.TDist2T;
                return true;
            case "TINV":
                kind = ConditionalFormulaScalarFunctionKind.TInv2T;
                return true;
            case "T.INV":
                kind = ConditionalFormulaScalarFunctionKind.TInv;
                return true;
            case "T.INV.2T":
                kind = ConditionalFormulaScalarFunctionKind.TInv2T;
                return true;
            case "FDIST":
                kind = ConditionalFormulaScalarFunctionKind.FDistRt;
                return true;
            case "F.DIST":
                kind = ConditionalFormulaScalarFunctionKind.FDist;
                return true;
            case "F.DIST.RT":
                kind = ConditionalFormulaScalarFunctionKind.FDistRt;
                return true;
            case "FINV":
                kind = ConditionalFormulaScalarFunctionKind.FInvRt;
                return true;
            case "F.INV":
                kind = ConditionalFormulaScalarFunctionKind.FInv;
                return true;
            case "F.INV.RT":
                kind = ConditionalFormulaScalarFunctionKind.FInvRt;
                return true;
            case "CHIDIST":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqDistRt;
                return true;
            case "CHISQ.DIST":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqDist;
                return true;
            case "CHISQ.DIST.RT":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqDistRt;
                return true;
            case "CHIINV":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqInvRt;
                return true;
            case "CHISQ.INV":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqInv;
                return true;
            case "CHISQ.INV.RT":
                kind = ConditionalFormulaScalarFunctionKind.ChiSqInvRt;
                return true;
            case "BETA.DIST":
                kind = ConditionalFormulaScalarFunctionKind.BetaDist;
                return true;
            case "BETADIST":
                kind = ConditionalFormulaScalarFunctionKind.BetaDistCompat;
                return true;
            case "BETA.INV":
            case "BETAINV":
                kind = ConditionalFormulaScalarFunctionKind.BetaInv;
                return true;
            case "GAMMA":
                kind = ConditionalFormulaScalarFunctionKind.Gamma;
                return true;
            case "GAMMA.DIST":
            case "GAMMADIST":
                kind = ConditionalFormulaScalarFunctionKind.GammaDist;
                return true;
            case "GAMMA.INV":
            case "GAMMAINV":
                kind = ConditionalFormulaScalarFunctionKind.GammaInv;
                return true;
            case "GAMMALN":
            case "GAMMALN.PRECISE":
                kind = ConditionalFormulaScalarFunctionKind.GammaLn;
                return true;
            case "LOGNORM.DIST":
                kind = ConditionalFormulaScalarFunctionKind.LogNormDist;
                return true;
            case "LOGNORMDIST":
                kind = ConditionalFormulaScalarFunctionKind.LogNormDistCompat;
                return true;
            case "LOGNORM.INV":
            case "LOGINV":
                kind = ConditionalFormulaScalarFunctionKind.LogNormInv;
                return true;
            case "EXPON.DIST":
            case "EXPONDIST":
                kind = ConditionalFormulaScalarFunctionKind.ExponDist;
                return true;
            case "WEIBULL":
            case "WEIBULL.DIST":
                kind = ConditionalFormulaScalarFunctionKind.WeibullDist;
                return true;
            case "PMT":
                kind = ConditionalFormulaScalarFunctionKind.Pmt;
                return true;
            case "PV":
                kind = ConditionalFormulaScalarFunctionKind.Pv;
                return true;
            case "FV":
                kind = ConditionalFormulaScalarFunctionKind.Fv;
                return true;
            case "NPER":
                kind = ConditionalFormulaScalarFunctionKind.Nper;
                return true;
            case "RATE":
                kind = ConditionalFormulaScalarFunctionKind.Rate;
                return true;
            case "IPMT":
                kind = ConditionalFormulaScalarFunctionKind.Ipmt;
                return true;
            case "PPMT":
                kind = ConditionalFormulaScalarFunctionKind.Ppmt;
                return true;
            case "ISPMT":
                kind = ConditionalFormulaScalarFunctionKind.Ispmt;
                return true;
            case "NPV":
                kind = ConditionalFormulaScalarFunctionKind.Npv;
                return true;
            case "IRR":
                kind = ConditionalFormulaScalarFunctionKind.Irr;
                return true;
            case "MIRR":
                kind = ConditionalFormulaScalarFunctionKind.Mirr;
                return true;
            case "XNPV":
                kind = ConditionalFormulaScalarFunctionKind.Xnpv;
                return true;
            case "XIRR":
                kind = ConditionalFormulaScalarFunctionKind.Xirr;
                return true;
            case "DISC":
                kind = ConditionalFormulaScalarFunctionKind.Disc;
                return true;
            case "INTRATE":
                kind = ConditionalFormulaScalarFunctionKind.Intrate;
                return true;
            case "RECEIVED":
                kind = ConditionalFormulaScalarFunctionKind.Received;
                return true;
            case "PRICEDISC":
                kind = ConditionalFormulaScalarFunctionKind.Pricedisc;
                return true;
            case "PRICEMAT":
                kind = ConditionalFormulaScalarFunctionKind.Pricemat;
                return true;
            case "TBILLEQ":
                kind = ConditionalFormulaScalarFunctionKind.Tbilleq;
                return true;
            case "TBILLPRICE":
                kind = ConditionalFormulaScalarFunctionKind.Tbillprice;
                return true;
            case "TBILLYIELD":
                kind = ConditionalFormulaScalarFunctionKind.Tbillyield;
                return true;
            case "DURATION":
                kind = ConditionalFormulaScalarFunctionKind.Duration;
                return true;
            case "MDURATION":
                kind = ConditionalFormulaScalarFunctionKind.Mduration;
                return true;
            case "PRICE":
                kind = ConditionalFormulaScalarFunctionKind.Price;
                return true;
            case "YIELD":
                kind = ConditionalFormulaScalarFunctionKind.Yield;
                return true;
            case "YIELDDISC":
                kind = ConditionalFormulaScalarFunctionKind.Yielddisc;
                return true;
            case "YIELDMAT":
                kind = ConditionalFormulaScalarFunctionKind.Yieldmat;
                return true;
            case "ODDFPRICE":
                kind = ConditionalFormulaScalarFunctionKind.Oddfprice;
                return true;
            case "ODDFYIELD":
                kind = ConditionalFormulaScalarFunctionKind.Oddfyield;
                return true;
            case "ODDLPRICE":
                kind = ConditionalFormulaScalarFunctionKind.Oddlprice;
                return true;
            case "ODDLYIELD":
                kind = ConditionalFormulaScalarFunctionKind.Oddlyield;
                return true;
            case "SLN":
                kind = ConditionalFormulaScalarFunctionKind.Sln;
                return true;
            case "SYD":
                kind = ConditionalFormulaScalarFunctionKind.Syd;
                return true;
            case "DB":
                kind = ConditionalFormulaScalarFunctionKind.Db;
                return true;
            case "DDB":
                kind = ConditionalFormulaScalarFunctionKind.Ddb;
                return true;
            case "VDB":
                kind = ConditionalFormulaScalarFunctionKind.Vdb;
                return true;
            case "EFFECT":
                kind = ConditionalFormulaScalarFunctionKind.Effect;
                return true;
            case "NOMINAL":
                kind = ConditionalFormulaScalarFunctionKind.Nominal;
                return true;
            case "RRI":
                kind = ConditionalFormulaScalarFunctionKind.Rri;
                return true;
            case "PDURATION":
                kind = ConditionalFormulaScalarFunctionKind.Pduration;
                return true;
            case "COUPDAYBS":
                kind = ConditionalFormulaScalarFunctionKind.Coupdaybs;
                return true;
            case "COUPDAYS":
                kind = ConditionalFormulaScalarFunctionKind.Coupdays;
                return true;
            case "COUPDAYSNC":
                kind = ConditionalFormulaScalarFunctionKind.Coupdaysnc;
                return true;
            case "COUPNCD":
                kind = ConditionalFormulaScalarFunctionKind.Coupncd;
                return true;
            case "COUPNUM":
                kind = ConditionalFormulaScalarFunctionKind.Coupnum;
                return true;
            case "COUPPCD":
                kind = ConditionalFormulaScalarFunctionKind.Couppcd;
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
            case "TEXT":
                kind = ConditionalFormulaScalarFunctionKind.Text;
                return true;
            case "FIXED":
                kind = ConditionalFormulaScalarFunctionKind.Fixed;
                return true;
            case "DOLLAR":
                kind = ConditionalFormulaScalarFunctionKind.Dollar;
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
            case "CHOOSE":
                kind = ConditionalFormulaScalarFunctionKind.Choose;
                return true;
            case "MATCH":
                kind = ConditionalFormulaScalarFunctionKind.Match;
                return true;
            case "XMATCH":
                kind = ConditionalFormulaScalarFunctionKind.XMatch;
                return true;
            case "INDEX":
                kind = ConditionalFormulaScalarFunctionKind.Index;
                return true;
            case "VLOOKUP":
                kind = ConditionalFormulaScalarFunctionKind.VLookup;
                return true;
            case "HLOOKUP":
                kind = ConditionalFormulaScalarFunctionKind.HLookup;
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
            ConditionalFormulaScalarFunctionKind.NormSDistCompat or
            ConditionalFormulaScalarFunctionKind.NormSInv or
            ConditionalFormulaScalarFunctionKind.Phi or
            ConditionalFormulaScalarFunctionKind.Gauss or
            ConditionalFormulaScalarFunctionKind.Gamma or
            ConditionalFormulaScalarFunctionKind.GammaLn or
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
            ConditionalFormulaScalarFunctionKind.Fixed => argumentCount is >= 1 and <= 3,
            ConditionalFormulaScalarFunctionKind.Log or
            ConditionalFormulaScalarFunctionKind.Roman or
            ConditionalFormulaScalarFunctionKind.IsoCeiling or
            ConditionalFormulaScalarFunctionKind.FloorPrecise or
            ConditionalFormulaScalarFunctionKind.Trunc or
            ConditionalFormulaScalarFunctionKind.Erf or
            ConditionalFormulaScalarFunctionKind.Delta or
            ConditionalFormulaScalarFunctionKind.GeStep or
            ConditionalFormulaScalarFunctionKind.Dollar or
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
            ConditionalFormulaScalarFunctionKind.Text or
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
            ConditionalFormulaScalarFunctionKind.Effect or
            ConditionalFormulaScalarFunctionKind.Nominal or
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
            ConditionalFormulaScalarFunctionKind.Choose => argumentCount is >= 2 and <= 255,
            ConditionalFormulaScalarFunctionKind.Match => argumentCount is 2 or 3,
            ConditionalFormulaScalarFunctionKind.XMatch or
            ConditionalFormulaScalarFunctionKind.Index => argumentCount is >= 2 and <= 4,
            ConditionalFormulaScalarFunctionKind.VLookup or
            ConditionalFormulaScalarFunctionKind.HLookup => argumentCount is 3 or 4,
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
            ConditionalFormulaScalarFunctionKind.TDistCompat or
            ConditionalFormulaScalarFunctionKind.FDistRt or
            ConditionalFormulaScalarFunctionKind.FInv or
            ConditionalFormulaScalarFunctionKind.FInvRt or
            ConditionalFormulaScalarFunctionKind.ChiSqDist or
            ConditionalFormulaScalarFunctionKind.NormInv or
            ConditionalFormulaScalarFunctionKind.ExponDist or
            ConditionalFormulaScalarFunctionKind.GammaInv or
            ConditionalFormulaScalarFunctionKind.LogNormDistCompat or
            ConditionalFormulaScalarFunctionKind.LogNormInv or
            ConditionalFormulaScalarFunctionKind.Standardize or
            ConditionalFormulaScalarFunctionKind.Sln or
            ConditionalFormulaScalarFunctionKind.Rri or
            ConditionalFormulaScalarFunctionKind.Pduration or
            ConditionalFormulaScalarFunctionKind.Convert => argumentCount == 3,
            ConditionalFormulaScalarFunctionKind.TDist or
            ConditionalFormulaScalarFunctionKind.FDist or
            ConditionalFormulaScalarFunctionKind.NormDist or
            ConditionalFormulaScalarFunctionKind.GammaDist or
            ConditionalFormulaScalarFunctionKind.LogNormDist or
            ConditionalFormulaScalarFunctionKind.WeibullDist or
            ConditionalFormulaScalarFunctionKind.Syd => argumentCount == 4,
            ConditionalFormulaScalarFunctionKind.BetaDist => argumentCount is >= 4 and <= 6,
            ConditionalFormulaScalarFunctionKind.BetaDistCompat or
            ConditionalFormulaScalarFunctionKind.BetaInv => argumentCount is >= 3 and <= 5,
            ConditionalFormulaScalarFunctionKind.NormSDist => argumentCount == 2,
            ConditionalFormulaScalarFunctionKind.Npv => argumentCount is >= 2 and <= 255,
            ConditionalFormulaScalarFunctionKind.Irr => argumentCount is 1 or 2,
            ConditionalFormulaScalarFunctionKind.Mirr => argumentCount == 3,
            ConditionalFormulaScalarFunctionKind.Xnpv => argumentCount == 3,
            ConditionalFormulaScalarFunctionKind.Xirr => argumentCount is 2 or 3,
            ConditionalFormulaScalarFunctionKind.Disc or
            ConditionalFormulaScalarFunctionKind.Intrate or
            ConditionalFormulaScalarFunctionKind.Received or
            ConditionalFormulaScalarFunctionKind.Pricedisc => argumentCount is 4 or 5,
            ConditionalFormulaScalarFunctionKind.Pricemat => argumentCount is 5 or 6,
            ConditionalFormulaScalarFunctionKind.Tbilleq or
            ConditionalFormulaScalarFunctionKind.Tbillprice or
            ConditionalFormulaScalarFunctionKind.Tbillyield => argumentCount == 3,
            ConditionalFormulaScalarFunctionKind.Price or
            ConditionalFormulaScalarFunctionKind.Yield => argumentCount is 6 or 7,
            ConditionalFormulaScalarFunctionKind.Yielddisc => argumentCount is 4 or 5,
            ConditionalFormulaScalarFunctionKind.Duration or
            ConditionalFormulaScalarFunctionKind.Mduration or
            ConditionalFormulaScalarFunctionKind.Yieldmat => argumentCount is 5 or 6,
            ConditionalFormulaScalarFunctionKind.Oddfprice or
            ConditionalFormulaScalarFunctionKind.Oddfyield => argumentCount is 8 or 9,
            ConditionalFormulaScalarFunctionKind.Oddlprice or
            ConditionalFormulaScalarFunctionKind.Oddlyield => argumentCount is 7 or 8,
            ConditionalFormulaScalarFunctionKind.Db or
            ConditionalFormulaScalarFunctionKind.Ddb => argumentCount is 4 or 5,
            ConditionalFormulaScalarFunctionKind.Vdb => argumentCount is >= 5 and <= 7,
            ConditionalFormulaScalarFunctionKind.Coupdaybs or
            ConditionalFormulaScalarFunctionKind.Coupdays or
            ConditionalFormulaScalarFunctionKind.Coupdaysnc or
            ConditionalFormulaScalarFunctionKind.Coupncd or
            ConditionalFormulaScalarFunctionKind.Coupnum or
            ConditionalFormulaScalarFunctionKind.Couppcd => argumentCount is 3 or 4,
            ConditionalFormulaScalarFunctionKind.TDistRt or
            ConditionalFormulaScalarFunctionKind.TDist2T or
            ConditionalFormulaScalarFunctionKind.TInv or
            ConditionalFormulaScalarFunctionKind.TInv2T or
            ConditionalFormulaScalarFunctionKind.ChiSqDistRt or
            ConditionalFormulaScalarFunctionKind.ChiSqInv or
            ConditionalFormulaScalarFunctionKind.ChiSqInvRt => argumentCount == 2,
            ConditionalFormulaScalarFunctionKind.Pmt or
            ConditionalFormulaScalarFunctionKind.Pv or
            ConditionalFormulaScalarFunctionKind.Fv or
            ConditionalFormulaScalarFunctionKind.Nper => argumentCount is >= 3 and <= 5,
            ConditionalFormulaScalarFunctionKind.Rate => argumentCount is >= 3 and <= 6,
            ConditionalFormulaScalarFunctionKind.Ipmt or
            ConditionalFormulaScalarFunctionKind.Ppmt => argumentCount is >= 4 and <= 6,
            ConditionalFormulaScalarFunctionKind.Ispmt => argumentCount == 4,
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
            ConditionalFormulaAggregateKind.SumIf or
            ConditionalFormulaAggregateKind.AverageIf => argumentCount is 2 or 3,
            ConditionalFormulaAggregateKind.CountIf => argumentCount == 2,
            ConditionalFormulaAggregateKind.SumIfs or
            ConditionalFormulaAggregateKind.AverageIfs => argumentCount is >= 3 and <= MaxFormulaConditionalAggregateArgumentCount &&
                (argumentCount - 1) % 2 == 0,
            ConditionalFormulaAggregateKind.CountIfs => argumentCount is >= 2 and <= MaxFormulaConditionalAggregateArgumentCount &&
                argumentCount % 2 == 0,
            ConditionalFormulaAggregateKind.SumProduct => argumentCount is >= 1 and <= MaxFormulaSumProductArgumentCount,
            _ when IsFormulaDatabaseAggregate(aggregateKind) => argumentCount == 3,
            _ when IsFormulaPairwiseAggregate(aggregateKind) => argumentCount == 2,
            ConditionalFormulaAggregateKind.Large or
            ConditionalFormulaAggregateKind.Small or
            ConditionalFormulaAggregateKind.PercentileInc or
            ConditionalFormulaAggregateKind.PercentileExc or
            ConditionalFormulaAggregateKind.QuartileInc or
            ConditionalFormulaAggregateKind.QuartileExc or
            ConditionalFormulaAggregateKind.PercentOf => argumentCount == 2,
            ConditionalFormulaAggregateKind.Rank or
            ConditionalFormulaAggregateKind.RankEq or
            ConditionalFormulaAggregateKind.RankAvg or
            ConditionalFormulaAggregateKind.PercentRankInc or
            ConditionalFormulaAggregateKind.PercentRankExc => argumentCount is 2 or 3,
            ConditionalFormulaAggregateKind.Prob => argumentCount is 3 or 4,
            ConditionalFormulaAggregateKind.ModeSngl => argumentCount is >= 1 and <= MaxFormulaModeArgumentCount,
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
            case "SUMIF":
                kind = ConditionalFormulaAggregateKind.SumIf;
                return true;
            case "SUMIFS":
                kind = ConditionalFormulaAggregateKind.SumIfs;
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
            case "AVERAGEIF":
                kind = ConditionalFormulaAggregateKind.AverageIf;
                return true;
            case "AVERAGEIFS":
                kind = ConditionalFormulaAggregateKind.AverageIfs;
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
            case "COUNTIF":
                kind = ConditionalFormulaAggregateKind.CountIf;
                return true;
            case "COUNTIFS":
                kind = ConditionalFormulaAggregateKind.CountIfs;
                return true;
            case "COUNTA":
                kind = ConditionalFormulaAggregateKind.CountA;
                return true;
            case "COUNTBLANK":
                kind = ConditionalFormulaAggregateKind.CountBlank;
                return true;
            case "LARGE":
                kind = ConditionalFormulaAggregateKind.Large;
                return true;
            case "SMALL":
                kind = ConditionalFormulaAggregateKind.Small;
                return true;
            case "RANK":
                kind = ConditionalFormulaAggregateKind.Rank;
                return true;
            case "RANK.EQ":
                kind = ConditionalFormulaAggregateKind.RankEq;
                return true;
            case "RANK.AVG":
                kind = ConditionalFormulaAggregateKind.RankAvg;
                return true;
            case "PERCENTILE":
            case "PERCENTILE.INC":
                kind = ConditionalFormulaAggregateKind.PercentileInc;
                return true;
            case "PERCENTILE.EXC":
                kind = ConditionalFormulaAggregateKind.PercentileExc;
                return true;
            case "QUARTILE":
            case "QUARTILE.INC":
                kind = ConditionalFormulaAggregateKind.QuartileInc;
                return true;
            case "QUARTILE.EXC":
                kind = ConditionalFormulaAggregateKind.QuartileExc;
                return true;
            case "PERCENTRANK":
            case "PERCENTRANK.INC":
                kind = ConditionalFormulaAggregateKind.PercentRankInc;
                return true;
            case "PERCENTRANK.EXC":
                kind = ConditionalFormulaAggregateKind.PercentRankExc;
                return true;
            case "MODE":
            case "MODE.SNGL":
                kind = ConditionalFormulaAggregateKind.ModeSngl;
                return true;
            case "PROB":
                kind = ConditionalFormulaAggregateKind.Prob;
                return true;
            case "PERCENTOF":
                kind = ConditionalFormulaAggregateKind.PercentOf;
                return true;
            case "DSUM":
                kind = ConditionalFormulaAggregateKind.DSum;
                return true;
            case "DAVERAGE":
                kind = ConditionalFormulaAggregateKind.DAverage;
                return true;
            case "DCOUNT":
                kind = ConditionalFormulaAggregateKind.DCount;
                return true;
            case "DCOUNTA":
                kind = ConditionalFormulaAggregateKind.DCountA;
                return true;
            case "DMAX":
                kind = ConditionalFormulaAggregateKind.DMax;
                return true;
            case "DMIN":
                kind = ConditionalFormulaAggregateKind.DMin;
                return true;
            case "DPRODUCT":
                kind = ConditionalFormulaAggregateKind.DProduct;
                return true;
            case "DSTDEV":
                kind = ConditionalFormulaAggregateKind.DStdDev;
                return true;
            case "DSTDEVP":
                kind = ConditionalFormulaAggregateKind.DStdDevP;
                return true;
            case "DVAR":
                kind = ConditionalFormulaAggregateKind.DVar;
                return true;
            case "DVARP":
                kind = ConditionalFormulaAggregateKind.DVarP;
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

    private static bool IsFormulaConditionalAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
        aggregateKind is
            ConditionalFormulaAggregateKind.SumIf or
            ConditionalFormulaAggregateKind.CountIf or
            ConditionalFormulaAggregateKind.AverageIf or
            ConditionalFormulaAggregateKind.SumIfs or
            ConditionalFormulaAggregateKind.CountIfs or
            ConditionalFormulaAggregateKind.AverageIfs;

    private static bool IsFormulaDatabaseAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
        aggregateKind is
            ConditionalFormulaAggregateKind.DSum or
            ConditionalFormulaAggregateKind.DAverage or
            ConditionalFormulaAggregateKind.DCount or
            ConditionalFormulaAggregateKind.DCountA or
            ConditionalFormulaAggregateKind.DMax or
            ConditionalFormulaAggregateKind.DMin or
            ConditionalFormulaAggregateKind.DProduct or
            ConditionalFormulaAggregateKind.DStdDev or
            ConditionalFormulaAggregateKind.DStdDevP or
            ConditionalFormulaAggregateKind.DVar or
            ConditionalFormulaAggregateKind.DVarP;

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

    private sealed record ConditionalFormulaErrorFallbackExpression(
        ConditionalFormulaErrorFallbackKind Kind,
        ConditionalFormulaExpression Value,
        ConditionalFormulaExpression Fallback) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaIfsExpression(
        IReadOnlyList<ConditionalFormulaIfsBranch> Branches) : ConditionalFormulaExpression;

    private sealed record ConditionalFormulaSwitchExpression(
        ConditionalFormulaExpression Selector,
        IReadOnlyList<ConditionalFormulaSwitchCase> Cases,
        ConditionalFormulaExpression? DefaultValue) : ConditionalFormulaExpression;

    private enum ConditionalFormulaLogicalOperator
    {
        And,
        Or,
        Xor,
        Not
    }

    private enum ConditionalFormulaErrorFallbackKind
    {
        IfError,
        IfNa
    }

    private readonly record struct ConditionalFormulaIfsBranch(
        ConditionalFormulaExpression Condition,
        ConditionalFormulaExpression Value);

    private readonly record struct ConditionalFormulaSwitchCase(
        ConditionalFormulaExpression MatchValue,
        ConditionalFormulaExpression Result);

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
        NormDist,
        NormInv,
        NormSDistCompat,
        NormSDist,
        NormSInv,
        Phi,
        Gauss,
        Standardize,
        TDistCompat,
        TDist,
        TDistRt,
        TDist2T,
        TInv,
        TInv2T,
        FDist,
        FDistRt,
        FInv,
        FInvRt,
        ChiSqDist,
        ChiSqDistRt,
        ChiSqInv,
        ChiSqInvRt,
        BetaDist,
        BetaDistCompat,
        BetaInv,
        Gamma,
        GammaDist,
        GammaInv,
        GammaLn,
        LogNormDist,
        LogNormDistCompat,
        LogNormInv,
        ExponDist,
        WeibullDist,
        Pmt,
        Pv,
        Fv,
        Nper,
        Rate,
        Ipmt,
        Ppmt,
        Ispmt,
        Npv,
        Irr,
        Mirr,
        Xnpv,
        Xirr,
        Disc,
        Intrate,
        Received,
        Pricedisc,
        Pricemat,
        Tbilleq,
        Tbillprice,
        Tbillyield,
        Duration,
        Mduration,
        Price,
        Yield,
        Yielddisc,
        Yieldmat,
        Oddfprice,
        Oddfyield,
        Oddlprice,
        Oddlyield,
        Sln,
        Syd,
        Db,
        Ddb,
        Vdb,
        Effect,
        Nominal,
        Rri,
        Pduration,
        Coupdaybs,
        Coupdays,
        Coupdaysnc,
        Coupncd,
        Coupnum,
        Couppcd,
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
        Text,
        Fixed,
        Dollar,
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
        Choose,
        Match,
        XMatch,
        Index,
        VLookup,
        HLookup,
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
        SumIf,
        SumIfs,
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
        AverageIf,
        AverageIfs,
        AverageA,
        Median,
        Min,
        MinA,
        Max,
        MaxA,
        Count,
        CountIf,
        CountIfs,
        CountA,
        CountBlank,
        Large,
        Small,
        Rank,
        RankEq,
        RankAvg,
        PercentileInc,
        PercentileExc,
        QuartileInc,
        QuartileExc,
        PercentRankInc,
        PercentRankExc,
        ModeSngl,
        Prob,
        PercentOf,
        DSum,
        DAverage,
        DCount,
        DCountA,
        DMax,
        DMin,
        DProduct,
        DStdDev,
        DStdDevP,
        DVar,
        DVarP
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
            int colOffset)
        {
            if (!TryEvaluateFormulaExpressionValue(expression, rowOffset, colOffset, out var value))
                return null;

            return FormulaBooleanValue(value);
        }

        private bool TryEvaluateFormulaExpressionValue(
            ConditionalFormulaExpression expression,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            switch (expression)
            {
                case ConditionalFormulaOperandExpression operand:
                    return TryResolveFormulaOperand(operand.Operand, rowOffset, colOffset, out value);
                case ConditionalFormulaComparisonExpression comparison:
                    return TryEvaluateFormulaComparisonValue(comparison.Comparison, rowOffset, colOffset, out value);
                case ConditionalFormulaLogicalExpression logical:
                    var logicalResult = EvaluateFormulaLogical(logical, rowOffset, colOffset);
                    if (!logicalResult.HasValue)
                        return false;

                    value = new BoolValue(logicalResult.Value);
                    return true;
                case ConditionalFormulaPredicateExpression predicate:
                    var predicateResult = EvaluateFormulaPredicate(predicate.Predicate, rowOffset, colOffset);
                    if (!predicateResult.HasValue)
                        return false;

                    value = new BoolValue(predicateResult.Value);
                    return true;
                case ConditionalFormulaIfExpression ifExpression:
                    return TryEvaluateFormulaIfValue(ifExpression, rowOffset, colOffset, out value);
                case ConditionalFormulaErrorFallbackExpression errorFallback:
                    return TryEvaluateFormulaErrorFallbackValue(errorFallback, rowOffset, colOffset, out value);
                case ConditionalFormulaIfsExpression ifsExpression:
                    return TryEvaluateFormulaIfsValue(ifsExpression, rowOffset, colOffset, out value);
                case ConditionalFormulaSwitchExpression switchExpression:
                    return TryEvaluateFormulaSwitchValue(switchExpression, rowOffset, colOffset, out value);
                default:
                    return false;
            }
        }

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

        private bool TryEvaluateFormulaIfValue(
            ConditionalFormulaIfExpression ifExpression,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            if (!TryEvaluateFormulaExpressionValue(ifExpression.Condition, rowOffset, colOffset, out var conditionValue))
            {
                value = ErrorValue.Value;
                return false;
            }

            if (conditionValue is ErrorValue)
            {
                value = conditionValue;
                return true;
            }

            var condition = FormulaBooleanValue(conditionValue);
            if (!condition.HasValue)
            {
                value = ErrorValue.Value;
                return false;
            }

            return TryEvaluateFormulaExpressionValue(
                condition.Value ? ifExpression.WhenTrue : ifExpression.WhenFalse,
                rowOffset,
                colOffset,
                out value);
        }

        private bool TryEvaluateFormulaErrorFallbackValue(
            ConditionalFormulaErrorFallbackExpression errorFallback,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            if (!TryEvaluateFormulaExpressionValue(errorFallback.Value, rowOffset, colOffset, out value))
                return false;

            if (value is not ErrorValue error || !FormulaErrorFallbackMatches(errorFallback.Kind, error))
                return true;

            return TryEvaluateFormulaExpressionValue(errorFallback.Fallback, rowOffset, colOffset, out value);
        }

        private static bool FormulaErrorFallbackMatches(
            ConditionalFormulaErrorFallbackKind kind,
            ErrorValue error) =>
            kind switch
            {
                ConditionalFormulaErrorFallbackKind.IfError => true,
                ConditionalFormulaErrorFallbackKind.IfNa => IsNaError(error),
                _ => false
            };

        private bool TryEvaluateFormulaIfsValue(
            ConditionalFormulaIfsExpression ifsExpression,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            for (var i = 0; i < ifsExpression.Branches.Count; i++)
            {
                var branch = ifsExpression.Branches[i];
                if (!TryEvaluateFormulaExpressionValue(branch.Condition, rowOffset, colOffset, out var conditionValue))
                {
                    value = ErrorValue.Value;
                    return false;
                }

                if (conditionValue is ErrorValue)
                {
                    value = conditionValue;
                    return true;
                }

                var condition = FormulaBooleanValue(conditionValue);
                if (!condition.HasValue)
                {
                    value = ErrorValue.Value;
                    return false;
                }

                if (condition.Value)
                    return TryEvaluateFormulaExpressionValue(branch.Value, rowOffset, colOffset, out value);
            }

            value = ErrorValue.NA;
            return true;
        }

        private bool TryEvaluateFormulaSwitchValue(
            ConditionalFormulaSwitchExpression switchExpression,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            if (!TryEvaluateFormulaExpressionValue(switchExpression.Selector, rowOffset, colOffset, out var selectorValue) ||
                selectorValue is RangeValue)
            {
                value = ErrorValue.Value;
                return false;
            }

            if (selectorValue is ErrorValue)
            {
                value = selectorValue;
                return true;
            }

            for (var i = 0; i < switchExpression.Cases.Count; i++)
            {
                var switchCase = switchExpression.Cases[i];
                if (!TryEvaluateFormulaExpressionValue(switchCase.MatchValue, rowOffset, colOffset, out var matchValue) ||
                    matchValue is RangeValue)
                {
                    value = ErrorValue.Value;
                    return false;
                }

                if (matchValue is ErrorValue)
                {
                    value = matchValue;
                    return true;
                }

                if (CompareFormulaValues(selectorValue, matchValue) == 0)
                    return TryEvaluateFormulaExpressionValue(switchCase.Result, rowOffset, colOffset, out value);
            }

            if (switchExpression.DefaultValue is { } defaultValue)
                return TryEvaluateFormulaExpressionValue(defaultValue, rowOffset, colOffset, out value);

            value = ErrorValue.NA;
            return true;
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
            if (!TryEvaluateFormulaComparisonValue(comparison, rowOffset, colOffset, out var value))
                return null;

            return FormulaBooleanValue(value);
        }

        private bool TryEvaluateFormulaComparisonValue(
            ConditionalFormulaComparison comparison,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            if (!TryResolveFormulaOperand(comparison.Left, rowOffset, colOffset, out var left) ||
                !TryResolveFormulaOperand(comparison.Right, rowOffset, colOffset, out var right))
            {
                value = ErrorValue.Value;
                return false;
            }

            if (left is ErrorValue)
            {
                value = left;
                return true;
            }

            if (right is ErrorValue)
            {
                value = right;
                return true;
            }

            if (left is RangeValue || right is RangeValue)
            {
                value = ErrorValue.Value;
                return false;
            }

            var result = CompareFormulaValues(left, right);
            value = new BoolValue(comparison.Operator switch
            {
                BinaryOperator.Equal => result == 0,
                BinaryOperator.NotEqual => result != 0,
                BinaryOperator.LessThan => result < 0,
                BinaryOperator.GreaterThan => result > 0,
                BinaryOperator.LessOrEqual => result <= 0,
                BinaryOperator.GreaterOrEqual => result >= 0,
                _ => false
            });
            return true;
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
                case ConditionalFormulaScalarFunctionKind.NormDist:
                case ConditionalFormulaScalarFunctionKind.NormInv:
                case ConditionalFormulaScalarFunctionKind.NormSDistCompat:
                case ConditionalFormulaScalarFunctionKind.NormSDist:
                case ConditionalFormulaScalarFunctionKind.NormSInv:
                case ConditionalFormulaScalarFunctionKind.Phi:
                case ConditionalFormulaScalarFunctionKind.Gauss:
                case ConditionalFormulaScalarFunctionKind.Standardize:
                    return TryEvaluateFormulaNormalDistributionFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.TDistCompat:
                case ConditionalFormulaScalarFunctionKind.TDist:
                case ConditionalFormulaScalarFunctionKind.TDistRt:
                case ConditionalFormulaScalarFunctionKind.TDist2T:
                case ConditionalFormulaScalarFunctionKind.TInv:
                case ConditionalFormulaScalarFunctionKind.TInv2T:
                case ConditionalFormulaScalarFunctionKind.FDist:
                case ConditionalFormulaScalarFunctionKind.FDistRt:
                case ConditionalFormulaScalarFunctionKind.FInv:
                case ConditionalFormulaScalarFunctionKind.FInvRt:
                case ConditionalFormulaScalarFunctionKind.ChiSqDist:
                case ConditionalFormulaScalarFunctionKind.ChiSqDistRt:
                case ConditionalFormulaScalarFunctionKind.ChiSqInv:
                case ConditionalFormulaScalarFunctionKind.ChiSqInvRt:
                    return TryEvaluateFormulaTFChiSquareDistributionFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.BetaDist:
                case ConditionalFormulaScalarFunctionKind.BetaDistCompat:
                case ConditionalFormulaScalarFunctionKind.BetaInv:
                case ConditionalFormulaScalarFunctionKind.Gamma:
                case ConditionalFormulaScalarFunctionKind.GammaDist:
                case ConditionalFormulaScalarFunctionKind.GammaInv:
                case ConditionalFormulaScalarFunctionKind.GammaLn:
                case ConditionalFormulaScalarFunctionKind.LogNormDist:
                case ConditionalFormulaScalarFunctionKind.LogNormDistCompat:
                case ConditionalFormulaScalarFunctionKind.LogNormInv:
                case ConditionalFormulaScalarFunctionKind.ExponDist:
                case ConditionalFormulaScalarFunctionKind.WeibullDist:
                    return TryEvaluateFormulaContinuousDistributionFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Pmt:
                case ConditionalFormulaScalarFunctionKind.Pv:
                case ConditionalFormulaScalarFunctionKind.Fv:
                case ConditionalFormulaScalarFunctionKind.Nper:
                case ConditionalFormulaScalarFunctionKind.Rate:
                case ConditionalFormulaScalarFunctionKind.Ipmt:
                case ConditionalFormulaScalarFunctionKind.Ppmt:
                case ConditionalFormulaScalarFunctionKind.Ispmt:
                    return TryEvaluateFormulaFinancialAnnuityFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Npv:
                case ConditionalFormulaScalarFunctionKind.Irr:
                case ConditionalFormulaScalarFunctionKind.Mirr:
                case ConditionalFormulaScalarFunctionKind.Xnpv:
                case ConditionalFormulaScalarFunctionKind.Xirr:
                    return TryEvaluateFormulaFinancialCashFlowFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Disc:
                case ConditionalFormulaScalarFunctionKind.Intrate:
                case ConditionalFormulaScalarFunctionKind.Received:
                case ConditionalFormulaScalarFunctionKind.Pricedisc:
                case ConditionalFormulaScalarFunctionKind.Pricemat:
                case ConditionalFormulaScalarFunctionKind.Tbilleq:
                case ConditionalFormulaScalarFunctionKind.Tbillprice:
                case ConditionalFormulaScalarFunctionKind.Tbillyield:
                case ConditionalFormulaScalarFunctionKind.Duration:
                case ConditionalFormulaScalarFunctionKind.Mduration:
                case ConditionalFormulaScalarFunctionKind.Price:
                case ConditionalFormulaScalarFunctionKind.Yield:
                case ConditionalFormulaScalarFunctionKind.Yielddisc:
                case ConditionalFormulaScalarFunctionKind.Yieldmat:
                case ConditionalFormulaScalarFunctionKind.Oddfprice:
                case ConditionalFormulaScalarFunctionKind.Oddfyield:
                case ConditionalFormulaScalarFunctionKind.Oddlprice:
                case ConditionalFormulaScalarFunctionKind.Oddlyield:
                case ConditionalFormulaScalarFunctionKind.Sln:
                case ConditionalFormulaScalarFunctionKind.Syd:
                case ConditionalFormulaScalarFunctionKind.Db:
                case ConditionalFormulaScalarFunctionKind.Ddb:
                case ConditionalFormulaScalarFunctionKind.Vdb:
                case ConditionalFormulaScalarFunctionKind.Effect:
                case ConditionalFormulaScalarFunctionKind.Nominal:
                case ConditionalFormulaScalarFunctionKind.Rri:
                case ConditionalFormulaScalarFunctionKind.Pduration:
                case ConditionalFormulaScalarFunctionKind.Coupdaybs:
                case ConditionalFormulaScalarFunctionKind.Coupdays:
                case ConditionalFormulaScalarFunctionKind.Coupdaysnc:
                case ConditionalFormulaScalarFunctionKind.Coupncd:
                case ConditionalFormulaScalarFunctionKind.Coupnum:
                case ConditionalFormulaScalarFunctionKind.Couppcd:
                    return TryEvaluateFormulaFinancialScalarFunction(function, rowOffset, colOffset, out value);
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
                case ConditionalFormulaScalarFunctionKind.Text:
                    return TryEvaluateFormulaTextFormatFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Fixed:
                    return TryEvaluateFormulaFixedFunction(function, rowOffset, colOffset, out value);
                case ConditionalFormulaScalarFunctionKind.Dollar:
                    return TryEvaluateFormulaDollarFunction(function, rowOffset, colOffset, out value);
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
                case ConditionalFormulaScalarFunctionKind.Choose:
                case ConditionalFormulaScalarFunctionKind.Match:
                case ConditionalFormulaScalarFunctionKind.XMatch:
                case ConditionalFormulaScalarFunctionKind.Index:
                case ConditionalFormulaScalarFunctionKind.VLookup:
                case ConditionalFormulaScalarFunctionKind.HLookup:
                    return TryEvaluateFormulaLookupReferenceFunction(function, rowOffset, colOffset, out value);
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

        private bool TryEvaluateFormulaLookupReferenceFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            return function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Choose =>
                    TryEvaluateFormulaChooseFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Match =>
                    TryEvaluateFormulaMatchFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.XMatch =>
                    TryEvaluateFormulaXMatchFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Index =>
                    TryEvaluateFormulaIndexFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.VLookup =>
                    TryEvaluateFormulaLookupFunction(function, rowOffset, colOffset, vertical: true, out value),
                ConditionalFormulaScalarFunctionKind.HLookup =>
                    TryEvaluateFormulaLookupFunction(function, rowOffset, colOffset, vertical: false, out value),
                _ => false
            };
        }

        private bool TryEvaluateFormulaChooseFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[0], rowOffset, colOffset, out var indexValue))
                return false;

            if (indexValue is ErrorValue indexError)
            {
                value = indexError;
                return true;
            }

            if (!TryGetFormulaLookupInteger(indexValue, out var index) ||
                index < 1 ||
                index >= function.Arguments.Count)
            {
                value = ErrorValue.Value;
                return true;
            }

            return TryResolveFormulaLookupResultArgument(function.Arguments[index], rowOffset, colOffset, out value);
        }

        private bool TryEvaluateFormulaMatchFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[0], rowOffset, colOffset, out var lookupValue))
                return false;

            if (lookupValue is ErrorValue lookupError)
            {
                value = lookupError;
                return true;
            }

            if (!TryResolveFormulaLookupRangeArgument(
                    function.Arguments[1],
                    rowOffset,
                    colOffset,
                    out var lookupRange,
                    out var rangeError))
            {
                return false;
            }

            if (rangeError is not null)
            {
                value = rangeError;
                return true;
            }

            if (!TryGetFormulaLookupVector(lookupRange, out var lookupVector))
            {
                value = ErrorValue.NA;
                return true;
            }

            var matchType = 1;
            if (function.Arguments.Count > 2)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[2], rowOffset, colOffset, out var matchTypeValue))
                    return false;

                if (matchTypeValue is ErrorValue matchTypeError)
                {
                    value = matchTypeError;
                    return true;
                }

                if (!TryGetFormulaLookupInteger(matchTypeValue, out matchType) ||
                    matchType is < -1 or > 1)
                {
                    value = ErrorValue.NA;
                    return true;
                }
            }

            if (!TryFindFormulaLookupMatch(lookupVector, lookupValue, matchType, reverse: false, out var matchIndex, out var matchError))
            {
                if (matchError is not null)
                {
                    value = matchError;
                    return true;
                }

                return false;
            }

            value = new NumberValue(matchIndex);
            return true;
        }

        private bool TryEvaluateFormulaXMatchFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[0], rowOffset, colOffset, out var lookupValue))
                return false;

            if (lookupValue is ErrorValue lookupError)
            {
                value = lookupError;
                return true;
            }

            if (!TryResolveFormulaLookupRangeArgument(
                    function.Arguments[1],
                    rowOffset,
                    colOffset,
                    out var lookupRange,
                    out var rangeError))
            {
                return false;
            }

            if (rangeError is not null)
            {
                value = rangeError;
                return true;
            }

            if (!TryGetFormulaLookupVector(lookupRange, out var lookupVector))
            {
                value = ErrorValue.Value;
                return true;
            }

            var matchMode = 0;
            if (function.Arguments.Count > 2)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[2], rowOffset, colOffset, out var matchModeValue))
                    return false;

                if (matchModeValue is ErrorValue matchModeError)
                {
                    value = matchModeError;
                    return true;
                }

                if (!TryGetFormulaLookupInteger(matchModeValue, out matchMode) ||
                    matchMode is < -1 or > 1)
                {
                    return false;
                }
            }

            var searchMode = 1;
            if (function.Arguments.Count > 3)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[3], rowOffset, colOffset, out var searchModeValue))
                    return false;

                if (searchModeValue is ErrorValue searchModeError)
                {
                    value = searchModeError;
                    return true;
                }

                if (!TryGetFormulaLookupInteger(searchModeValue, out searchMode) ||
                    searchMode is not (1 or -1))
                {
                    return false;
                }
            }

            if (!TryFindFormulaXMatch(lookupVector, lookupValue, matchMode, reverse: searchMode < 0, out var matchIndex, out var matchError))
            {
                if (matchError is not null)
                {
                    value = matchError;
                    return true;
                }

                return false;
            }

            value = new NumberValue(matchIndex);
            return true;
        }

        private bool TryEvaluateFormulaIndexFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaLookupRangeArgument(
                    function.Arguments[0],
                    rowOffset,
                    colOffset,
                    out var range,
                    out var rangeError))
            {
                return false;
            }

            if (rangeError is not null)
            {
                value = rangeError;
                return true;
            }

            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[1], rowOffset, colOffset, out var rowValue))
                return false;

            if (rowValue is ErrorValue rowError)
            {
                value = rowError;
                return true;
            }

            if (!TryGetFormulaLookupInteger(rowValue, out var rowIndex))
            {
                value = ErrorValue.Value;
                return true;
            }

            var columnSpecified = function.Arguments.Count > 2;
            var colIndex = 1;
            if (columnSpecified)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[2], rowOffset, colOffset, out var colValue))
                    return false;

                if (colValue is ErrorValue colError)
                {
                    value = colError;
                    return true;
                }

                if (!TryGetFormulaLookupInteger(colValue, out colIndex))
                {
                    value = ErrorValue.Value;
                    return true;
                }
            }
            else if (range.RowCount == 1 && range.ColCount > 1)
            {
                colIndex = rowIndex;
                rowIndex = 1;
            }

            if (function.Arguments.Count > 3)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[3], rowOffset, colOffset, out var areaValue))
                    return false;

                if (areaValue is ErrorValue areaError)
                {
                    value = areaError;
                    return true;
                }

                if (!TryGetFormulaLookupInteger(areaValue, out var areaIndex))
                {
                    value = ErrorValue.Value;
                    return true;
                }

                if (areaIndex != 1)
                {
                    value = ErrorValue.Ref;
                    return true;
                }
            }

            if (rowIndex < 0 || colIndex < 0)
            {
                value = ErrorValue.Ref;
                return true;
            }

            if (rowIndex == 0 && colIndex == 0)
            {
                value = range;
                return true;
            }

            if (rowIndex == 0)
            {
                if (colIndex < 1 || colIndex > range.ColCount)
                {
                    value = ErrorValue.Ref;
                    return true;
                }

                value = FormulaLookupColumnRange(range, colIndex);
                return true;
            }

            if (colIndex == 0)
            {
                if (rowIndex < 1 || rowIndex > range.RowCount)
                {
                    value = ErrorValue.Ref;
                    return true;
                }

                value = FormulaLookupRowRange(range, rowIndex);
                return true;
            }

            if (rowIndex > range.RowCount || colIndex > range.ColCount)
            {
                value = ErrorValue.Ref;
                return true;
            }

            value = range.Cells[rowIndex - 1, colIndex - 1];
            return true;
        }

        private bool TryEvaluateFormulaLookupFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            bool vertical,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[0], rowOffset, colOffset, out var lookupValue))
                return false;

            if (lookupValue is ErrorValue lookupError)
            {
                value = lookupError;
                return true;
            }

            if (!TryResolveFormulaLookupRangeArgument(
                    function.Arguments[1],
                    rowOffset,
                    colOffset,
                    out var tableRange,
                    out var tableError))
            {
                return false;
            }

            if (tableError is not null)
            {
                value = tableError;
                return true;
            }

            if (!TryResolveFormulaLookupScalarArgument(function.Arguments[2], rowOffset, colOffset, out var resultIndexValue))
                return false;

            if (resultIndexValue is ErrorValue resultIndexError)
            {
                value = resultIndexError;
                return true;
            }

            if (!TryGetFormulaLookupInteger(resultIndexValue, out var resultIndex) ||
                resultIndex < 1)
            {
                value = ErrorValue.Value;
                return true;
            }

            var resultLimit = vertical ? tableRange.ColCount : tableRange.RowCount;
            if (resultIndex > resultLimit)
            {
                value = ErrorValue.Ref;
                return true;
            }

            var approximate = true;
            if (function.Arguments.Count > 3)
            {
                if (!TryResolveFormulaLookupScalarArgument(function.Arguments[3], rowOffset, colOffset, out var rangeLookupValue))
                    return false;

                if (rangeLookupValue is ErrorValue rangeLookupError)
                {
                    value = rangeLookupError;
                    return true;
                }

                var boolean = FormulaBooleanValue(rangeLookupValue);
                if (!boolean.HasValue)
                {
                    value = ErrorValue.Value;
                    return true;
                }

                approximate = boolean.Value;
            }

            var lookupVector = vertical
                ? tableRange.GetColumn(1)
                : tableRange.GetRow(1);
            var matchType = approximate ? 1 : 0;
            if (!TryFindFormulaLookupMatch(lookupVector, lookupValue, matchType, reverse: false, out var matchIndex, out var matchError))
            {
                if (matchError is not null)
                {
                    value = matchError;
                    return true;
                }

                return false;
            }

            value = vertical
                ? tableRange.Cells[matchIndex - 1, resultIndex - 1]
                : tableRange.Cells[resultIndex - 1, matchIndex - 1];
            return true;
        }

        private bool TryResolveFormulaLookupResultArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var range))
                    return false;

                value = FormulaLookupRangeResult(range);
                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out value))
                return false;

            if (value is RangeValue resolvedRange)
                value = FormulaLookupRangeResult(resolvedRange);

            return true;
        }

        private bool TryResolveFormulaLookupScalarArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var range))
                    return false;

                if (!TryGetSingleFormulaLookupRangeValue(range, out value))
                    value = ErrorValue.Value;

                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out value))
                return false;

            if (value is RangeValue resolvedRange &&
                !TryGetSingleFormulaLookupRangeValue(resolvedRange, out value))
            {
                value = ErrorValue.Value;
            }

            return true;
        }

        private bool TryResolveFormulaLookupRangeArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            out RangeValue range,
            out ErrorValue? error)
        {
            range = default!;
            error = null;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                return TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out range);

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return true;
            }

            range = value is RangeValue resolvedRange
                ? resolvedRange
                : SingleFormulaLookupRange(value);
            return true;
        }

        private static bool TryGetSingleFormulaLookupRangeValue(RangeValue range, out ScalarValue value)
        {
            if (range.RowCount == 1 && range.ColCount == 1)
            {
                value = range.Cells[0, 0];
                return true;
            }

            value = ErrorValue.Value;
            return false;
        }

        private static ScalarValue FormulaLookupRangeResult(RangeValue range) =>
            TryGetSingleFormulaLookupRangeValue(range, out var value) ? value : range;

        private static RangeValue SingleFormulaLookupRange(ScalarValue value) =>
            new(new[,] { { value } });

        private static RangeValue FormulaLookupColumnRange(RangeValue range, int colIndex)
        {
            var cells = new ScalarValue[range.RowCount, 1];
            for (var row = 0; row < range.RowCount; row++)
                cells[row, 0] = range.Cells[row, colIndex - 1];

            return new RangeValue(cells, range.StartRow, range.StartCol + (uint)(colIndex - 1))
            {
                SheetName = range.SheetName
            };
        }

        private static RangeValue FormulaLookupRowRange(RangeValue range, int rowIndex)
        {
            var cells = new ScalarValue[1, range.ColCount];
            for (var col = 0; col < range.ColCount; col++)
                cells[0, col] = range.Cells[rowIndex - 1, col];

            return new RangeValue(cells, range.StartRow + (uint)(rowIndex - 1), range.StartCol)
            {
                SheetName = range.SheetName
            };
        }

        private static bool TryGetFormulaLookupVector(RangeValue range, out IReadOnlyList<ScalarValue> values)
        {
            if (range.RowCount == 1)
            {
                values = range.GetRow(1);
                return true;
            }

            if (range.ColCount == 1)
            {
                values = range.GetColumn(1);
                return true;
            }

            values = Array.Empty<ScalarValue>();
            return false;
        }

        private static bool TryGetFormulaLookupInteger(ScalarValue value, out int integer)
        {
            integer = 0;
            if (!TryGetFormulaCoercedNumber(value, out var number) ||
                number < int.MinValue ||
                number > int.MaxValue)
            {
                return false;
            }

            integer = (int)Math.Truncate(number);
            return true;
        }

        private static bool TryFindFormulaLookupMatch(
            IReadOnlyList<ScalarValue> lookupVector,
            ScalarValue lookupValue,
            int matchType,
            bool reverse,
            out int oneBasedIndex,
            out ErrorValue? error)
        {
            oneBasedIndex = 0;
            error = null;
            if (matchType == 0)
                return TryFindFormulaExactLookupMatch(lookupVector, lookupValue, reverse, out oneBasedIndex, out error);

            var ascending = matchType > 0;
            if (!FormulaLookupVectorIsSorted(lookupVector, ascending, out error))
                return false;

            for (var i = 0; i < lookupVector.Count; i++)
            {
                if (!TryCompareFormulaLookupValues(lookupVector[i], lookupValue, out var comparison, out error))
                    return false;

                if ((ascending && comparison <= 0) ||
                    (!ascending && comparison >= 0))
                {
                    oneBasedIndex = i + 1;
                }
            }

            if (oneBasedIndex > 0)
                return true;

            error = ErrorValue.NA;
            return false;
        }

        private static bool TryFindFormulaXMatch(
            IReadOnlyList<ScalarValue> lookupVector,
            ScalarValue lookupValue,
            int matchMode,
            bool reverse,
            out int oneBasedIndex,
            out ErrorValue? error)
        {
            if (TryFindFormulaExactLookupMatch(lookupVector, lookupValue, reverse, out oneBasedIndex, out error))
                return true;

            if (error is not null && error != ErrorValue.NA)
                return false;

            if (matchMode == 0)
            {
                error = ErrorValue.NA;
                return false;
            }

            error = null;
            var bestIndex = 0;
            ScalarValue? bestValue = null;
            foreach (var index in FormulaLookupSearchIndexes(lookupVector.Count, reverse))
            {
                var candidate = lookupVector[index];
                if (!TryCompareFormulaLookupValues(candidate, lookupValue, out var comparison, out error))
                    return false;

                var candidateMatches = matchMode < 0
                    ? comparison <= 0
                    : comparison >= 0;
                if (!candidateMatches)
                    continue;

                if (bestValue is null)
                {
                    bestValue = candidate;
                    bestIndex = index + 1;
                    continue;
                }

                if (!TryCompareFormulaLookupValues(candidate, bestValue, out var bestComparison, out error))
                    return false;

                if ((matchMode < 0 && bestComparison > 0) ||
                    (matchMode > 0 && bestComparison < 0))
                {
                    bestValue = candidate;
                    bestIndex = index + 1;
                }
            }

            if (bestIndex > 0)
            {
                oneBasedIndex = bestIndex;
                return true;
            }

            oneBasedIndex = 0;
            error = ErrorValue.NA;
            return false;
        }

        private static bool TryFindFormulaExactLookupMatch(
            IReadOnlyList<ScalarValue> lookupVector,
            ScalarValue lookupValue,
            bool reverse,
            out int oneBasedIndex,
            out ErrorValue? error)
        {
            oneBasedIndex = 0;
            error = null;
            foreach (var index in FormulaLookupSearchIndexes(lookupVector.Count, reverse))
            {
                if (!TryCompareFormulaLookupValues(lookupVector[index], lookupValue, out var comparison, out error))
                    return false;

                if (comparison == 0)
                {
                    oneBasedIndex = index + 1;
                    return true;
                }
            }

            error = ErrorValue.NA;
            return false;
        }

        private static IEnumerable<int> FormulaLookupSearchIndexes(int count, bool reverse)
        {
            if (reverse)
            {
                for (var i = count - 1; i >= 0; i--)
                    yield return i;
            }
            else
            {
                for (var i = 0; i < count; i++)
                    yield return i;
            }
        }

        private static bool FormulaLookupVectorIsSorted(
            IReadOnlyList<ScalarValue> lookupVector,
            bool ascending,
            out ErrorValue? error)
        {
            error = null;
            for (var i = 1; i < lookupVector.Count; i++)
            {
                if (!TryCompareFormulaLookupValues(lookupVector[i - 1], lookupVector[i], out var comparison, out error))
                    return false;

                if ((ascending && comparison > 0) ||
                    (!ascending && comparison < 0))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCompareFormulaLookupValues(
            ScalarValue left,
            ScalarValue right,
            out int comparison,
            out ErrorValue? error)
        {
            comparison = 0;
            error = null;
            if (left is ErrorValue leftError)
            {
                error = leftError;
                return false;
            }

            if (right is ErrorValue rightError)
            {
                error = rightError;
                return false;
            }

            if (left is RangeValue || right is RangeValue)
            {
                error = ErrorValue.Value;
                return false;
            }

            comparison = CompareFormulaValues(left, right);
            return true;
        }

        private bool TryEvaluateFormulaFinancialAnnuityFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[function.Arguments.Count];
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                if (function.Arguments[i].Kind == ConditionalFormulaOperandKind.ReferenceRange ||
                    !TryResolveFormulaOperand(function.Arguments[i], rowOffset, colOffset, out arguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is ErrorValue error)
                {
                    value = error;
                    return true;
                }
            }

            if (arguments.Any(static argument => argument is RangeValue))
                return false;

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.Pmt:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var pmtRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var pmtNper) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var pmtPv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var pmtFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var pmtType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaPmtScalar(pmtRate, pmtNper, pmtPv, pmtFv, pmtType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Pv:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var pvRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var pvNper) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var pvPmt) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var pvFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var pvType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaPvScalar(pvRate, pvNper, pvPmt, pvFv, pvType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Fv:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var fvRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var fvNper) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var fvPmt) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var fvPv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var fvType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaFvScalar(fvRate, fvNper, fvPmt, fvPv, fvType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Nper:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var nperRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var nperPmt) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var nperPv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var nperFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var nperType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNperScalar(nperRate, nperPmt, nperPv, nperFv, nperType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Rate:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var rateNper) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var ratePmt) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var ratePv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var rateFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var rateType) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 5, 0.1d, out var rateGuess))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaRateScalar(rateNper, ratePmt, ratePv, rateFv, rateType, rateGuess);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Ipmt:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var ipmtRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var ipmtPer) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var ipmtNper) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var ipmtPv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var ipmtFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 5, 0d, out var ipmtType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaIpmtScalar(ipmtRate, ipmtPer, ipmtNper, ipmtPv, ipmtFv, ipmtType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Ppmt:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var ppmtRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var ppmtPer) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var ppmtNper) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var ppmtPv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 0d, out var ppmtFv) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 5, 0d, out var ppmtType))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaPpmtScalar(ppmtRate, ppmtPer, ppmtNper, ppmtPv, ppmtFv, ppmtType);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Ispmt:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var ispmtRate) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var ispmtPer) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var ispmtNper) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var ispmtPv))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaIspmtScalar(ispmtRate, ispmtPer, ispmtNper, ispmtPv);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFormulaFinancialNumber(ScalarValue value, out double number) =>
            TryGetFormulaCoercedNumber(value, out number);

        private static bool TryGetFormulaFinancialOptionalNumber(
            IReadOnlyList<ScalarValue> arguments,
            int index,
            double defaultValue,
            out double number)
        {
            if (arguments.Count <= index || arguments[index] is BlankValue)
            {
                number = defaultValue;
                return true;
            }

            return TryGetFormulaFinancialNumber(arguments[index], out number);
        }

        private static bool IsValidFormulaFinancialPaymentType(double type) =>
            double.IsFinite(type) && (type == 0d || type == 1d);

        private static ScalarValue FormulaPmtScalar(double rate, double nper, double pv, double fv, double type)
        {
            if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
                return ErrorValue.Num;

            if (!IsValidFormulaFinancialPaymentType(type))
                return ErrorValue.Num;

            if (nper == 0d)
                return ErrorValue.DivByZero;

            if (Math.Abs(rate) < 1e-10d)
                return FormulaFinancialNumberResult(-(pv + fv) / nper);

            var rn = Math.Pow(1d + rate, nper);
            return FormulaFinancialNumberResult(-(pv * rn + fv) * rate / ((1d + rate * type) * (rn - 1d)));
        }

        private static ScalarValue FormulaPvScalar(double rate, double nper, double pmt, double fv, double type)
        {
            if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pmt) || !double.IsFinite(fv) || !double.IsFinite(type))
                return ErrorValue.Num;

            if (!IsValidFormulaFinancialPaymentType(type))
                return ErrorValue.Num;

            if (nper == 0d)
                return ErrorValue.DivByZero;

            if (Math.Abs(rate) < 1e-10d)
                return FormulaFinancialNumberResult(-pmt * nper - fv);

            var rn = Math.Pow(1d + rate, nper);
            return FormulaFinancialNumberResult((-pmt * (1d + rate * type) * (rn - 1d) / rate - fv) / rn);
        }

        private static ScalarValue FormulaFvScalar(double rate, double nper, double pmt, double pv, double type)
        {
            if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(type))
                return ErrorValue.Num;

            if (!IsValidFormulaFinancialPaymentType(type))
                return ErrorValue.Num;

            if (Math.Abs(rate) < 1e-10d)
                return FormulaFinancialNumberResult(-pv - pmt * nper);

            var rn = Math.Pow(1d + rate, nper);
            return FormulaFinancialNumberResult(-pv * rn - pmt * (1d + rate * type) * (rn - 1d) / rate);
        }

        private static ScalarValue FormulaNperScalar(double rate, double pmt, double pv, double fv, double type)
        {
            if (!double.IsFinite(rate) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
                return ErrorValue.Num;

            if (!IsValidFormulaFinancialPaymentType(type))
                return ErrorValue.Num;

            if (Math.Abs(rate) < 1e-10d)
            {
                if (Math.Abs(pmt) < 1e-10d)
                    return ErrorValue.DivByZero;

                return FormulaFinancialNumberResult(-(pv + fv) / pmt);
            }

            var pmtAdjusted = pmt * (1d + rate * type);
            var ratio = (pmtAdjusted - fv * rate) / (pmtAdjusted + pv * rate);
            if (ratio <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(Math.Log(ratio) / Math.Log(1d + rate));
        }

        private static ScalarValue FormulaRateScalar(double nper, double pmt, double pv, double fv, double type, double guess)
        {
            if (!double.IsFinite(nper) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) || !double.IsFinite(guess))
                return ErrorValue.Num;

            if (!IsValidFormulaFinancialPaymentType(type))
                return ErrorValue.Num;

            if (nper == 0d)
                return ErrorValue.DivByZero;

            var rate = guess;
            for (var i = 0; i < 100; i++)
            {
                var rn = Math.Pow(1d + rate, nper);
                var rn1 = nper * Math.Pow(1d + rate, nper - 1d);
                double f;
                double df;
                if (Math.Abs(rate) < 1e-10d)
                {
                    f = pv + pmt * nper + fv;
                    df = pv * nper + pmt * nper * (nper - 1d) / 2d;
                }
                else
                {
                    f = pv * rn + pmt * (1d + rate * type) * (rn - 1d) / rate + fv;
                    df = pv * rn1
                        + pmt * type * (rn - 1d) / rate
                        + pmt * (1d + rate * type) * (rn1 * rate - (rn - 1d)) / (rate * rate);
                }

                if (Math.Abs(df) < 1e-15d)
                    break;

                var delta = f / df;
                rate -= delta;
                if (Math.Abs(delta) < 1e-10d)
                    break;
            }

            return FormulaFinancialNumberResult(rate);
        }

        private static ScalarValue FormulaIpmtScalar(double rate, double per, double nper, double pv, double fv, double type)
        {
            if (!TryGetFormulaFinancialPaymentPeriod(rate, per, nper, pv, fv, type, out var period, out var paymentType))
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(FormulaFinancialCalcIpmt(rate, period, nper, pv, fv, paymentType));
        }

        private static ScalarValue FormulaPpmtScalar(double rate, double per, double nper, double pv, double fv, double type)
        {
            if (!TryGetFormulaFinancialPaymentPeriod(rate, per, nper, pv, fv, type, out var period, out var paymentType))
                return ErrorValue.Num;

            var pmt = FormulaFinancialCalcPmt(rate, nper, pv, fv, paymentType);
            var ipmt = FormulaFinancialCalcIpmt(rate, period, nper, pv, fv, paymentType);
            return FormulaFinancialNumberResult(pmt - ipmt);
        }

        private static ScalarValue FormulaIspmtScalar(double rate, double per, double nper, double pv)
        {
            per = Math.Truncate(per);
            if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) || !double.IsFinite(pv))
                return ErrorValue.Num;

            if (nper <= 0d || per < 0d || per > nper)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(-pv * rate * (nper - per) / nper);
        }

        private static bool TryGetFormulaFinancialPaymentPeriod(
            double rate,
            double per,
            double nper,
            double pv,
            double fv,
            double type,
            out int period,
            out int paymentType)
        {
            period = 0;
            paymentType = 0;
            if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
                !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) ||
                per < int.MinValue || per > int.MaxValue ||
                nper < int.MinValue || nper > int.MaxValue ||
                type < int.MinValue || type > int.MaxValue)
            {
                return false;
            }

            paymentType = (int)Math.Truncate(type);
            if (paymentType != 0 && paymentType != 1)
                return false;

            if (nper <= 0d)
                return false;

            period = (int)Math.Truncate(per);
            var periodCount = (int)Math.Truncate(nper);
            return period >= 1 && period <= periodCount;
        }

        private static double FormulaFinancialCalcPmt(double rate, double nper, double pv, double fv, int type)
        {
            if (Math.Abs(rate) < 1e-14d)
                return -(pv + fv) / nper;

            var r1 = Math.Pow(1d + rate, nper);
            return -(pv * r1 + fv) * rate / ((1d + rate * type) * (r1 - 1d));
        }

        private static double FormulaFinancialCalcIpmt(double rate, double per, double nper, double pv, double fv, int type)
        {
            var pmt = FormulaFinancialCalcPmt(rate, nper, pv, fv, type);
            if (Math.Abs(rate) < 1e-14d)
                return 0d;

            var pvAtPeriod = pv * Math.Pow(1d + rate, per - 1d)
                + pmt * (1d + rate * type) * (Math.Pow(1d + rate, per - 1d) - 1d) / rate;
            return type == 0 ? -(pvAtPeriod * rate) : -((pvAtPeriod - pmt) * rate);
        }

        private static ScalarValue FormulaFinancialNumberResult(double result) =>
            double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;

        private bool TryEvaluateFormulaNormalDistributionFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[function.Arguments.Count];
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                if (function.Arguments[i].Kind == ConditionalFormulaOperandKind.ReferenceRange ||
                    !TryResolveFormulaOperand(function.Arguments[i], rowOffset, colOffset, out arguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is ErrorValue error)
                {
                    value = error;
                    return true;
                }
            }

            if (arguments.Any(static argument => argument is RangeValue))
                return false;

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.NormDist:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var normDistX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var normDistMean) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var normDistStdev) ||
                        !TryGetFormulaNormalBoolean(arguments[3], out var normDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormDistScalar(normDistX, normDistMean, normDistStdev, normDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.NormInv:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var normInvProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var normInvMean) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var normInvStdev))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormInvScalar(normInvProbability, normInvMean, normInvStdev);
                    return true;
                case ConditionalFormulaScalarFunctionKind.NormSDistCompat:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var normSDistCompatZ))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormalNumberResult(FormulaNormSCdf(normSDistCompatZ));
                    return true;
                case ConditionalFormulaScalarFunctionKind.NormSDist:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var normSDistZ) ||
                        !TryGetFormulaNormalBoolean(arguments[1], out var normSDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormalNumberResult(normSDistCumulative
                        ? FormulaNormSCdf(normSDistZ)
                        : FormulaNormSPdf(normSDistZ));
                    return true;
                case ConditionalFormulaScalarFunctionKind.NormSInv:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var normSInvProbability))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormSInvScalar(normSInvProbability);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Phi:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var phiX))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormalNumberResult(FormulaNormSPdf(phiX));
                    return true;
                case ConditionalFormulaScalarFunctionKind.Gauss:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var gaussZ))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaNormalNumberResult(FormulaNormSCdf(gaussZ) - 0.5d);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Standardize:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var standardizeX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var standardizeMean) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var standardizeStdev))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaStandardizeScalar(standardizeX, standardizeMean, standardizeStdev);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFormulaNormalNumber(ScalarValue value, out double number)
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
                    number = boolean.Value ? 1d : 0d;
                    break;
                case BlankValue:
                    number = 0d;
                    break;
                default:
                    number = 0d;
                    return false;
            }

            return double.IsFinite(number);
        }

        private static bool TryGetFormulaNormalBoolean(ScalarValue value, out bool boolean)
        {
            switch (value)
            {
                case BoolValue logical:
                    boolean = logical.Value;
                    return true;
                case NumberValue numeric when double.IsFinite(numeric.Value):
                    boolean = numeric.Value != 0d;
                    return true;
                case DateTimeValue dateTime when double.IsFinite(dateTime.Value):
                    boolean = dateTime.Value != 0d;
                    return true;
                case BlankValue:
                    boolean = false;
                    return true;
                default:
                    boolean = false;
                    return false;
            }
        }

        private static ScalarValue FormulaNormDistScalar(double x, double mean, double stdev, bool cumulative)
        {
            if (stdev <= 0d)
                return ErrorValue.Num;

            var z = (x - mean) / stdev;
            return FormulaNormalNumberResult(cumulative ? FormulaNormSCdf(z) : FormulaNormSPdf(z) / stdev);
        }

        private static ScalarValue FormulaNormInvScalar(double probability, double mean, double stdev)
        {
            if (stdev <= 0d || probability <= 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaNormalNumberResult(FormulaNormSInv(probability) * stdev + mean);
        }

        private static ScalarValue FormulaNormSInvScalar(double probability)
        {
            if (probability <= 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaNormalNumberResult(FormulaNormSInv(probability));
        }

        private static ScalarValue FormulaStandardizeScalar(double x, double mean, double stdev)
        {
            if (stdev <= 0d)
                return ErrorValue.Num;

            return FormulaNormalNumberResult((x - mean) / stdev);
        }

        private static ScalarValue FormulaNormalNumberResult(double result) =>
            double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;

        private static double FormulaNormalErfc(double x)
        {
            var z = Math.Abs(x);
            var t = 2.0d / (2.0d + z);
            var ty = 4.0d * t - 2.0d;
            var d = 0.0d;
            var dd = 0.0d;
            ReadOnlySpan<double> coefficients =
            [
                -1.3026537197817094d,
                 0.64196979235649026d,
                 0.019476473204185836d,
                -0.009561514786808631d,
                -0.000946595344482036d,
                 0.000366839497852761d,
                 0.000042523324806907d,
                -0.000020278578112534d,
                -0.000001624290004647d,
                 0.00000130365583558d,
                 0.000000015626441722d,
                -0.000000085238095915d,
                 0.000000006529054439d,
                 0.000000005059343495d,
                -0.000000000991364156d,
                -0.000000000227365122d,
                 0.000000000096467911d,
                 0.000000000002394038d,
                -0.000000000006886027d,
                 0.000000000000894487d,
                 0.000000000000313092d,
                -0.000000000000112708d,
                 0.000000000000000381d,
                 0.000000000000007106d,
                -0.000000000000001523d,
                -0.000000000000000094d,
                 0.000000000000000121d,
                -0.000000000000000028d
            ];

            for (var j = coefficients.Length - 1; j > 0; j--)
            {
                var previous = d;
                d = ty * d - dd + coefficients[j];
                dd = previous;
            }

            var result = t * Math.Exp(-z * z + 0.5d * (coefficients[0] + ty * d) - dd);
            return x >= 0.0d ? result : 2.0d - result;
        }

        private static double FormulaNormalErf(double x) =>
            x >= 0.0d ? 1.0d - FormulaNormalErfc(x) : FormulaNormalErfc(-x) - 1.0d;

        private static double FormulaNormSCdf(double z) =>
            0.5d * (1.0d + FormulaNormalErf(z / Math.Sqrt(2.0d)));

        private static double FormulaNormSPdf(double z) =>
            Math.Exp(-0.5d * z * z) / Math.Sqrt(2.0d * Math.PI);

        private static double FormulaNormSInv(double probability)
        {
            if (probability == 0.5d)
                return 0.0d;

            const double plow = 0.02425d;
            const double phigh = 1.0d - plow;
            double x;

            if (probability < plow)
            {
                var q = Math.Sqrt(-2.0d * Math.Log(probability));
                x = (((((-0.007784894002430293d * q - 0.3223964580411365d) * q - 2.400758277161838d) * q - 2.549732539343734d) * q + 4.374664141464968d) * q + 2.938163982698783d) /
                    ((((0.007784695709041462d * q + 0.3224671290700398d) * q + 2.445134137142996d) * q + 3.754408661907416d) * q + 1.0d);
            }
            else if (probability <= phigh)
            {
                var q = probability - 0.5d;
                var r = q * q;
                x = (((((-39.69683028665376d * r + 220.9460984245205d) * r - 275.9285104469687d) * r + 138.3577518672690d) * r - 30.66479806614716d) * r + 2.506628277459239d) * q /
                    (((((-54.47609879822406d * r + 161.5858368580409d) * r - 155.6989798598866d) * r + 66.80131188771972d) * r - 13.28068155288572d) * r + 1.0d);
            }
            else
            {
                var q = Math.Sqrt(-2.0d * Math.Log(1.0d - probability));
                x = -(((((-0.007784894002430293d * q - 0.3223964580411365d) * q - 2.400758277161838d) * q - 2.549732539343734d) * q + 4.374664141464968d) * q + 2.938163982698783d) /
                    ((((0.007784695709041462d * q + 0.3224671290700398d) * q + 2.445134137142996d) * q + 3.754408661907416d) * q + 1.0d);
            }

            for (var i = 0; i < 2; i++)
            {
                var pdf = FormulaNormSPdf(x);
                if (pdf == 0d || !double.IsFinite(pdf))
                    break;

                x -= (FormulaNormSCdf(x) - probability) / pdf;
            }

            return x;
        }

        private bool TryEvaluateFormulaContinuousDistributionFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[function.Arguments.Count];
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                if (function.Arguments[i].Kind == ConditionalFormulaOperandKind.ReferenceRange ||
                    !TryResolveFormulaOperand(function.Arguments[i], rowOffset, colOffset, out arguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is ErrorValue error)
                {
                    value = error;
                    return true;
                }
            }

            if (arguments.Any(static argument => argument is RangeValue))
                return false;

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.BetaDist:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var betaDistX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var betaDistAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var betaDistBeta) ||
                        !TryGetFormulaDistributionBoolean(arguments[3], out var betaDistCumulative) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 5 ? arguments[4] : BlankValue.Instance,
                            0d,
                            out var betaDistLower) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 6 ? arguments[5] : BlankValue.Instance,
                            1d,
                            out var betaDistUpper))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaBetaDistScalar(betaDistX, betaDistAlpha, betaDistBeta, betaDistCumulative, betaDistLower, betaDistUpper);
                    return true;
                case ConditionalFormulaScalarFunctionKind.BetaDistCompat:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var betaDistCompatX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var betaDistCompatAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var betaDistCompatBeta) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 4 ? arguments[3] : BlankValue.Instance,
                            0d,
                            out var betaDistCompatLower) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 5 ? arguments[4] : BlankValue.Instance,
                            1d,
                            out var betaDistCompatUpper))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaBetaDistScalar(betaDistCompatX, betaDistCompatAlpha, betaDistCompatBeta, true, betaDistCompatLower, betaDistCompatUpper);
                    return true;
                case ConditionalFormulaScalarFunctionKind.BetaInv:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var betaInvProbability) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var betaInvAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var betaInvBeta) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 4 ? arguments[3] : BlankValue.Instance,
                            0d,
                            out var betaInvLower) ||
                        !TryGetFormulaDistributionOptionalNumber(
                            function.Arguments.Count >= 5 ? arguments[4] : BlankValue.Instance,
                            1d,
                            out var betaInvUpper))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaBetaInvScalar(betaInvProbability, betaInvAlpha, betaInvBeta, betaInvLower, betaInvUpper);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Gamma:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var gammaX))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaGammaScalar(gammaX);
                    return true;
                case ConditionalFormulaScalarFunctionKind.GammaDist:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var gammaDistX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var gammaDistAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var gammaDistBeta) ||
                        !TryGetFormulaDistributionBoolean(arguments[3], out var gammaDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaGammaDistScalar(gammaDistX, gammaDistAlpha, gammaDistBeta, gammaDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.GammaInv:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var gammaInvProbability) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var gammaInvAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var gammaInvBeta))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaGammaInvScalar(gammaInvProbability, gammaInvAlpha, gammaInvBeta);
                    return true;
                case ConditionalFormulaScalarFunctionKind.GammaLn:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var gammaLnX))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaGammaLnScalar(gammaLnX);
                    return true;
                case ConditionalFormulaScalarFunctionKind.LogNormDist:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var logNormDistX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var logNormDistMean) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var logNormDistStdev) ||
                        !TryGetFormulaDistributionBoolean(arguments[3], out var logNormDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaLogNormDistScalar(logNormDistX, logNormDistMean, logNormDistStdev, logNormDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.LogNormDistCompat:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var logNormDistCompatX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var logNormDistCompatMean) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var logNormDistCompatStdev))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaLogNormDistScalar(logNormDistCompatX, logNormDistCompatMean, logNormDistCompatStdev, true);
                    return true;
                case ConditionalFormulaScalarFunctionKind.LogNormInv:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var logNormInvProbability) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var logNormInvMean) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var logNormInvStdev))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaLogNormInvScalar(logNormInvProbability, logNormInvMean, logNormInvStdev);
                    return true;
                case ConditionalFormulaScalarFunctionKind.ExponDist:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var exponDistX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var exponDistLambda) ||
                        !TryGetFormulaDistributionBoolean(arguments[2], out var exponDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaExponDistScalar(exponDistX, exponDistLambda, exponDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.WeibullDist:
                    if (!TryGetFormulaDistributionNumber(arguments[0], out var weibullDistX) ||
                        !TryGetFormulaDistributionNumber(arguments[1], out var weibullDistAlpha) ||
                        !TryGetFormulaDistributionNumber(arguments[2], out var weibullDistBeta) ||
                        !TryGetFormulaDistributionBoolean(arguments[3], out var weibullDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaWeibullDistScalar(weibullDistX, weibullDistAlpha, weibullDistBeta, weibullDistCumulative);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFormulaDistributionNumber(ScalarValue value, out double number) =>
            TryGetFormulaNormalNumber(value, out number);

        private static bool TryGetFormulaDistributionBoolean(ScalarValue value, out bool boolean) =>
            TryGetFormulaNormalBoolean(value, out boolean);

        private static bool TryGetFormulaDistributionOptionalNumber(
            ScalarValue value,
            double defaultValue,
            out double number)
        {
            if (value is BlankValue)
            {
                number = defaultValue;
                return true;
            }

            return TryGetFormulaDistributionNumber(value, out number);
        }

        private bool TryEvaluateFormulaFinancialCashFlowFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            return function.Kind switch
            {
                ConditionalFormulaScalarFunctionKind.Npv => TryEvaluateFormulaNpvFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Irr => TryEvaluateFormulaIrrFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Mirr => TryEvaluateFormulaMirrFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Xnpv => TryEvaluateFormulaXnpvFunction(function, rowOffset, colOffset, out value),
                ConditionalFormulaScalarFunctionKind.Xirr => TryEvaluateFormulaXirrFunction(function, rowOffset, colOffset, out value),
                _ => false
            };
        }

        private bool TryEvaluateFormulaNpvFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFinancialScalarArgument(function.Arguments[0], rowOffset, colOffset, out var rateValue))
                return false;

            if (!TryGetFormulaFinancialScalarNumber(rateValue, out var rate, out var rateError))
            {
                value = rateError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(rate))
            {
                value = ErrorValue.Num;
                return true;
            }

            var cashFlows = new List<double>();
            for (var i = 1; i < function.Arguments.Count; i++)
            {
                if (!AppendFormulaFinancialArgumentNumbers(function.Arguments[i], rowOffset, colOffset, cashFlows, out var argumentError))
                {
                    value = argumentError ?? ErrorValue.Value;
                    return argumentError is not null;
                }
            }

            var result = 0d;
            for (var i = 0; i < cashFlows.Count; i++)
                result += cashFlows[i] / Math.Pow(1d + rate, i + 1);

            value = FormulaFinancialNumberResult(result);
            return true;
        }

        private bool TryEvaluateFormulaIrrFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFinancialArrayArgument(function.Arguments[0], rowOffset, colOffset, out var valueRange))
                return false;

            var guess = 0.1d;
            if (function.Arguments.Count > 1)
            {
                if (!TryResolveFormulaFinancialScalarArgument(function.Arguments[1], rowOffset, colOffset, out var guessValue))
                    return false;

                if (guessValue is not BlankValue &&
                    !TryGetFormulaFinancialScalarNumber(guessValue, out guess, out var guessError))
                {
                    value = guessError ?? ErrorValue.Value;
                    return true;
                }
            }

            if (!double.IsFinite(guess) || guess <= -1d)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryCollectFormulaFinancialRangeNumbers(valueRange, out var cashFlows, out var valueError))
            {
                value = valueError ?? ErrorValue.Value;
                return true;
            }

            value = FormulaIrrCashFlows(cashFlows, guess);
            return true;
        }

        private bool TryEvaluateFormulaMirrFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFinancialArrayArgument(function.Arguments[0], rowOffset, colOffset, out var valueRange) ||
                !TryResolveFormulaFinancialScalarArgument(function.Arguments[1], rowOffset, colOffset, out var financeRateValue) ||
                !TryResolveFormulaFinancialScalarArgument(function.Arguments[2], rowOffset, colOffset, out var reinvestRateValue))
            {
                return false;
            }

            if (!TryGetFormulaFinancialScalarNumber(financeRateValue, out var financeRate, out var financeRateError))
            {
                value = financeRateError ?? ErrorValue.Value;
                return true;
            }

            if (!TryGetFormulaFinancialScalarNumber(reinvestRateValue, out var reinvestRate, out var reinvestRateError))
            {
                value = reinvestRateError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(financeRate) || !double.IsFinite(reinvestRate))
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryCollectFormulaFinancialRangeNumbers(valueRange, out var cashFlows, out var valueError))
            {
                value = valueError ?? ErrorValue.Value;
                return true;
            }

            var count = cashFlows.Count;
            if (count < 2)
            {
                value = ErrorValue.DivByZero;
                return true;
            }

            var npvNeg = 0d;
            var npvPos = 0d;
            for (var i = 0; i < count; i++)
            {
                if (cashFlows[i] < 0d)
                    npvNeg += cashFlows[i] / Math.Pow(1d + financeRate, i);
                else if (cashFlows[i] > 0d)
                    npvPos += cashFlows[i] / Math.Pow(1d + reinvestRate, i);
            }

            if (npvNeg == 0d || npvPos == 0d)
            {
                value = ErrorValue.DivByZero;
                return true;
            }

            value = FormulaFinancialNumberResult(
                Math.Pow((-npvPos * Math.Pow(1d + reinvestRate, count - 1)) / npvNeg, 1.0d / (count - 1)) - 1d);
            return true;
        }

        private bool TryEvaluateFormulaXnpvFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFinancialScalarArgument(function.Arguments[0], rowOffset, colOffset, out var rateValue) ||
                !TryResolveFormulaFinancialArrayArgument(function.Arguments[1], rowOffset, colOffset, out var valueRange) ||
                !TryResolveFormulaFinancialArrayArgument(function.Arguments[2], rowOffset, colOffset, out var dateRange))
            {
                return false;
            }

            if (!TryGetFormulaFinancialScalarNumber(rateValue, out var rate, out var rateError))
            {
                value = rateError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(rate) || rate <= -1d)
            {
                value = ErrorValue.Num;
                return true;
            }

            var (valueCount, valueError) = CountFormulaFinancialRangeNumbers(valueRange);
            if (valueError is not null)
            {
                value = valueError;
                return true;
            }

            var (dateCount, dateError) = CountFormulaFinancialRangeNumbers(dateRange);
            if (dateError is not null)
            {
                value = dateError;
                return true;
            }

            if (valueCount != dateCount || valueCount == 0)
            {
                value = ErrorValue.Num;
                return true;
            }

            var valueRow = 0;
            var valueCol = 0;
            var dateRow = 0;
            var dateCol = 0;
            if (!TryReadNextFormulaFinancialRangeNumber(dateRange, ref dateRow, ref dateCol, out var firstDateSerial) ||
                !TryFormulaFinancialSerialToDate(firstDateSerial, out var firstDate))
            {
                value = ErrorValue.Num;
                return true;
            }

            dateRow = 0;
            dateCol = 0;
            var result = 0d;
            for (var i = 0; i < valueCount; i++)
            {
                if (!TryReadNextFormulaFinancialRangeNumber(valueRange, ref valueRow, ref valueCol, out var cashFlow) ||
                    !TryReadNextFormulaFinancialRangeNumber(dateRange, ref dateRow, ref dateCol, out var dateSerial) ||
                    !TryFormulaFinancialSerialToDate(dateSerial, out var date))
                {
                    value = ErrorValue.Num;
                    return true;
                }

                var yearFraction = (date - firstDate).TotalDays / 365.0d;
                result += cashFlow / Math.Pow(1d + rate, yearFraction);
            }

            value = FormulaFinancialNumberResult(result);
            return true;
        }

        private bool TryEvaluateFormulaXirrFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaFinancialArrayArgument(function.Arguments[0], rowOffset, colOffset, out var valueRange) ||
                !TryResolveFormulaFinancialArrayArgument(function.Arguments[1], rowOffset, colOffset, out var dateRange))
            {
                return false;
            }

            var guess = 0.1d;
            if (function.Arguments.Count > 2)
            {
                if (!TryResolveFormulaFinancialScalarArgument(function.Arguments[2], rowOffset, colOffset, out var guessValue))
                    return false;

                if (guessValue is not BlankValue &&
                    !TryGetFormulaFinancialScalarNumber(guessValue, out guess, out var guessError))
                {
                    value = guessError ?? ErrorValue.Value;
                    return true;
                }
            }

            if (!TryCollectFormulaFinancialRangeNumbers(valueRange, out var cashFlows, out var valueError))
            {
                value = valueError ?? ErrorValue.Value;
                return true;
            }

            if (!TryCollectFormulaFinancialRangeNumbers(dateRange, out var dates, out var dateError))
            {
                value = dateError ?? ErrorValue.Value;
                return true;
            }

            if (cashFlows.Count < 2)
            {
                value = ErrorValue.NA;
                return true;
            }

            if (cashFlows.Count != dates.Count)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!NormalizeFormulaFinancialDateSerialsToYearFractions(dates))
            {
                value = ErrorValue.Num;
                return true;
            }

            var rate = guess;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var functionValue = 0d;
                var derivative = 0d;
                for (var i = 0; i < cashFlows.Count; i++)
                {
                    var time = dates[i];
                    var denominator = Math.Pow(1d + rate, time);
                    functionValue += cashFlows[i] / denominator;
                    derivative -= time * cashFlows[i] / (denominator * (1d + rate));
                }

                if (Math.Abs(derivative) < 1E-14d)
                    break;

                var delta = functionValue / derivative;
                rate -= delta;
                if (Math.Abs(delta) < 1E-10d)
                    break;
            }

            value = FormulaFinancialNumberResult(rate);
            return true;
        }

        private bool TryResolveFormulaFinancialScalarArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var range))
                    return false;

                if (!TryGetSingleFormulaStatisticalRangeValue(range, out value))
                    value = ErrorValue.Value;

                return true;
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out value))
                return false;

            if (value is RangeValue resolvedRange &&
                !TryGetSingleFormulaStatisticalRangeValue(resolvedRange, out value))
            {
                value = ErrorValue.Value;
            }

            return true;
        }

        private bool TryResolveFormulaFinancialArrayArgument(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            out RangeValue range)
        {
            range = default!;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                return TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out range);

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var value))
                return false;

            range = value is RangeValue resolvedRange
                ? resolvedRange
                : SingleFormulaFinancialArray(value);
            return true;
        }

        private bool AppendFormulaFinancialArgumentNumbers(
            ConditionalFormulaOperand argument,
            int rowOffset,
            int colOffset,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            if (argument.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryMaterializeFormulaReferenceRange(argument, rowOffset, colOffset, out var materializedRange))
                    return false;

                return AppendFormulaFinancialRangeNumbers(materializedRange, numbers, out error);
            }

            if (argument.Kind == ConditionalFormulaOperandKind.Reference)
            {
                if (!TryResolveFormulaReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                    return false;

                return AppendFormulaFinancialValueNumber(targetSheet.GetValue(row, col), isDirectArgument: false, numbers, out error);
            }

            if (!TryResolveFormulaOperand(argument, rowOffset, colOffset, out var value))
                return false;

            return value is RangeValue resolvedRange
                ? AppendFormulaFinancialRangeNumbers(resolvedRange, numbers, out error)
                : AppendFormulaFinancialValueNumber(value, isDirectArgument: true, numbers, out error);
        }

        private static RangeValue SingleFormulaFinancialArray(ScalarValue value) =>
            new(new[,] { { value } });

        private static bool TryCollectFormulaFinancialRangeNumbers(
            RangeValue range,
            out List<double> numbers,
            out ErrorValue? error)
        {
            var (count, countError) = CountFormulaFinancialRangeNumbers(range);
            if (countError is not null)
            {
                numbers = new List<double>();
                error = countError;
                return false;
            }

            numbers = new List<double>(count);
            return AppendFormulaFinancialRangeNumbers(range, numbers, out error);
        }

        private static (int Count, ErrorValue? Error) CountFormulaFinancialRangeNumbers(RangeValue range)
        {
            var count = 0;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    var value = range.Cells[row, col];
                    if (value is ErrorValue error)
                        return (0, error);

                    if (value is NumberValue or DateTimeValue)
                        count++;
                }
            }

            return (count, null);
        }

        private static bool AppendFormulaFinancialRangeNumbers(
            RangeValue range,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    if (!AppendFormulaFinancialValueNumber(range.Cells[row, col], isDirectArgument: false, numbers, out error))
                        return false;
                }
            }

            return true;
        }

        private static bool AppendFormulaFinancialValueNumber(
            ScalarValue value,
            bool isDirectArgument,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            switch (value)
            {
                case RangeValue range:
                    return AppendFormulaFinancialRangeNumbers(range, numbers, out error);
                case ErrorValue valueError:
                    error = valueError;
                    return false;
                case NumberValue numeric:
                    numbers.Add(numeric.Value);
                    return true;
                case DateTimeValue dateTime:
                    numbers.Add(dateTime.Value);
                    return true;
                case BoolValue boolean when isDirectArgument:
                    numbers.Add(boolean.Value ? 1d : 0d);
                    return true;
                case TextValue text when isDirectArgument:
                    if (!TryParseFormulaTextScalarNumber(text.Value, out var parsed))
                    {
                        error = ErrorValue.Value;
                        return false;
                    }

                    numbers.Add(parsed);
                    return true;
                default:
                    return true;
            }
        }

        private static bool TryReadNextFormulaFinancialRangeNumber(
            RangeValue range,
            ref int row,
            ref int col,
            out double number)
        {
            for (; row < range.RowCount; row++)
            {
                for (; col < range.ColCount; col++)
                {
                    var value = range.Cells[row, col];
                    if (value is NumberValue numeric)
                    {
                        number = numeric.Value;
                        col++;
                        return true;
                    }

                    if (value is DateTimeValue dateTime)
                    {
                        number = dateTime.Value;
                        col++;
                        return true;
                    }
                }

                col = 0;
            }

            number = 0d;
            return false;
        }

        private static bool TryGetFormulaFinancialScalarNumber(
            ScalarValue value,
            out double number,
            out ErrorValue? error)
        {
            error = null;
            switch (value)
            {
                case ErrorValue valueError:
                    number = 0d;
                    error = valueError;
                    return false;
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
                case TextValue text when TryParseFormulaTextScalarNumber(text.Value, out var parsed):
                    number = parsed;
                    return true;
                default:
                    number = 0d;
                    error = ErrorValue.Value;
                    return false;
            }
        }

        private static ScalarValue FormulaIrrCashFlows(IReadOnlyList<double> cashFlows, double guess)
        {
            if (cashFlows.Count < 2)
                return ErrorValue.Num;

            var hasPositive = false;
            var hasNegative = false;
            for (var i = 0; i < cashFlows.Count; i++)
            {
                if (cashFlows[i] > 0d)
                    hasPositive = true;
                else if (cashFlows[i] < 0d)
                    hasNegative = true;
            }

            if (!hasPositive || !hasNegative)
                return ErrorValue.Num;

            var rate = guess;
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var functionValue = 0d;
                var derivative = 0d;
                for (var i = 0; i < cashFlows.Count; i++)
                {
                    var denominator = Math.Pow(1d + rate, i);
                    functionValue += cashFlows[i] / denominator;
                    if (i > 0)
                        derivative -= i * cashFlows[i] / (denominator * (1d + rate));
                }

                if (Math.Abs(functionValue) < 1E-10d)
                    break;

                if (Math.Abs(derivative) < 1E-15d)
                    return ErrorValue.Num;

                var delta = functionValue / derivative;
                rate -= delta;
                if (Math.Abs(delta) < 1E-10d)
                    break;
            }

            return FormulaFinancialNumberResult(rate);
        }

        private static bool NormalizeFormulaFinancialDateSerialsToYearFractions(List<double> serials)
        {
            if (serials.Count == 0 ||
                !TryFormulaFinancialSerialToDate(serials[0], out var firstDate))
            {
                return false;
            }

            for (var i = 0; i < serials.Count; i++)
            {
                if (!TryFormulaFinancialSerialToDate(serials[i], out var date))
                    return false;

                serials[i] = (date - firstDate).TotalDays / 365.0d;
            }

            return true;
        }

        private static bool TryFormulaFinancialSerialToDate(double serial, out DateTime date)
        {
            date = default;
            if (!double.IsFinite(serial))
                return false;

            try
            {
                date = FormulaExcelSerialToDate(serial);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static ScalarValue FormulaExponDistScalar(double x, double lambda, bool cumulative)
        {
            if (x < 0d || lambda <= 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(cumulative
                ? 1.0d - Math.Exp(-lambda * x)
                : lambda * Math.Exp(-lambda * x));
        }

        private static ScalarValue FormulaWeibullDistScalar(double x, double alpha, double beta, bool cumulative)
        {
            if (x < 0d || alpha <= 0d || beta <= 0d)
                return ErrorValue.Num;

            var scaled = x / beta;
            var powered = Math.Pow(scaled, alpha);
            return FormulaDistributionNumberResult(cumulative
                ? 1.0d - Math.Exp(-powered)
                : (alpha / beta) * Math.Pow(scaled, alpha - 1d) * Math.Exp(-powered));
        }

        private static ScalarValue FormulaGammaDistScalar(double x, double alpha, double beta, bool cumulative)
        {
            if (x < 0d || alpha <= 0d || beta <= 0d)
                return ErrorValue.Num;

            if (cumulative)
                return FormulaDistributionNumberResult(FormulaGammaInc(alpha, x / beta));

            var pdf = Math.Exp((alpha - 1d) * Math.Log(x) - x / beta - alpha * Math.Log(beta) - FormulaLogGamma(alpha));
            return FormulaDistributionNumberResult(pdf);
        }

        private static ScalarValue FormulaGammaInvScalar(double probability, double alpha, double beta)
        {
            if (probability < 0d || probability >= 1d || alpha <= 0d || beta <= 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaGammaInv(probability, alpha) * beta);
        }

        private static ScalarValue FormulaGammaLnScalar(double x)
        {
            if (x <= 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaLogGamma(x));
        }

        private static ScalarValue FormulaGammaScalar(double x)
        {
            if (x == 0d || x < 0d && x == Math.Floor(x))
                return ErrorValue.Num;

            var gamma = FormulaGammaValue(x);
            return double.IsFinite(gamma) ? FormulaDistributionNumberResult(gamma) : ErrorValue.Num;
        }

        private static ScalarValue FormulaBetaDistScalar(double x, double alpha, double beta, bool cumulative, double lower, double upper)
        {
            if (alpha <= 0d || beta <= 0d || lower >= upper)
                return ErrorValue.Num;

            if (x < lower || x > upper)
                return ErrorValue.Num;

            var t = (x - lower) / (upper - lower);
            if (cumulative)
                return FormulaDistributionNumberResult(FormulaBetaInc(alpha, beta, t));

            var logBeta = FormulaLogGamma(alpha) + FormulaLogGamma(beta) - FormulaLogGamma(alpha + beta);
            var pdf = Math.Exp((alpha - 1d) * Math.Log(t) + (beta - 1d) * Math.Log(1d - t) - logBeta) / (upper - lower);
            return FormulaDistributionNumberResult(pdf);
        }

        private static ScalarValue FormulaBetaInvScalar(double probability, double alpha, double beta, double lower, double upper)
        {
            if (probability < 0d || probability > 1d || alpha <= 0d || beta <= 0d || lower >= upper)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaBetaInv(probability, alpha, beta) * (upper - lower) + lower);
        }

        private static ScalarValue FormulaLogNormDistScalar(double x, double mean, double stdev, bool cumulative)
        {
            if (x <= 0d || stdev <= 0d)
                return ErrorValue.Num;

            var z = (Math.Log(x) - mean) / stdev;
            return FormulaDistributionNumberResult(cumulative
                ? FormulaNormSCdf(z)
                : FormulaNormSPdf(z) / (x * stdev));
        }

        private static ScalarValue FormulaLogNormInvScalar(double probability, double mean, double stdev)
        {
            if (probability <= 0d || probability >= 1d || stdev <= 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(Math.Exp(FormulaNormSInv(probability) * stdev + mean));
        }

        private bool TryEvaluateFormulaTFChiSquareDistributionFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[function.Arguments.Count];
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                if (function.Arguments[i].Kind == ConditionalFormulaOperandKind.ReferenceRange ||
                    !TryResolveFormulaOperand(function.Arguments[i], rowOffset, colOffset, out arguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is ErrorValue error)
                {
                    value = error;
                    return true;
                }
            }

            if (arguments.Any(static argument => argument is RangeValue))
                return false;

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.TDistCompat:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tDistCompatX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tDistCompatDf) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var tDistCompatTailsNumber))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    var tails = (int)Math.Truncate(tDistCompatTailsNumber);
                    value = tails switch
                    {
                        1 => FormulaTDistRtScalar(tDistCompatX, tDistCompatDf),
                        2 => FormulaTDist2TScalar(tDistCompatX, tDistCompatDf),
                        _ => ErrorValue.Num
                    };
                    return true;
                case ConditionalFormulaScalarFunctionKind.TDist:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tDistX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tDistDf) ||
                        !TryGetFormulaNormalBoolean(arguments[2], out var tDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaTDistScalar(tDistX, tDistDf, tDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.TDistRt:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tDistRtX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tDistRtDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaTDistRtScalar(tDistRtX, tDistRtDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.TDist2T:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tDist2TX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tDist2TDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaTDist2TScalar(tDist2TX, tDist2TDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.TInv:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tInvProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tInvDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaTInvScalar(tInvProbability, tInvDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.TInv2T:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var tInv2TProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var tInv2TDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaTInv2TScalar(tInv2TProbability, tInv2TDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.FDist:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var fDistX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var fDistD1) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var fDistD2) ||
                        !TryGetFormulaNormalBoolean(arguments[3], out var fDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaFDistScalar(fDistX, fDistD1, fDistD2, fDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.FDistRt:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var fDistRtX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var fDistRtD1) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var fDistRtD2))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaFDistRtScalar(fDistRtX, fDistRtD1, fDistRtD2);
                    return true;
                case ConditionalFormulaScalarFunctionKind.FInv:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var fInvProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var fInvD1) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var fInvD2))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaFInvScalar(fInvProbability, fInvD1, fInvD2);
                    return true;
                case ConditionalFormulaScalarFunctionKind.FInvRt:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var fInvRtProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var fInvRtD1) ||
                        !TryGetFormulaNormalNumber(arguments[2], out var fInvRtD2))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaFInvRtScalar(fInvRtProbability, fInvRtD1, fInvRtD2);
                    return true;
                case ConditionalFormulaScalarFunctionKind.ChiSqDist:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var chiSqDistX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var chiSqDistDf) ||
                        !TryGetFormulaNormalBoolean(arguments[2], out var chiSqDistCumulative))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaChiSqDistScalar(chiSqDistX, chiSqDistDf, chiSqDistCumulative);
                    return true;
                case ConditionalFormulaScalarFunctionKind.ChiSqDistRt:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var chiSqDistRtX) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var chiSqDistRtDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaChiSqDistRtScalar(chiSqDistRtX, chiSqDistRtDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.ChiSqInv:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var chiSqInvProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var chiSqInvDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaChiSqInvScalar(chiSqInvProbability, chiSqInvDf);
                    return true;
                case ConditionalFormulaScalarFunctionKind.ChiSqInvRt:
                    if (!TryGetFormulaNormalNumber(arguments[0], out var chiSqInvRtProbability) ||
                        !TryGetFormulaNormalNumber(arguments[1], out var chiSqInvRtDf))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    value = FormulaChiSqInvRtScalar(chiSqInvRtProbability, chiSqInvRtDf);
                    return true;
                default:
                    return false;
            }
        }

        private static ScalarValue FormulaTDistScalar(double x, double dfValue, bool cumulative)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(cumulative
                ? FormulaTCdf(x, df)
                : FormulaTPdf(x, df));
        }

        private static ScalarValue FormulaTDistRtScalar(double x, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(1.0d - FormulaTCdf(x, df));
        }

        private static ScalarValue FormulaTDist2TScalar(double x, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(2.0d * (1.0d - FormulaTCdf(x, df)));
        }

        private static ScalarValue FormulaTInvScalar(double probability, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || probability <= 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaTInv(probability, df));
        }

        private static ScalarValue FormulaTInv2TScalar(double probability, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || probability <= 0d || probability > 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaTInv(1.0d - probability / 2.0d, df));
        }

        private static ScalarValue FormulaFDistScalar(double x, double d1Value, double d2Value, bool cumulative)
        {
            var d1 = Math.Truncate(d1Value);
            var d2 = Math.Truncate(d2Value);
            if (d1 < 1d || d2 < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(cumulative
                ? FormulaFCdf(x, d1, d2)
                : FormulaFPdf(x, d1, d2));
        }

        private static ScalarValue FormulaFDistRtScalar(double x, double d1Value, double d2Value)
        {
            var d1 = Math.Truncate(d1Value);
            var d2 = Math.Truncate(d2Value);
            if (d1 < 1d || d2 < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(1.0d - FormulaFCdf(x, d1, d2));
        }

        private static ScalarValue FormulaFInvScalar(double probability, double d1Value, double d2Value)
        {
            var d1 = Math.Truncate(d1Value);
            var d2 = Math.Truncate(d2Value);
            if (d1 < 1d || d2 < 1d || probability <= 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaFInv(probability, d1, d2));
        }

        private static ScalarValue FormulaFInvRtScalar(double probability, double d1Value, double d2Value)
        {
            var d1 = Math.Truncate(d1Value);
            var d2 = Math.Truncate(d2Value);
            if (d1 < 1d || d2 < 1d || probability <= 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaFInv(1.0d - probability, d1, d2));
        }

        private static ScalarValue FormulaChiSqDistScalar(double x, double dfValue, bool cumulative)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(cumulative
                ? FormulaChiSqCdf(x, df)
                : FormulaChiSqPdf(x, df));
        }

        private static ScalarValue FormulaChiSqDistRtScalar(double x, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || x < 0d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(1.0d - FormulaChiSqCdf(x, df));
        }

        private static ScalarValue FormulaChiSqInvScalar(double probability, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || probability < 0d || probability >= 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaChiSqInv(probability, df));
        }

        private static ScalarValue FormulaChiSqInvRtScalar(double probability, double dfValue)
        {
            var df = Math.Truncate(dfValue);
            if (df < 1d || probability <= 0d || probability > 1d)
                return ErrorValue.Num;

            return FormulaDistributionNumberResult(FormulaChiSqInv(1.0d - probability, df));
        }

        private static double FormulaTCdf(double t, double df)
        {
            var x = df / (df + t * t);
            var tail = 0.5d * FormulaBetaInc(df / 2.0d, 0.5d, x);
            return t >= 0d ? 1.0d - tail : tail;
        }

        private static double FormulaTPdf(double t, double df) =>
            Math.Exp(FormulaLogGamma((df + 1.0d) / 2.0d) - FormulaLogGamma(df / 2.0d)) /
            (Math.Sqrt(df * Math.PI) * Math.Pow(1.0d + t * t / df, (df + 1.0d) / 2.0d));

        private static double FormulaTInv(double probability, double df)
        {
            var lo = -1e9d;
            var hi = 1e9d;
            for (var i = 0; i < 300; i++)
            {
                var mid = (lo + hi) / 2.0d;
                if (FormulaTCdf(mid, df) < probability)
                    lo = mid;
                else
                    hi = mid;
                if (hi - lo < 1e-10d)
                    break;
            }

            return (lo + hi) / 2.0d;
        }

        private static double FormulaFCdf(double x, double d1, double d2)
        {
            if (x <= 0d)
                return 0d;

            var t = d1 * x / (d1 * x + d2);
            return FormulaBetaInc(d1 / 2.0d, d2 / 2.0d, t);
        }

        private static double FormulaFPdf(double x, double d1, double d2)
        {
            if (x <= 0d)
                return 0d;

            var lbeta = FormulaLogGamma(d1 / 2.0d) + FormulaLogGamma(d2 / 2.0d) - FormulaLogGamma((d1 + d2) / 2.0d);
            return Math.Exp(
                (d1 / 2.0d) * Math.Log(d1) +
                (d2 / 2.0d) * Math.Log(d2) +
                (d1 / 2.0d - 1.0d) * Math.Log(x) -
                ((d1 + d2) / 2.0d) * Math.Log(d1 * x + d2) -
                lbeta);
        }

        private static double FormulaFInv(double probability, double d1, double d2)
        {
            var lo = 0d;
            var hi = 1e9d;
            for (var i = 0; i < 300; i++)
            {
                var mid = (lo + hi) / 2.0d;
                if (FormulaFCdf(mid, d1, d2) < probability)
                    lo = mid;
                else
                    hi = mid;
                if (hi - lo < 1e-9d)
                    break;
            }

            return (lo + hi) / 2.0d;
        }

        private static double FormulaChiSqCdf(double x, double df) =>
            x <= 0d ? 0d : FormulaGammaInc(df / 2.0d, x / 2.0d);

        private static double FormulaChiSqPdf(double x, double df)
        {
            if (x <= 0d)
                return 0d;

            return Math.Exp(
                (df / 2.0d - 1.0d) * Math.Log(x) -
                x / 2.0d -
                (df / 2.0d) * Math.Log(2d) -
                FormulaLogGamma(df / 2.0d));
        }

        private static double FormulaChiSqInv(double probability, double df) =>
            2.0d * FormulaGammaInv(probability, df / 2.0d);

        private static ScalarValue FormulaDistributionNumberResult(double result) =>
            double.IsFinite(result) ? new NumberValue(result) : ErrorValue.Num;

        private static double FormulaLogGamma(double x)
        {
            ReadOnlySpan<double> coefficients =
            [
                76.18009172947146d,
                -86.50532032941677d,
                24.01409824083091d,
                -1.231739572450155d,
                0.1208650973866179e-2d,
                -0.5395239384953e-5d
            ];

            var y = x;
            var tmp = x + 5.5d;
            tmp -= (x + 0.5d) * Math.Log(tmp);
            var ser = 1.000000000190015d;
            for (var j = 0; j < coefficients.Length; j++)
                ser += coefficients[j] / ++y;

            return -tmp + Math.Log(2.5066282746310005d * ser / x);
        }

        private static double FormulaGammaValue(double x)
        {
            if (x <= 0d)
            {
                if (x == Math.Floor(x))
                    return double.NaN;

                return Math.PI / (Math.Sin(Math.PI * x) * FormulaGammaValue(1.0d - x));
            }

            return Math.Exp(FormulaLogGamma(x));
        }

        private static double FormulaGammaInc(double a, double x)
        {
            if (x < 0d || a <= 0d)
                return double.NaN;

            if (x == 0d)
                return 0d;

            return x < a + 1.0d
                ? FormulaGammaIncSeries(a, x)
                : 1.0d - FormulaGammaIncCf(a, x);
        }

        private static double FormulaGammaIncSeries(double a, double x)
        {
            var ap = a;
            var delta = 1.0d / a;
            var sum = delta;
            for (var n = 1; n <= 300; n++)
            {
                ap++;
                delta *= x / ap;
                sum += delta;
                if (Math.Abs(delta) < Math.Abs(sum) * 1e-12d)
                    break;
            }

            return sum * Math.Exp(-x + a * Math.Log(x) - FormulaLogGamma(a));
        }

        private static double FormulaGammaIncCf(double a, double x)
        {
            var b = x + 1.0d - a;
            var c = 1.0d / 1e-30d;
            var d = 1.0d / b;
            var h = d;
            if (Math.Abs(d) < 1e-30d)
                d = 1e-30d;

            for (var i = 1; i <= 300; i++)
            {
                var an = -i * (i - a);
                b += 2.0d;
                d = an * d + b;
                if (Math.Abs(d) < 1e-30d)
                    d = 1e-30d;

                c = b + an / c;
                if (Math.Abs(c) < 1e-30d)
                    c = 1e-30d;

                d = 1.0d / d;
                var delta = d * c;
                h *= delta;
                if (Math.Abs(delta - 1.0d) < 1e-12d)
                    break;
            }

            return Math.Exp(-x + a * Math.Log(x) - FormulaLogGamma(a)) * h;
        }

        private static double FormulaGammaInv(double probability, double alpha)
        {
            if (probability <= 0d)
                return 0d;

            if (probability >= 1d)
                return double.PositiveInfinity;

            var x = alpha * Math.Pow(FormulaNormSInv(probability) / Math.Sqrt(9d * alpha) + 1d - 1.0d / (9d * alpha), 3d);
            if (x <= 0d)
                x = 0.01d;

            for (var i = 0; i < 200; i++)
            {
                var f = FormulaGammaInc(alpha, x) - probability;
                var df = Math.Exp((alpha - 1d) * Math.Log(x) - x - FormulaLogGamma(alpha));
                if (df == 0d)
                    break;

                var dx = f / df;
                x -= dx;
                if (x <= 0d)
                    x = 1e-10d;

                if (Math.Abs(dx) < x * 1e-10d)
                    break;
            }

            return x;
        }

        private static double FormulaBetaInc(double a, double b, double x)
        {
            if (x < 0d || x > 1d)
                return double.NaN;

            if (x == 0d)
                return 0d;

            if (x == 1d)
                return 1d;

            if (x > (a + 1d) / (a + b + 2d))
                return 1.0d - FormulaBetaInc(b, a, 1.0d - x);

            var logBeta = FormulaLogGamma(a) + FormulaLogGamma(b) - FormulaLogGamma(a + b);
            var front = Math.Exp(Math.Log(x) * a + Math.Log(1d - x) * b - logBeta) / a;
            return front * FormulaBetaCf(a, b, x);
        }

        private static double FormulaBetaCf(double a, double b, double x)
        {
            const int maxIterations = 300;
            const double epsilon = 3e-12d;

            var qab = a + b;
            var qap = a + 1d;
            var qam = a - 1d;
            var c = 1d;
            var d = 1d - qab * x / qap;
            if (Math.Abs(d) < 1e-30d)
                d = 1e-30d;

            d = 1d / d;
            var h = d;
            for (var m = 1; m <= maxIterations; m++)
            {
                var m2 = 2 * m;
                var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1d + aa * d;
                if (Math.Abs(d) < 1e-30d)
                    d = 1e-30d;

                c = 1d + aa / c;
                if (Math.Abs(c) < 1e-30d)
                    c = 1e-30d;

                d = 1d / d;
                h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1d + aa * d;
                if (Math.Abs(d) < 1e-30d)
                    d = 1e-30d;

                c = 1d + aa / c;
                if (Math.Abs(c) < 1e-30d)
                    c = 1e-30d;

                d = 1d / d;
                var delta = d * c;
                h *= delta;
                if (Math.Abs(delta - 1d) < epsilon)
                    break;
            }

            return h;
        }

        private static double FormulaBetaInv(double probability, double alpha, double beta)
        {
            if (probability <= 0d)
                return 0d;

            if (probability >= 1d)
                return 1d;

            var x = alpha / (alpha + beta);
            for (var i = 0; i < 200; i++)
            {
                var f = FormulaBetaInc(alpha, beta, x) - probability;
                var logBeta = FormulaLogGamma(alpha) + FormulaLogGamma(beta) - FormulaLogGamma(alpha + beta);
                var df = Math.Exp((alpha - 1d) * Math.Log(x) + (beta - 1d) * Math.Log(1d - x) - logBeta);
                if (df == 0d)
                    break;

                var dx = f / df;
                x -= dx;
                x = Math.Clamp(x, 1e-10d, 1.0d - 1e-10d);
                if (Math.Abs(dx) < 1e-10d)
                    break;
            }

            return x;
        }

        private bool TryEvaluateFormulaFinancialScalarFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            var arguments = new ScalarValue[function.Arguments.Count];
            for (var i = 0; i < function.Arguments.Count; i++)
            {
                if (!TryResolveFormulaOperand(function.Arguments[i], rowOffset, colOffset, out var argument) ||
                    argument is RangeValue)
                {
                    return false;
                }

                if (argument is ErrorValue error)
                {
                    value = error;
                    return true;
                }

                arguments[i] = argument;
            }

            switch (function.Kind)
            {
                case ConditionalFormulaScalarFunctionKind.Disc:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var discSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var discMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var discPrice, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var discRedemption, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 4, out var discBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialDiscScalar(discSettlement, discMaturity, discPrice, discRedemption, discBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Intrate:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var intrateSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var intrateMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var intrateInvestment, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var intrateRedemption, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 4, out var intrateBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialIntrateScalar(intrateSettlement, intrateMaturity, intrateInvestment, intrateRedemption, intrateBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Received:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var receivedSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var receivedMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var receivedInvestment, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var receivedDiscount, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 4, out var receivedBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialReceivedScalar(receivedSettlement, receivedMaturity, receivedInvestment, receivedDiscount, receivedBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Pricedisc:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var pricediscSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var pricediscMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var pricediscDiscount, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var pricediscRedemption, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 4, out var pricediscBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialPricediscScalar(pricediscSettlement, pricediscMaturity, pricediscDiscount, pricediscRedemption, pricediscBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Pricemat:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var pricematSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var pricematMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var pricematIssue, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var pricematRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var pricematYield, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 5, out var pricematBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialPricematScalar(pricematSettlement, pricematMaturity, pricematIssue, pricematRate, pricematYield, pricematBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Tbilleq:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var tbilleqSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var tbilleqMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var tbilleqDiscount, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialTbilleqScalar(tbilleqSettlement, tbilleqMaturity, tbilleqDiscount);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Tbillprice:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var tbillpriceSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var tbillpriceMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var tbillpriceDiscount, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialTbillpriceScalar(tbillpriceSettlement, tbillpriceMaturity, tbillpriceDiscount);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Tbillyield:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var tbillyieldSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var tbillyieldMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var tbillyieldPrice, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialTbillyieldScalar(tbillyieldSettlement, tbillyieldMaturity, tbillyieldPrice);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Sln:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var slnCost, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var slnSalvage, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var slnLife, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialSlnScalar(slnCost, slnSalvage, slnLife);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Syd:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var sydCost, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var sydSalvage, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var sydLife, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var sydPeriod, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialSydScalar(sydCost, sydSalvage, sydLife, sydPeriod);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Db:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var dbCost, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var dbSalvage, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var dbLife, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var dbPeriod, out value) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 12d, out var dbMonth, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialDbScalar(dbCost, dbSalvage, dbLife, dbPeriod, dbMonth);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Ddb:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var ddbCost, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var ddbSalvage, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var ddbLife, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var ddbPeriod, out value) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 4, 2d, out var ddbFactor, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialDdbScalar(ddbCost, ddbSalvage, ddbLife, ddbPeriod, ddbFactor);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Vdb:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var vdbCost, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var vdbSalvage, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var vdbLife, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var vdbStartPeriod, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var vdbEndPeriod, out value) ||
                        !TryGetFormulaFinancialOptionalNumber(arguments, 5, 2d, out var vdbFactor, out value) ||
                        !TryGetFormulaFinancialOptionalBool(arguments, 6, defaultValue: false, out var vdbNoSwitch, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialVdbScalar(vdbCost, vdbSalvage, vdbLife, vdbStartPeriod, vdbEndPeriod, vdbFactor, vdbNoSwitch);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Effect:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var effectRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var effectNpery, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialEffectScalar(effectRate, effectNpery);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Nominal:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var nominalRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var nominalNpery, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialNominalScalar(nominalRate, nominalNpery);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Rri:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var rriNper, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var rriPv, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var rriFv, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialRriScalar(rriNper, rriPv, rriFv);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Pduration:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var pdurationRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var pdurationPv, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var pdurationFv, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialPdurationScalar(pdurationRate, pdurationPv, pdurationFv);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Duration:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var durationSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var durationMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var durationCoupon, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var durationYield, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 4, 5, out var durationFrequency, out var durationBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialDurationScalar(durationSettlement, durationMaturity, durationCoupon, durationYield, durationFrequency, durationBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Mduration:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var mdurationSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var mdurationMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var mdurationCoupon, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var mdurationYield, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 4, 5, out var mdurationFrequency, out var mdurationBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialMdurationScalar(mdurationSettlement, mdurationMaturity, mdurationCoupon, mdurationYield, mdurationFrequency, mdurationBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Price:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var priceSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var priceMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var priceRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var priceYield, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var priceRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 5, 6, out var priceFrequency, out var priceBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialPriceScalar(priceSettlement, priceMaturity, priceRate, priceYield, priceRedemption, priceFrequency, priceBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Yield:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var yieldSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var yieldMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var yieldRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var yieldPrice, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var yieldRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 5, 6, out var yieldFrequency, out var yieldBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialYieldScalar(yieldSettlement, yieldMaturity, yieldRate, yieldPrice, yieldRedemption, yieldFrequency, yieldBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Yielddisc:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var yielddiscSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var yielddiscMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var yielddiscPrice, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var yielddiscRedemption, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 4, out var yielddiscBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialYielddiscScalar(yielddiscSettlement, yielddiscMaturity, yielddiscPrice, yielddiscRedemption, yielddiscBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Yieldmat:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var yieldmatSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var yieldmatMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var yieldmatIssue, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var yieldmatRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var yieldmatPrice, out value) ||
                        !TryGetFormulaFinancialOptionalBasis(arguments, 5, out var yieldmatBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialYieldmatScalar(yieldmatSettlement, yieldmatMaturity, yieldmatIssue, yieldmatRate, yieldmatPrice, yieldmatBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Oddfprice:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var oddfpriceSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var oddfpriceMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var oddfpriceIssue, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var oddfpriceFirstCoupon, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var oddfpriceRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[5], out var oddfpriceYield, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[6], out var oddfpriceRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 7, 8, out var oddfpriceFrequency, out var oddfpriceBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialOddfpriceScalar(
                        oddfpriceSettlement,
                        oddfpriceMaturity,
                        oddfpriceIssue,
                        oddfpriceFirstCoupon,
                        oddfpriceRate,
                        oddfpriceYield,
                        oddfpriceRedemption,
                        oddfpriceFrequency,
                        oddfpriceBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Oddfyield:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var oddfyieldSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var oddfyieldMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var oddfyieldIssue, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var oddfyieldFirstCoupon, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var oddfyieldRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[5], out var oddfyieldPrice, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[6], out var oddfyieldRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 7, 8, out var oddfyieldFrequency, out var oddfyieldBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialOddfyieldScalar(
                        oddfyieldSettlement,
                        oddfyieldMaturity,
                        oddfyieldIssue,
                        oddfyieldFirstCoupon,
                        oddfyieldRate,
                        oddfyieldPrice,
                        oddfyieldRedemption,
                        oddfyieldFrequency,
                        oddfyieldBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Oddlprice:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var oddlpriceSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var oddlpriceMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var oddlpriceLastInterest, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var oddlpriceRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var oddlpriceYield, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[5], out var oddlpriceRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 6, 7, out var oddlpriceFrequency, out var oddlpriceBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialOddlpriceScalar(
                        oddlpriceSettlement,
                        oddlpriceMaturity,
                        oddlpriceLastInterest,
                        oddlpriceRate,
                        oddlpriceYield,
                        oddlpriceRedemption,
                        oddlpriceFrequency,
                        oddlpriceBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Oddlyield:
                    if (!TryGetFormulaFinancialNumber(arguments[0], out var oddlyieldSettlement, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[1], out var oddlyieldMaturity, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[2], out var oddlyieldLastInterest, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[3], out var oddlyieldRate, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[4], out var oddlyieldPrice, out value) ||
                        !TryGetFormulaFinancialNumber(arguments[5], out var oddlyieldRedemption, out value) ||
                        !TryGetFormulaFinancialBondFrequencyAndBasis(arguments, 6, 7, out var oddlyieldFrequency, out var oddlyieldBasis, out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialOddlyieldScalar(
                        oddlyieldSettlement,
                        oddlyieldMaturity,
                        oddlyieldLastInterest,
                        oddlyieldRate,
                        oddlyieldPrice,
                        oddlyieldRedemption,
                        oddlyieldFrequency,
                        oddlyieldBasis);
                    return true;
                case ConditionalFormulaScalarFunctionKind.Coupdaybs:
                case ConditionalFormulaScalarFunctionKind.Coupdays:
                case ConditionalFormulaScalarFunctionKind.Coupdaysnc:
                case ConditionalFormulaScalarFunctionKind.Coupncd:
                case ConditionalFormulaScalarFunctionKind.Coupnum:
                case ConditionalFormulaScalarFunctionKind.Couppcd:
                    if (!TryGetFormulaFinancialCouponArguments(
                            arguments,
                            out var couponSettlement,
                            out var couponMaturity,
                            out var couponFrequency,
                            out var couponBasis,
                            out value))
                    {
                        return true;
                    }

                    value = FormulaFinancialCouponScalar(function.Kind, couponSettlement, couponMaturity, couponFrequency, couponBasis);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetFormulaFinancialNumber(
            ScalarValue source,
            out double number,
            out ScalarValue error)
        {
            error = ErrorValue.Value;
            switch (source)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    break;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    break;
                case BoolValue boolean:
                    number = boolean.Value ? 1d : 0d;
                    break;
                case BlankValue:
                    number = 0d;
                    break;
                case TextValue text when TryParseFormulaValueText(text.Value, out var parsed):
                    number = parsed;
                    break;
                case ErrorValue sourceError:
                    number = 0d;
                    error = sourceError;
                    return false;
                default:
                    number = 0d;
                    return false;
            }

            if (double.IsFinite(number))
                return true;

            error = ErrorValue.Num;
            return false;
        }

        private static bool TryGetFormulaFinancialOptionalNumber(
            IReadOnlyList<ScalarValue> arguments,
            int index,
            double defaultValue,
            out double number,
            out ScalarValue error)
        {
            if (index >= arguments.Count || arguments[index] is BlankValue)
            {
                number = defaultValue;
                error = ErrorValue.Value;
                return true;
            }

            return TryGetFormulaFinancialNumber(arguments[index], out number, out error);
        }

        private static bool TryGetFormulaFinancialOptionalBool(
            IReadOnlyList<ScalarValue> arguments,
            int index,
            bool defaultValue,
            out bool boolean,
            out ScalarValue error)
        {
            if (index >= arguments.Count || arguments[index] is BlankValue)
            {
                boolean = defaultValue;
                error = ErrorValue.Value;
                return true;
            }

            error = ErrorValue.Value;
            switch (arguments[index])
            {
                case BoolValue logical:
                    boolean = logical.Value;
                    return true;
                case NumberValue numeric when double.IsFinite(numeric.Value):
                    boolean = numeric.Value != 0d;
                    return true;
                case DateTimeValue dateTime when double.IsFinite(dateTime.Value):
                    boolean = dateTime.Value != 0d;
                    return true;
                case ErrorValue sourceError:
                    boolean = false;
                    error = sourceError;
                    return false;
                default:
                    boolean = false;
                    return false;
            }
        }

        private static ScalarValue FormulaFinancialSlnScalar(double cost, double salvage, double life)
        {
            if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life))
                return ErrorValue.Num;

            return life == 0d
                ? ErrorValue.DivByZero
                : FormulaFinancialNumberResult((cost - salvage) / life);
        }

        private static ScalarValue FormulaFinancialSydScalar(double cost, double salvage, double life, double period)
        {
            if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) || !double.IsFinite(period))
                return ErrorValue.Num;

            if (life <= 0d || period <= 0d || period > life)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((cost - salvage) * (life - period + 1d) / (life * (life + 1d) / 2d));
        }

        private static ScalarValue FormulaFinancialDbScalar(double cost, double salvage, double life, double period, double month)
        {
            if (!TryGetFormulaFinancialInteger(life, out var integerLife) ||
                !TryGetFormulaFinancialInteger(period, out var integerPeriod) ||
                !TryGetFormulaFinancialInteger(month, out var integerMonth))
            {
                return ErrorValue.Num;
            }

            if (cost <= 0d ||
                salvage < 0d ||
                integerLife <= 0 ||
                integerPeriod <= 0 ||
                integerPeriod > integerLife + 1 ||
                integerMonth is < 1 or > 12 ||
                integerPeriod > MaxFormulaFinancialDepreciationIterations)
            {
                return ErrorValue.Num;
            }

            if (salvage >= cost)
                return new NumberValue(0d);

            var rate = Math.Round(1d - Math.Pow(salvage / cost, 1d / integerLife), 3);
            var accumulated = 0d;
            var depreciation = 0d;
            for (var currentPeriod = 1; currentPeriod <= integerPeriod; currentPeriod++)
            {
                if (currentPeriod == 1)
                    depreciation = cost * rate * integerMonth / 12d;
                else if (currentPeriod <= integerLife)
                    depreciation = (cost - accumulated) * rate;
                else
                    depreciation = (cost - accumulated) * rate * (12d - integerMonth + 1d) / 12d;

                if (currentPeriod < integerPeriod)
                    accumulated += depreciation;
            }

            return FormulaFinancialNumberResult(depreciation);
        }

        private static ScalarValue FormulaFinancialDdbScalar(double cost, double salvage, double life, double period, double factor)
        {
            if (!double.IsFinite(cost) ||
                !double.IsFinite(salvage) ||
                !double.IsFinite(life) ||
                !double.IsFinite(factor) ||
                !TryGetFormulaFinancialInteger(period, out var integerPeriod))
            {
                return ErrorValue.Num;
            }

            if (cost < 0d ||
                salvage < 0d ||
                life <= 0d ||
                period <= 0d ||
                factor <= 0d ||
                integerPeriod > MaxFormulaFinancialDepreciationIterations)
            {
                return ErrorValue.Num;
            }

            var bookValue = cost;
            for (var currentPeriod = 1; currentPeriod <= integerPeriod; currentPeriod++)
            {
                var depreciation = Math.Min(bookValue - salvage, bookValue * factor / life);
                depreciation = Math.Max(depreciation, 0d);
                if (currentPeriod < integerPeriod)
                    bookValue -= depreciation;
                else
                    return FormulaFinancialNumberResult(depreciation);
            }

            return new NumberValue(0d);
        }

        private static ScalarValue FormulaFinancialVdbScalar(
            double cost,
            double salvage,
            double life,
            double startPeriod,
            double endPeriod,
            double factor,
            bool noSwitch)
        {
            if (!double.IsFinite(cost) ||
                !double.IsFinite(salvage) ||
                !double.IsFinite(life) ||
                !double.IsFinite(startPeriod) ||
                !double.IsFinite(endPeriod) ||
                !double.IsFinite(factor))
            {
                return ErrorValue.Num;
            }

            if (cost < 0d ||
                salvage < 0d ||
                life <= 0d ||
                startPeriod < 0d ||
                endPeriod < startPeriod ||
                endPeriod > life ||
                factor <= 0d)
            {
                return ErrorValue.Num;
            }

            var totalDepreciation = 0d;
            var bookValue = cost;
            var currentPeriod = startPeriod;
            var iterations = 0;
            while (currentPeriod < endPeriod)
            {
                if (++iterations > MaxFormulaFinancialDepreciationIterations)
                    return ErrorValue.Num;

                var periodEnd = Math.Min(Math.Ceiling(currentPeriod + 1e-10d), endPeriod);
                if (periodEnd <= currentPeriod)
                    return ErrorValue.Num;

                var fraction = periodEnd - currentPeriod;
                var period = Math.Floor(currentPeriod + 1e-10d);
                var ddbDepreciation = bookValue * factor / life;
                var straightLineDepreciation = (bookValue - salvage) / (life - period);
                var depreciation = !noSwitch && straightLineDepreciation > ddbDepreciation
                    ? straightLineDepreciation
                    : ddbDepreciation;
                depreciation = Math.Max(0d, Math.Min(depreciation, bookValue - salvage));

                var partialDepreciation = depreciation * fraction;
                totalDepreciation += partialDepreciation;
                bookValue -= partialDepreciation;
                currentPeriod = periodEnd;
            }

            return FormulaFinancialNumberResult(totalDepreciation);
        }

        private static ScalarValue FormulaFinancialEffectScalar(double nominalRate, double npery)
        {
            npery = Math.Truncate(npery);
            if (!double.IsFinite(nominalRate) || !double.IsFinite(npery) || nominalRate <= 0d || npery < 1d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(Math.Pow(1d + nominalRate / npery, npery) - 1d);
        }

        private static ScalarValue FormulaFinancialNominalScalar(double effectiveRate, double npery)
        {
            npery = Math.Truncate(npery);
            if (!double.IsFinite(effectiveRate) || !double.IsFinite(npery) || effectiveRate <= 0d || npery < 1d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((Math.Pow(1d + effectiveRate, 1d / npery) - 1d) * npery);
        }

        private static ScalarValue FormulaFinancialRriScalar(double nper, double pv, double fv)
        {
            if (!double.IsFinite(nper) || !double.IsFinite(pv) || !double.IsFinite(fv))
                return ErrorValue.Num;

            if (nper <= 0d || pv == 0d || pv > 0d && fv < 0d || pv < 0d && fv > 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(Math.Pow(fv / pv, 1d / nper) - 1d);
        }

        private static ScalarValue FormulaFinancialPdurationScalar(double rate, double pv, double fv)
        {
            if (!double.IsFinite(rate) || !double.IsFinite(pv) || !double.IsFinite(fv))
                return ErrorValue.Num;

            if (rate <= 0d || pv <= 0d || fv <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((Math.Log(fv) - Math.Log(pv)) / Math.Log(1d + rate));
        }

        private static ScalarValue FormulaFinancialDurationScalar(
            double settlement,
            double maturity,
            double coupon,
            double yield,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(coupon) ||
                !double.IsFinite(yield))
            {
                return ErrorValue.Num;
            }

            if (coupon < 0d ||
                yield < 0d ||
                !TryValidateFormulaFinancialBondSchedule(settlement, maturity, frequency, basis, out var settlementDate, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            if (!TryGetFormulaFinancialCouponSchedule(
                    settlementDate,
                    maturityDate,
                    frequency,
                    out var previousCouponDate,
                    out var nextCouponDate,
                    out var couponCount))
            {
                return ErrorValue.Num;
            }

            var daysInPeriod = (nextCouponDate - previousCouponDate).TotalDays;
            if (daysInPeriod <= 0d)
                return ErrorValue.Num;

            var fractionalPeriodsToNextCoupon = (nextCouponDate - settlementDate).TotalDays / daysInPeriod;
            var couponPayment = coupon / frequency * 100d;
            var yieldPerPeriod = yield / frequency;
            var price = 0d;
            var weightedTime = 0d;
            var currentCouponDate = nextCouponDate;
            var months = 12 / frequency;

            for (var index = 0; index < couponCount; index++)
            {
                var periodsFromSettlement = index + fractionalPeriodsToNextCoupon;
                var cashFlow = couponPayment;
                if (currentCouponDate == maturityDate)
                    cashFlow += 100d;

                var presentValue = cashFlow / Math.Pow(1d + yieldPerPeriod, periodsFromSettlement);
                price += presentValue;
                weightedTime += periodsFromSettlement / frequency * presentValue;

                try
                {
                    currentCouponDate = currentCouponDate.AddMonths(months);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return ErrorValue.Num;
                }
            }

            if (Math.Abs(price) < 1E-14d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(weightedTime / price);
        }

        private static ScalarValue FormulaFinancialMdurationScalar(
            double settlement,
            double maturity,
            double coupon,
            double yield,
            int frequency,
            int basis)
        {
            var duration = FormulaFinancialDurationScalar(settlement, maturity, coupon, yield, frequency, basis);
            if (duration is not NumberValue durationNumber)
                return duration;

            return FormulaFinancialNumberResult(durationNumber.Value / (1d + yield / frequency));
        }

        private static ScalarValue FormulaFinancialPriceScalar(
            double settlement,
            double maturity,
            double rate,
            double yield,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(yield) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                yield < 0d ||
                redemption <= 0d ||
                !TryValidateFormulaFinancialBondSchedule(settlement, maturity, frequency, basis, out var settlementDate, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            return TryCalculateFormulaFinancialBondPrice(
                    settlementDate,
                    maturityDate,
                    rate,
                    yield,
                    redemption,
                    frequency,
                    out var price)
                ? FormulaFinancialNumberResult(price)
                : ErrorValue.Num;
        }

        private static ScalarValue FormulaFinancialYieldScalar(
            double settlement,
            double maturity,
            double rate,
            double price,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(price) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                price <= 0d ||
                redemption <= 0d ||
                !TryValidateFormulaFinancialBondSchedule(settlement, maturity, frequency, basis, out var settlementDate, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            var yield = 0.1d;
            for (var iteration = 0; iteration < MaxFormulaFinancialBondYieldIterations; iteration++)
            {
                if (!TryCalculateFormulaFinancialBondPrice(
                        settlementDate,
                        maturityDate,
                        rate,
                        yield,
                        redemption,
                        frequency,
                        out var calculatedPrice) ||
                    !TryCalculateFormulaFinancialBondPrice(
                        settlementDate,
                        maturityDate,
                        rate,
                        yield + 1E-6d,
                        redemption,
                        frequency,
                        out var shiftedPrice))
                {
                    return ErrorValue.Num;
                }

                var derivative = (shiftedPrice - calculatedPrice) / 1E-6d;
                if (!double.IsFinite(derivative))
                    return ErrorValue.Num;

                if (Math.Abs(derivative) < 1E-14d)
                    break;

                var delta = (calculatedPrice - price) / derivative;
                if (!double.IsFinite(delta))
                    return ErrorValue.Num;

                yield -= delta;
                if (yield < -0.999d)
                    yield = -0.999d;

                if (Math.Abs(delta) < 1E-10d)
                    break;
            }

            return FormulaFinancialNumberResult(yield);
        }

        private static ScalarValue FormulaFinancialYielddiscScalar(
            double settlement,
            double maturity,
            double price,
            double redemption,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(price) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (price <= 0d ||
                redemption <= 0d ||
                !TryGetFormulaFinancialBondDates(settlement, maturity, out var settlementDate, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            var dayCountFraction = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            if (!double.IsFinite(dayCountFraction) || dayCountFraction <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((redemption / price - 1d) / dayCountFraction);
        }

        private static ScalarValue FormulaFinancialYieldmatScalar(
            double settlement,
            double maturity,
            double issue,
            double rate,
            double price,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(issue) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(price))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                price <= 0d ||
                !TryGetFormulaFinancialBondDates(settlement, maturity, out var settlementDate, out var maturityDate) ||
                !TryGetFormulaFinancialCouponDate(issue, out var issueDate))
            {
                return ErrorValue.Num;
            }

            var daysIssueToMaturity = FormulaFinancialDayCountFraction(issueDate, maturityDate, basis);
            var daysSettlementToMaturity = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            if (!double.IsFinite(daysIssueToMaturity) ||
                !double.IsFinite(daysSettlementToMaturity) ||
                daysSettlementToMaturity <= 0d)
            {
                return ErrorValue.Num;
            }

            var numerator = (1d + rate * daysIssueToMaturity) / (price / 100d) - 1d;
            return FormulaFinancialNumberResult(numerator / daysSettlementToMaturity);
        }

        private static ScalarValue FormulaFinancialOddfpriceScalar(
            double settlement,
            double maturity,
            double issue,
            double firstCoupon,
            double rate,
            double yield,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(issue) ||
                !double.IsFinite(firstCoupon) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(yield) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                yield < 0d ||
                redemption <= 0d ||
                !TryGetFormulaFinancialOddFirstCouponDates(
                    settlement,
                    maturity,
                    issue,
                    firstCoupon,
                    frequency,
                    basis,
                    out var settlementDate,
                    out var maturityDate,
                    out var issueDate,
                    out var firstCouponDate))
            {
                return ErrorValue.Num;
            }

            return TryCalculateFormulaFinancialOddFirstPrice(
                    issueDate,
                    settlementDate,
                    maturityDate,
                    firstCouponDate,
                    rate,
                    yield,
                    redemption,
                    frequency,
                    basis,
                    out var price)
                ? FormulaFinancialNumberResult(price)
                : ErrorValue.Num;
        }

        private static ScalarValue FormulaFinancialOddfyieldScalar(
            double settlement,
            double maturity,
            double issue,
            double firstCoupon,
            double rate,
            double price,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(issue) ||
                !double.IsFinite(firstCoupon) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(price) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                price <= 0d ||
                redemption <= 0d ||
                !TryGetFormulaFinancialOddFirstCouponDates(
                    settlement,
                    maturity,
                    issue,
                    firstCoupon,
                    frequency,
                    basis,
                    out var settlementDate,
                    out var maturityDate,
                    out var issueDate,
                    out var firstCouponDate))
            {
                return ErrorValue.Num;
            }

            var yield = 0.1d;
            for (var iteration = 0; iteration < MaxFormulaFinancialBondYieldIterations; iteration++)
            {
                if (!TryCalculateFormulaFinancialOddFirstPrice(
                        issueDate,
                        settlementDate,
                        maturityDate,
                        firstCouponDate,
                        rate,
                        yield,
                        redemption,
                        frequency,
                        basis,
                        out var calculatedPrice) ||
                    !TryCalculateFormulaFinancialOddFirstPrice(
                        issueDate,
                        settlementDate,
                        maturityDate,
                        firstCouponDate,
                        rate,
                        yield + 1E-6d,
                        redemption,
                        frequency,
                        basis,
                        out var shiftedPrice))
                {
                    return ErrorValue.Num;
                }

                var derivative = (shiftedPrice - calculatedPrice) / 1E-6d;
                if (!double.IsFinite(derivative))
                    return ErrorValue.Num;

                if (Math.Abs(derivative) < 1E-14d)
                    break;

                var delta = (calculatedPrice - price) / derivative;
                if (!double.IsFinite(delta))
                    return ErrorValue.Num;

                yield -= delta;
                if (yield < -0.999d)
                    yield = -0.999d;

                if (Math.Abs(delta) < 1E-10d)
                    break;
            }

            return FormulaFinancialNumberResult(yield);
        }

        private static ScalarValue FormulaFinancialOddlpriceScalar(
            double settlement,
            double maturity,
            double lastInterest,
            double rate,
            double yield,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(lastInterest) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(yield) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                yield < 0d ||
                redemption <= 0d ||
                !TryGetFormulaFinancialOddLastCouponDates(
                    settlement,
                    maturity,
                    lastInterest,
                    frequency,
                    basis,
                    out var settlementDate,
                    out var maturityDate,
                    out var lastInterestDate))
            {
                return ErrorValue.Num;
            }

            return TryCalculateFormulaFinancialOddLastPrice(
                    lastInterestDate,
                    settlementDate,
                    maturityDate,
                    rate,
                    yield,
                    redemption,
                    frequency,
                    basis,
                    out var price)
                ? FormulaFinancialNumberResult(price)
                : ErrorValue.Num;
        }

        private static ScalarValue FormulaFinancialOddlyieldScalar(
            double settlement,
            double maturity,
            double lastInterest,
            double rate,
            double price,
            double redemption,
            int frequency,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(lastInterest) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(price) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d ||
                price <= 0d ||
                redemption <= 0d ||
                !TryGetFormulaFinancialOddLastCouponDates(
                    settlement,
                    maturity,
                    lastInterest,
                    frequency,
                    basis,
                    out var settlementDate,
                    out var maturityDate,
                    out var lastInterestDate))
            {
                return ErrorValue.Num;
            }

            if (!TryGetFormulaFinancialOddLastCouponPeriods(
                    lastInterestDate,
                    settlementDate,
                    maturityDate,
                    frequency,
                    basis,
                    out var accruedPeriods,
                    out var remainingPeriods,
                    out var oddCouponPeriods))
            {
                return ErrorValue.Num;
            }

            var couponAmount = rate / frequency * redemption;
            var numerator = redemption + couponAmount * oddCouponPeriods;
            var denominator = price + couponAmount * accruedPeriods;
            if (Math.Abs(remainingPeriods) < 1E-14d ||
                Math.Abs(denominator) < 1E-14d)
            {
                return ErrorValue.DivByZero;
            }

            return FormulaFinancialNumberResult((numerator / denominator - 1d) / remainingPeriods * frequency);
        }

        private static bool TryGetFormulaFinancialOddFirstCouponDates(
            double settlement,
            double maturity,
            double issue,
            double firstCoupon,
            int frequency,
            int basis,
            out DateTime settlementDate,
            out DateTime maturityDate,
            out DateTime issueDate,
            out DateTime firstCouponDate)
        {
            settlementDate = default;
            maturityDate = default;
            issueDate = default;
            firstCouponDate = default;
            return frequency is 1 or 2 or 4 &&
                basis is >= 0 and <= 4 &&
                TryGetFormulaFinancialCouponDate(settlement, out settlementDate) &&
                TryGetFormulaFinancialCouponDate(maturity, out maturityDate) &&
                TryGetFormulaFinancialCouponDate(issue, out issueDate) &&
                TryGetFormulaFinancialCouponDate(firstCoupon, out firstCouponDate) &&
                maturityDate > firstCouponDate &&
                firstCouponDate > settlementDate &&
                settlementDate > issueDate;
        }

        private static bool TryGetFormulaFinancialOddLastCouponDates(
            double settlement,
            double maturity,
            double lastInterest,
            int frequency,
            int basis,
            out DateTime settlementDate,
            out DateTime maturityDate,
            out DateTime lastInterestDate)
        {
            settlementDate = default;
            maturityDate = default;
            lastInterestDate = default;
            return frequency is 1 or 2 or 4 &&
                basis is >= 0 and <= 4 &&
                TryGetFormulaFinancialCouponDate(settlement, out settlementDate) &&
                TryGetFormulaFinancialCouponDate(maturity, out maturityDate) &&
                TryGetFormulaFinancialCouponDate(lastInterest, out lastInterestDate) &&
                maturityDate > settlementDate &&
                settlementDate > lastInterestDate;
        }

        private static bool TryCalculateFormulaFinancialOddFirstPrice(
            DateTime issue,
            DateTime settlement,
            DateTime maturity,
            DateTime firstCoupon,
            double rate,
            double yield,
            double redemption,
            int frequency,
            int basis,
            out double price)
        {
            price = 0d;
            var yieldPerPeriod = yield / frequency;
            if (1d + yieldPerPeriod <= 0d)
                return false;

            var months = 12 / frequency;
            DateTime previousCoupon;
            try
            {
                previousCoupon = firstCoupon.AddMonths(-months);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            var daysInCoupon = FormulaFinancialCouponPeriodDays(previousCoupon, frequency, basis);
            if (!double.IsFinite(daysInCoupon) || Math.Abs(daysInCoupon) < 1E-14d)
                return false;

            var accrued = FormulaFinancialDays(issue, settlement, basis) / daysInCoupon;
            var firstCouponPeriods = FormulaFinancialDays(issue, firstCoupon, basis) / daysInCoupon;
            var periodsToFirstCoupon = FormulaFinancialDays(settlement, firstCoupon, basis) / daysInCoupon;
            var couponAmount = rate / frequency * redemption;
            price = couponAmount * firstCouponPeriods / Math.Pow(1d + yieldPerPeriod, periodsToFirstCoupon);

            var period = 1;
            try
            {
                for (var currentDate = firstCoupon.AddMonths(months); currentDate <= maturity; currentDate = currentDate.AddMonths(months))
                {
                    if (period >= MaxFormulaFinancialBondCouponIterations)
                        return false;

                    var cash = couponAmount;
                    if (currentDate == maturity)
                        cash += redemption;

                    price += cash / Math.Pow(1d + yieldPerPeriod, period + periodsToFirstCoupon);
                    period++;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            price -= couponAmount * accrued;
            return double.IsFinite(price);
        }

        private static bool TryCalculateFormulaFinancialOddLastPrice(
            DateTime lastInterest,
            DateTime settlement,
            DateTime maturity,
            double rate,
            double yield,
            double redemption,
            int frequency,
            int basis,
            out double price)
        {
            price = 0d;
            if (!TryGetFormulaFinancialOddLastCouponPeriods(
                    lastInterest,
                    settlement,
                    maturity,
                    frequency,
                    basis,
                    out var accruedPeriods,
                    out var remainingPeriods,
                    out var oddCouponPeriods))
            {
                return false;
            }

            var couponAmount = rate / frequency * redemption;
            var denominator = 1d + remainingPeriods * yield / frequency;
            if (Math.Abs(denominator) < 1E-14d)
                return false;

            price = (redemption + couponAmount * oddCouponPeriods) / denominator - couponAmount * accruedPeriods;
            return double.IsFinite(price);
        }

        private static bool TryGetFormulaFinancialOddLastCouponPeriods(
            DateTime lastInterest,
            DateTime settlement,
            DateTime maturity,
            int frequency,
            int basis,
            out double accruedPeriods,
            out double remainingPeriods,
            out double oddCouponPeriods)
        {
            accruedPeriods = 0d;
            remainingPeriods = 0d;
            oddCouponPeriods = 0d;
            var daysInCoupon = FormulaFinancialCouponPeriodDays(lastInterest, frequency, basis);
            if (!double.IsFinite(daysInCoupon) || Math.Abs(daysInCoupon) < 1E-14d)
                return false;

            accruedPeriods = FormulaFinancialDays(lastInterest, settlement, basis) / daysInCoupon;
            remainingPeriods = FormulaFinancialDays(settlement, maturity, basis) / daysInCoupon;
            oddCouponPeriods = FormulaFinancialDays(lastInterest, maturity, basis) / daysInCoupon;
            return double.IsFinite(accruedPeriods) &&
                double.IsFinite(remainingPeriods) &&
                double.IsFinite(oddCouponPeriods);
        }

        private static double FormulaFinancialCouponPeriodDays(DateTime periodStart, int frequency, int basis) =>
            FormulaFinancialDays(periodStart, periodStart.AddMonths(12 / frequency), basis);

        private static double FormulaFinancialDays(DateTime start, DateTime end, int basis) =>
            basis switch
            {
                0 => FormulaFinancialDays360Us(start, end),
                4 => FormulaDays30E360(start, end),
                _ => (end - start).TotalDays
            };

        private static double FormulaFinancialDays360Us(DateTime start, DateTime end)
        {
            var startDay = start.Day;
            var endDay = end.Day;
            if (endDay == 31 && startDay >= 30)
                endDay = 30;
            if (startDay == 31)
                startDay = 30;

            return (end.Year - start.Year) * 360d + (end.Month - start.Month) * 30d + (endDay - startDay);
        }

        private static bool TryValidateFormulaFinancialBondSchedule(
            double settlement,
            double maturity,
            int frequency,
            int basis,
            out DateTime settlementDate,
            out DateTime maturityDate)
        {
            settlementDate = default;
            maturityDate = default;
            return frequency is 1 or 2 or 4 &&
                basis is >= 0 and <= 4 &&
                TryGetFormulaFinancialBondDates(settlement, maturity, out settlementDate, out maturityDate);
        }

        private static bool TryGetFormulaFinancialBondDates(
            double settlement,
            double maturity,
            out DateTime settlementDate,
            out DateTime maturityDate)
        {
            settlementDate = default;
            maturityDate = default;
            return TryGetFormulaFinancialCouponDate(settlement, out settlementDate) &&
                TryGetFormulaFinancialCouponDate(maturity, out maturityDate) &&
                settlementDate < maturityDate;
        }

        private static bool TryCalculateFormulaFinancialBondPrice(
            DateTime settlement,
            DateTime maturity,
            double couponRate,
            double yield,
            double redemption,
            int frequency,
            out double price)
        {
            price = 0d;
            if (!TryGetFormulaFinancialCouponSchedule(
                    settlement,
                    maturity,
                    frequency,
                    out var previousCouponDate,
                    out var nextCouponDate,
                    out var couponCount))
            {
                return false;
            }

            var daysInPeriod = (nextCouponDate - previousCouponDate).TotalDays;
            if (daysInPeriod <= 0d)
                return false;

            var fractionalPeriodsToNextCoupon = (nextCouponDate - settlement).TotalDays / daysInPeriod;
            var couponPayment = couponRate / frequency * redemption;
            var yieldPerPeriod = yield / frequency;
            if (1d + yieldPerPeriod <= 0d)
                return false;

            for (var period = 1; period <= couponCount; period++)
                price += couponPayment / Math.Pow(1d + yieldPerPeriod, period - 1d + fractionalPeriodsToNextCoupon);

            price += redemption / Math.Pow(1d + yieldPerPeriod, couponCount - 1d + fractionalPeriodsToNextCoupon);
            return double.IsFinite(price);
        }

        private static bool TryGetFormulaFinancialCouponSchedule(
            DateTime settlement,
            DateTime maturity,
            int frequency,
            out DateTime previousCouponDate,
            out DateTime nextCouponDate,
            out int couponCount)
        {
            previousCouponDate = default;
            nextCouponDate = default;
            couponCount = 0;
            if (!TryGetFormulaFinancialCouponDateBefore(settlement, maturity, frequency, out previousCouponDate))
                return false;

            var months = 12 / frequency;
            try
            {
                nextCouponDate = previousCouponDate.AddMonths(months);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            var currentCouponDate = nextCouponDate;
            while (currentCouponDate <= maturity)
            {
                if (couponCount >= MaxFormulaFinancialBondCouponIterations)
                    return false;

                couponCount++;
                try
                {
                    currentCouponDate = currentCouponDate.AddMonths(months);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }

            if (couponCount == 0)
                couponCount = 1;

            return true;
        }

        private static bool TryGetFormulaFinancialCouponDateBefore(
            DateTime settlement,
            DateTime maturity,
            int frequency,
            out DateTime previousCouponDate)
        {
            previousCouponDate = maturity;
            var months = 12 / frequency;
            for (var iteration = 0; previousCouponDate > settlement; iteration++)
            {
                if (iteration >= MaxFormulaFinancialBondCouponIterations)
                    return false;

                try
                {
                    previousCouponDate = previousCouponDate.AddMonths(-months);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetFormulaFinancialBondFrequencyAndBasis(
            IReadOnlyList<ScalarValue> arguments,
            int frequencyIndex,
            int basisIndex,
            out int frequency,
            out int basis,
            out ScalarValue error)
        {
            frequency = 0;
            basis = 0;
            if (!TryGetFormulaFinancialNumber(arguments[frequencyIndex], out var rawFrequency, out error))
                return false;

            if (!TryGetFormulaFinancialCouponFrequency(rawFrequency, out frequency))
            {
                error = ErrorValue.Num;
                return false;
            }

            return TryGetFormulaFinancialOptionalBasis(arguments, basisIndex, out basis, out error);
        }

        private static bool TryGetFormulaFinancialOptionalBasis(
            IReadOnlyList<ScalarValue> arguments,
            int index,
            out int basis,
            out ScalarValue error)
        {
            basis = 0;
            if (!TryGetFormulaFinancialOptionalNumber(arguments, index, 0d, out var rawBasis, out error))
                return false;

            if (TryGetFormulaFinancialBasis(rawBasis, out basis))
                return true;

            error = ErrorValue.Num;
            return false;
        }

        private static ScalarValue FormulaFinancialDiscScalar(
            double settlement,
            double maturity,
            double price,
            double redemption,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(price) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (price <= 0d || redemption <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            var dayCountFraction = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            if (dayCountFraction <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((redemption - price) / redemption / dayCountFraction);
        }

        private static ScalarValue FormulaFinancialIntrateScalar(
            double settlement,
            double maturity,
            double investment,
            double redemption,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(investment) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (investment <= 0d || redemption <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            var dayCountFraction = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            if (dayCountFraction <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((redemption - investment) / investment / dayCountFraction);
        }

        private static ScalarValue FormulaFinancialReceivedScalar(
            double settlement,
            double maturity,
            double investment,
            double discount,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(investment) ||
                !double.IsFinite(discount))
            {
                return ErrorValue.Num;
            }

            if (investment <= 0d || discount <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            var dayCountFraction = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            var denominator = 1d - discount * dayCountFraction;
            if (Math.Abs(denominator) < 1E-14d)
                return ErrorValue.DivByZero;

            return FormulaFinancialNumberResult(investment / denominator);
        }

        private static ScalarValue FormulaFinancialPricediscScalar(
            double settlement,
            double maturity,
            double discount,
            double redemption,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(discount) ||
                !double.IsFinite(redemption))
            {
                return ErrorValue.Num;
            }

            if (discount <= 0d || redemption <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            var dayCountFraction = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            return FormulaFinancialNumberResult(redemption * (1d - discount * dayCountFraction));
        }

        private static ScalarValue FormulaFinancialPricematScalar(
            double settlement,
            double maturity,
            double issue,
            double rate,
            double yieldRate,
            int basis)
        {
            if (!double.IsFinite(settlement) ||
                !double.IsFinite(maturity) ||
                !double.IsFinite(issue) ||
                !double.IsFinite(rate) ||
                !double.IsFinite(yieldRate))
            {
                return ErrorValue.Num;
            }

            if (rate < 0d || yieldRate < 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate) ||
                !TryGetFormulaFinancialDate(issue, out var issueDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            var issueToMaturity = FormulaFinancialDayCountFraction(issueDate, maturityDate, basis);
            var settlementToMaturity = FormulaFinancialDayCountFraction(settlementDate, maturityDate, basis);
            return FormulaFinancialNumberResult(100d * (1d + rate * issueToMaturity) / (1d + yieldRate * settlementToMaturity));
        }

        private static ScalarValue FormulaFinancialTbilleqScalar(double settlement, double maturity, double discount)
        {
            if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
                return ErrorValue.Num;

            if (discount <= 0d || discount >= 1d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            var daysSettlementToMaturity = (maturityDate - settlementDate).TotalDays;
            if (daysSettlementToMaturity <= 0d || daysSettlementToMaturity > 182d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((365d * discount) / (360d - discount * daysSettlementToMaturity));
        }

        private static ScalarValue FormulaFinancialTbillpriceScalar(double settlement, double maturity, double discount)
        {
            if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
                return ErrorValue.Num;

            if (discount <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            var daysSettlementToMaturity = (maturityDate - settlementDate).TotalDays;
            if (daysSettlementToMaturity <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult(100d * (1d - discount * daysSettlementToMaturity / 360d));
        }

        private static ScalarValue FormulaFinancialTbillyieldScalar(double settlement, double maturity, double price)
        {
            if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(price))
                return ErrorValue.Num;

            if (price <= 0d)
                return ErrorValue.Num;

            if (!TryGetFormulaFinancialDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialDate(maturity, out var maturityDate))
            {
                return ErrorValue.Num;
            }

            var daysSettlementToMaturity = (maturityDate - settlementDate).TotalDays;
            if (daysSettlementToMaturity <= 0d)
                return ErrorValue.Num;

            return FormulaFinancialNumberResult((100d - price) / price * 360d / daysSettlementToMaturity);
        }

        private static double FormulaFinancialDayCountFraction(DateTime start, DateTime end, int basis)
        {
            switch (basis)
            {
                case 0:
                    var startDay = start.Day;
                    var endDay = end.Day;
                    if (endDay == 31 && startDay >= 30)
                        endDay = 30;
                    if (startDay == 31)
                        startDay = 30;

                    return ((end.Year - start.Year) * 360d + (end.Month - start.Month) * 30d + (endDay - startDay)) / 360d;
                case 1:
                    return (end - start).TotalDays / FormulaFinancialActualYearLength(start, end);
                case 2:
                    return (end - start).TotalDays / 360d;
                case 3:
                    return (end - start).TotalDays / 365d;
                case 4:
                    return FormulaDays30E360(start, end) / 360d;
                default:
                    return (end - start).TotalDays / 365d;
            }
        }

        private static double FormulaFinancialActualYearLength(DateTime start, DateTime end)
        {
            if (start.Year == end.Year)
                return DateTime.IsLeapYear(start.Year) ? 366d : 365d;

            var years = end.Year - start.Year;
            var days = (end - start).TotalDays;
            return days / years;
        }

        private static bool TryGetFormulaFinancialDate(double serial, out DateTime date) =>
            TryGetFormulaFinancialCouponDate(serial, out date);

        private static bool TryGetFormulaFinancialCouponArguments(
            IReadOnlyList<ScalarValue> arguments,
            out double settlement,
            out double maturity,
            out int frequency,
            out int basis,
            out ScalarValue error)
        {
            settlement = 0d;
            maturity = 0d;
            frequency = 0;
            basis = 0;
            if (!TryGetFormulaFinancialNumber(arguments[0], out settlement, out error) ||
                !TryGetFormulaFinancialNumber(arguments[1], out maturity, out error) ||
                !TryGetFormulaFinancialNumber(arguments[2], out var rawFrequency, out error))
            {
                return false;
            }

            if (!TryGetFormulaFinancialCouponFrequency(rawFrequency, out frequency))
            {
                error = ErrorValue.Num;
                return false;
            }

            if (!TryGetFormulaFinancialOptionalNumber(arguments, 3, 0d, out var rawBasis, out error))
                return false;

            if (!TryGetFormulaFinancialBasis(rawBasis, out basis))
            {
                error = ErrorValue.Num;
                return false;
            }

            return true;
        }

        private static bool TryGetFormulaFinancialCouponFrequency(double rawFrequency, out int frequency)
        {
            frequency = 0;
            if (!double.IsFinite(rawFrequency) ||
                rawFrequency < int.MinValue ||
                rawFrequency > int.MaxValue)
            {
                return false;
            }

            frequency = (int)Math.Truncate(rawFrequency);
            return frequency is 1 or 2 or 4;
        }

        private static bool TryGetFormulaFinancialBasis(double rawBasis, out int basis)
        {
            basis = 0;
            if (!double.IsFinite(rawBasis) ||
                rawBasis < int.MinValue ||
                rawBasis > int.MaxValue)
            {
                return false;
            }

            basis = (int)Math.Truncate(rawBasis);
            return basis is >= 0 and <= 4;
        }

        private static ScalarValue FormulaFinancialCouponScalar(
            ConditionalFormulaScalarFunctionKind kind,
            double settlement,
            double maturity,
            int frequency,
            int basis)
        {
            if (!TryGetFormulaFinancialCouponDate(settlement, out var settlementDate) ||
                !TryGetFormulaFinancialCouponDate(maturity, out var maturityDate) ||
                settlementDate >= maturityDate)
            {
                return ErrorValue.Num;
            }

            try
            {
                var previousCouponDate = FormulaFinancialCouponDateBefore(settlementDate, maturityDate, frequency);
                var nextCouponDate = previousCouponDate.AddMonths(12 / frequency);
                return kind switch
                {
                    ConditionalFormulaScalarFunctionKind.Coupdaybs =>
                        FormulaFinancialNumberResult((settlementDate - previousCouponDate).TotalDays),
                    ConditionalFormulaScalarFunctionKind.Coupdays =>
                        FormulaFinancialNumberResult(basis == 1
                            ? (nextCouponDate - previousCouponDate).TotalDays
                            : 365d / frequency),
                    ConditionalFormulaScalarFunctionKind.Coupdaysnc =>
                        FormulaFinancialNumberResult((nextCouponDate - settlementDate).TotalDays),
                    ConditionalFormulaScalarFunctionKind.Coupncd =>
                        FormulaFinancialNumberResult(FormulaDateToExcelSerial(nextCouponDate)),
                    ConditionalFormulaScalarFunctionKind.Coupnum =>
                        FormulaFinancialNumberResult(FormulaFinancialCouponCount(settlementDate, maturityDate, frequency)),
                    ConditionalFormulaScalarFunctionKind.Couppcd =>
                        FormulaFinancialNumberResult(FormulaDateToExcelSerial(previousCouponDate)),
                    _ => ErrorValue.Value
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return ErrorValue.Num;
            }
        }

        private static bool TryGetFormulaFinancialCouponDate(double serial, out DateTime date)
        {
            date = default;
            if (!double.IsFinite(serial) ||
                serial < 0d ||
                serial > 2958465d)
            {
                return false;
            }

            try
            {
                date = FormulaExcelSerialToDate(serial);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static DateTime FormulaFinancialCouponDateBefore(
            DateTime settlement,
            DateTime maturity,
            int frequency)
        {
            var months = 12 / frequency;
            var previous = maturity;
            while (previous > settlement)
                previous = previous.AddMonths(-months);

            return previous;
        }

        private static int FormulaFinancialCouponCount(
            DateTime settlement,
            DateTime maturity,
            int frequency)
        {
            var months = 12 / frequency;
            var count = 0;
            var current = maturity;
            while (current > settlement)
            {
                count++;
                current = current.AddMonths(-months);
            }

            return count;
        }

        private static bool TryGetFormulaFinancialInteger(double value, out int integer)
        {
            integer = 0;
            if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
                return false;

            integer = (int)Math.Truncate(value);
            return true;
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

        private bool TryEvaluateFormulaTextFormatFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var source) ||
                !TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out var formatSource))
            {
                return false;
            }

            value = MapFormulaTextScalarBinaryArguments(source, formatSource, FormulaTextFormatScalar);
            return true;
        }

        private bool TryEvaluateFormulaFixedFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var numberSource))
                return false;

            var decimalsSource = (ScalarValue)new NumberValue(2);
            if (function.Arguments.Count >= 2 &&
                !TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out decimalsSource))
            {
                return false;
            }

            var noCommasSource = (ScalarValue)BlankValue.Instance;
            if (function.Arguments.Count == 3 &&
                !TryResolveFormulaTextScalarArgument(function.Arguments[2], rowOffset, colOffset, out noCommasSource))
            {
                return false;
            }

            value = MapFormulaTextScalarArguments(
                new[] { numberSource, decimalsSource, noCommasSource },
                static arguments => FormulaFixedScalar(arguments[0], arguments[1], arguments[2]));
            return true;
        }

        private bool TryEvaluateFormulaDollarFunction(
            ConditionalFormulaScalarFunction function,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (!TryResolveFormulaTextScalarArgument(function.Arguments[0], rowOffset, colOffset, out var numberSource))
                return false;

            var decimalsSource = (ScalarValue)new NumberValue(2);
            if (function.Arguments.Count == 2 &&
                !TryResolveFormulaTextScalarArgument(function.Arguments[1], rowOffset, colOffset, out decimalsSource))
            {
                return false;
            }

            value = MapFormulaTextScalarBinaryArguments(numberSource, decimalsSource, FormulaDollarScalar);
            return true;
        }

        private static ScalarValue FormulaTextFormatScalar(ScalarValue value, ScalarValue formatValue)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (formatValue is ErrorValue formatError)
                return formatError;

            if (!TryGetFormulaCoercedText(formatValue, out var formatText) ||
                FormulaTextScalarExceedsExcelTextLimit(formatText))
            {
                return ErrorValue.Value;
            }

            if (value is NumberValue or DateTimeValue)
            {
                try
                {
                    return FormulaTextScalarResult(NumberFormatter.Format(value, formatText));
                }
                catch (FormulaEvalException ex)
                {
                    return FormulaErrorValueFromCode(ex.ErrorCode);
                }
                catch
                {
                    return ErrorValue.Value;
                }
            }

            return TryGetFormulaCoercedText(value, out var text)
                ? FormulaTextScalarResult(text)
                : ErrorValue.Value;
        }

        private static ScalarValue FormulaFixedScalar(
            ScalarValue value,
            ScalarValue decimalsValue,
            ScalarValue noCommasValue)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (decimalsValue is ErrorValue decimalsError)
                return decimalsError;

            if (noCommasValue is ErrorValue noCommasError)
                return noCommasError;

            if (!TryGetFormulaTextFormattingNumber(value, out var number, out var error) ||
                !TryGetFormulaTextFormattingInteger(decimalsValue, out var decimals, out error) ||
                !TryGetFormulaTextFormattingBoolean(noCommasValue, out var noCommas, out error))
                return error ?? ErrorValue.Value;

            return FormulaTextFormatRoundedNumber(number, decimals, useCommas: !noCommas);
        }

        private static ScalarValue FormulaDollarScalar(ScalarValue value, ScalarValue decimalsValue)
        {
            if (value is ErrorValue valueError)
                return valueError;

            if (decimalsValue is ErrorValue decimalsError)
                return decimalsError;

            if (!TryGetFormulaTextFormattingNumber(value, out var number, out var error) ||
                !TryGetFormulaTextFormattingInteger(decimalsValue, out var decimals, out error))
                return error ?? ErrorValue.Value;

            var numberText = FormulaTextFormatRoundedNumber(Math.Abs(number), decimals, useCommas: true);
            if (numberText is not TextValue text)
                return numberText;

            var formatted = "$" + text.Value;
            return FormulaTextScalarResult(number < 0d && (decimals >= 0 || text.Value != "0")
                ? "(" + formatted + ")"
                : formatted);
        }

        private static ScalarValue FormulaTextFormatRoundedNumber(
            double value,
            int decimals,
            bool useCommas)
        {
            if (!double.IsFinite(value))
                return ErrorValue.Num;

            if (decimals > MaxFormulaTextSliceLength)
                return ErrorValue.Value;

            var rounded = decimals < -308
                ? 0d
                : decimals <= MaxFormulaRoundDigits
                    ? RoundFormulaNumber(value, decimals)
                    : value;
            if (!double.IsFinite(rounded))
                return ErrorValue.Num;

            var displayDecimals = Math.Clamp(decimals, 0, 99);
            var format = (useCommas ? "N" : "F") + displayDecimals.ToString(CultureInfo.InvariantCulture);
            return FormulaTextScalarResult(rounded.ToString(format, CultureInfo.InvariantCulture));
        }

        private static bool TryGetFormulaTextFormattingNumber(
            ScalarValue value,
            out double number,
            out ErrorValue? error)
        {
            error = null;
            if (!TryGetFormulaTextScalarNumber(value, out number))
            {
                error = ErrorValue.Value;
                return false;
            }

            if (!double.IsFinite(number))
            {
                error = ErrorValue.Num;
                return false;
            }

            return true;
        }

        private static bool TryGetFormulaTextFormattingInteger(
            ScalarValue value,
            out int integer,
            out ErrorValue? error)
        {
            integer = 0;
            if (!TryGetFormulaTextFormattingNumber(value, out var number, out error))
                return false;

            if (number < int.MinValue || number > int.MaxValue)
            {
                error = ErrorValue.Num;
                return false;
            }

            integer = (int)number;
            return true;
        }

        private static bool TryGetFormulaTextFormattingBoolean(
            ScalarValue value,
            out bool boolean,
            out ErrorValue? error)
        {
            error = null;
            switch (value)
            {
                case BoolValue logical:
                    boolean = logical.Value;
                    return true;
                case NumberValue number when double.IsFinite(number.Value):
                    boolean = number.Value != 0d;
                    return true;
                case DateTimeValue dateTime when double.IsFinite(dateTime.Value):
                    boolean = dateTime.Value != 0d;
                    return true;
                case BlankValue:
                    boolean = false;
                    return true;
                default:
                    boolean = false;
                    error = value is NumberValue or DateTimeValue ? ErrorValue.Num : ErrorValue.Value;
                    return false;
            }
        }

        private static ErrorValue FormulaErrorValueFromCode(string code) => code.ToUpperInvariant() switch
        {
            "#DIV/0!" => ErrorValue.DivByZero,
            "#VALUE!" => ErrorValue.Value,
            "#REF!" => ErrorValue.Ref,
            "#NAME?" => ErrorValue.Name,
            "#NULL!" => ErrorValue.Null,
            "#N/A" => ErrorValue.NA,
            "#NUM!" => ErrorValue.Num,
            "#SPILL!" => ErrorValue.Spill,
            "#CALC!" => ErrorValue.Calc,
            _ => ErrorValue.Value
        };

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

        private static bool IsFormulaStatisticalSelectionAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
            aggregateKind is
                ConditionalFormulaAggregateKind.Large or
                ConditionalFormulaAggregateKind.Small or
                ConditionalFormulaAggregateKind.Rank or
                ConditionalFormulaAggregateKind.RankEq or
                ConditionalFormulaAggregateKind.RankAvg or
                ConditionalFormulaAggregateKind.PercentileInc or
                ConditionalFormulaAggregateKind.PercentileExc or
                ConditionalFormulaAggregateKind.QuartileInc or
                ConditionalFormulaAggregateKind.QuartileExc or
                ConditionalFormulaAggregateKind.PercentRankInc or
                ConditionalFormulaAggregateKind.PercentRankExc or
                ConditionalFormulaAggregateKind.ModeSngl or
                ConditionalFormulaAggregateKind.Prob or
                ConditionalFormulaAggregateKind.PercentOf;

        private bool TryEvaluateFormulaStatisticalSelectionAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: > 0 } arguments)
                return false;

            return operand.AggregateKind switch
            {
                ConditionalFormulaAggregateKind.Large =>
                    TryEvaluateFormulaLargeSmallAggregate(arguments, rowOffset, colOffset, largest: true, out value),
                ConditionalFormulaAggregateKind.Small =>
                    TryEvaluateFormulaLargeSmallAggregate(arguments, rowOffset, colOffset, largest: false, out value),
                ConditionalFormulaAggregateKind.Rank or
                ConditionalFormulaAggregateKind.RankEq =>
                    TryEvaluateFormulaRankAggregate(arguments, rowOffset, colOffset, averageTies: false, requireRangeArray: false, out value),
                ConditionalFormulaAggregateKind.RankAvg =>
                    TryEvaluateFormulaRankAggregate(arguments, rowOffset, colOffset, averageTies: true, requireRangeArray: true, out value),
                ConditionalFormulaAggregateKind.PercentileInc =>
                    TryEvaluateFormulaPercentileAggregate(arguments, rowOffset, colOffset, inclusive: true, out value),
                ConditionalFormulaAggregateKind.PercentileExc =>
                    TryEvaluateFormulaPercentileAggregate(arguments, rowOffset, colOffset, inclusive: false, out value),
                ConditionalFormulaAggregateKind.QuartileInc =>
                    TryEvaluateFormulaQuartileAggregate(arguments, rowOffset, colOffset, inclusive: true, out value),
                ConditionalFormulaAggregateKind.QuartileExc =>
                    TryEvaluateFormulaQuartileAggregate(arguments, rowOffset, colOffset, inclusive: false, out value),
                ConditionalFormulaAggregateKind.PercentRankInc =>
                    TryEvaluateFormulaPercentRankAggregate(arguments, rowOffset, colOffset, inclusive: true, out value),
                ConditionalFormulaAggregateKind.PercentRankExc =>
                    TryEvaluateFormulaPercentRankAggregate(arguments, rowOffset, colOffset, inclusive: false, out value),
                ConditionalFormulaAggregateKind.ModeSngl =>
                    TryEvaluateFormulaModeSnglAggregate(arguments, rowOffset, colOffset, out value),
                ConditionalFormulaAggregateKind.Prob =>
                    TryEvaluateFormulaProbAggregate(arguments, rowOffset, colOffset, out value),
                ConditionalFormulaAggregateKind.PercentOf =>
                    TryEvaluateFormulaPercentOfAggregate(arguments, rowOffset, colOffset, out value),
                _ => false
            };
        }

        private bool TryEvaluateFormulaLargeSmallAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool largest,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count != 2 ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[0], rowOffset, colOffset, out var range) ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[1], rowOffset, colOffset, out var kValue))
            {
                return false;
            }

            if (kValue is ErrorValue kError)
            {
                value = kError;
                return true;
            }

            if (!TryGetFormulaStatisticalNumber(kValue, out var rawK, out var kCoercionError))
            {
                value = kCoercionError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(rawK) || rawK < int.MinValue || rawK > int.MaxValue)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryCollectFormulaStatisticalArrayNumbers(range, out var numbers, out var rangeError))
            {
                value = rangeError ?? ErrorValue.Value;
                return true;
            }

            var k = (int)rawK;
            if (k < 1 || k > numbers.Count)
            {
                value = ErrorValue.Num;
                return true;
            }

            numbers.Sort();
            value = FormulaStatisticalNumberResult(largest ? numbers[^k] : numbers[k - 1]);
            return true;
        }

        private bool TryEvaluateFormulaRankAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool averageTies,
            bool requireRangeArray,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count is < 2 or > 3 ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[0], rowOffset, colOffset, out var numberValue))
            {
                return false;
            }

            if (numberValue is ErrorValue numberError)
            {
                value = numberError;
                return true;
            }

            if (requireRangeArray &&
                !IsFormulaStatisticalRangeArgument(arguments[1]))
            {
                value = ErrorValue.Value;
                return true;
            }

            if (!TryResolveFormulaStatisticalArrayArgument(arguments[1], rowOffset, colOffset, out var range))
                return false;

            var orderValue = (ScalarValue)BlankValue.Instance;
            if (arguments.Count == 3 &&
                !TryResolveFormulaStatisticalScalarArgument(arguments[2], rowOffset, colOffset, out orderValue))
            {
                return false;
            }

            if (orderValue is ErrorValue orderError)
            {
                value = orderError;
                return true;
            }

            var numberIsValid = TryGetFormulaStatisticalNumber(numberValue, out var number, out var numberCoercionError);
            var orderIsValid = TryGetFormulaStatisticalNumber(orderValue, out var rawOrder, out var orderCoercionError);
            if (!numberIsValid || !orderIsValid)
            {
                value = numberCoercionError ?? orderCoercionError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(number) || !double.IsFinite(rawOrder))
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryCollectFormulaStatisticalArrayNumbers(range, out var numbers, out var rangeError))
            {
                value = rangeError ?? ErrorValue.Value;
                return true;
            }

            if (!numbers.Contains(number))
            {
                value = ErrorValue.NA;
                return true;
            }

            var descending = rawOrder == 0d;
            var betterCount = descending
                ? numbers.Count(candidate => candidate > number)
                : numbers.Count(candidate => candidate < number);
            if (!averageTies)
            {
                value = new NumberValue(betterCount + 1);
                return true;
            }

            var tieCount = numbers.Count(candidate => candidate == number);
            value = new NumberValue(betterCount + 1 + (tieCount - 1) / 2.0);
            return true;
        }

        private bool TryEvaluateFormulaPercentileAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool inclusive,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count != 2 ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[0], rowOffset, colOffset, out var range) ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[1], rowOffset, colOffset, out var percentileValue))
            {
                return false;
            }

            if (percentileValue is ErrorValue percentileError)
            {
                value = percentileError;
                return true;
            }

            if (!TryGetFormulaStatisticalNumber(percentileValue, out var percentile, out var coercionError))
            {
                value = coercionError ?? ErrorValue.Value;
                return true;
            }

            value = EvaluateFormulaPercentile(range, percentile, inclusive);
            return true;
        }

        private bool TryEvaluateFormulaQuartileAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool inclusive,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count != 2 ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[0], rowOffset, colOffset, out var range) ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[1], rowOffset, colOffset, out var quartileValue))
            {
                return false;
            }

            if (quartileValue is ErrorValue quartileError)
            {
                value = quartileError;
                return true;
            }

            if (!TryGetFormulaStatisticalNumber(quartileValue, out var rawQuartile, out var coercionError))
            {
                value = coercionError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(rawQuartile) || rawQuartile < int.MinValue || rawQuartile > int.MaxValue)
            {
                value = ErrorValue.Num;
                return true;
            }

            var quartile = (int)rawQuartile;
            if (inclusive)
            {
                if (quartile is < 0 or > 4)
                {
                    value = ErrorValue.Num;
                    return true;
                }

                value = quartile switch
                {
                    0 => EvaluateFormulaPercentile(range, 0d, inclusive: true),
                    4 => EvaluateFormulaPercentile(range, 1d, inclusive: true),
                    _ => EvaluateFormulaPercentile(range, quartile / 4.0, inclusive: true)
                };
                return true;
            }

            if (quartile is < 1 or > 3)
            {
                value = ErrorValue.Num;
                return true;
            }

            value = EvaluateFormulaPercentile(range, quartile / 4.0, inclusive: false);
            return true;
        }

        private ScalarValue EvaluateFormulaPercentile(RangeValue range, double percentile, bool inclusive)
        {
            if (!double.IsFinite(percentile))
                return ErrorValue.Num;

            if (inclusive)
            {
                if (percentile is < 0d or > 1d)
                    return ErrorValue.Num;
            }
            else if (percentile <= 0d || percentile >= 1d)
            {
                return ErrorValue.Num;
            }

            if (!TryCollectFormulaStatisticalArrayNumbers(range, out var numbers, out var rangeError))
                return rangeError ?? ErrorValue.Value;

            numbers.Sort();
            var count = numbers.Count;
            if (count == 0)
                return ErrorValue.Num;

            var rank = inclusive
                ? percentile * (count - 1)
                : percentile * (count + 1) - 1;
            if (!inclusive && (rank < 0d || rank >= count))
                return ErrorValue.Num;

            var lowerIndex = (int)rank;
            if (lowerIndex >= count - 1)
                return FormulaStatisticalNumberResult(numbers[^1]);

            var lower = numbers[lowerIndex];
            var upper = numbers[lowerIndex + 1];
            return FormulaStatisticalNumberResult(lower + (rank - lowerIndex) * (upper - lower));
        }

        private bool TryEvaluateFormulaPercentRankAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool inclusive,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count is < 2 or > 3 ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[0], rowOffset, colOffset, out var range) ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[1], rowOffset, colOffset, out var xValue))
            {
                return false;
            }

            var significanceValue = (ScalarValue)BlankValue.Instance;
            if (arguments.Count == 3 &&
                !TryResolveFormulaStatisticalScalarArgument(arguments[2], rowOffset, colOffset, out significanceValue))
            {
                return false;
            }

            if (xValue is ErrorValue xError)
            {
                value = xError;
                return true;
            }

            if (significanceValue is ErrorValue significanceError)
            {
                value = significanceError;
                return true;
            }

            var xIsValid = TryGetFormulaStatisticalNumber(xValue, out var x, out var xCoercionError);
            var significanceIsValid = TryGetFormulaStatisticalNumber(significanceValue, out var rawSignificance, out var significanceCoercionError);
            if (!xIsValid || !significanceIsValid)
            {
                value = xCoercionError ?? significanceCoercionError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(x) || !double.IsFinite(rawSignificance) || rawSignificance > int.MaxValue)
            {
                value = ErrorValue.Num;
                return true;
            }

            var significance = significanceValue is BlankValue ? 3 : (int)rawSignificance;
            if (significance < 1)
            {
                value = ErrorValue.Num;
                return true;
            }

            if (!TryCollectFormulaStatisticalArrayNumbers(range, out var numbers, out var rangeError))
            {
                value = rangeError ?? ErrorValue.Value;
                return true;
            }

            numbers.Sort();
            var count = numbers.Count;
            if (count == 0 || x < numbers[0] || x > numbers[^1])
            {
                value = ErrorValue.NA;
                return true;
            }

            var factor = Math.Pow(10d, significance);
            if (!double.IsFinite(factor))
            {
                value = ErrorValue.Num;
                return true;
            }

            var below = numbers.Count(candidate => candidate < x);
            var equal = numbers.Count(candidate => candidate == x);
            double percentRank;
            if (equal > 0)
            {
                percentRank = inclusive
                    ? count == 1 ? 1d : (double)below / (count - 1)
                    : (below + 1.0) / (count + 1.0);
            }
            else
            {
                var lowerIndex = below - 1;
                if (lowerIndex < 0 || lowerIndex >= count - 1)
                {
                    value = ErrorValue.NA;
                    return true;
                }

                var lower = numbers[lowerIndex];
                var upper = numbers[lowerIndex + 1];
                var fraction = upper > lower ? (x - lower) / (upper - lower) : 0d;
                percentRank = inclusive
                    ? (lowerIndex + fraction) / (count - 1)
                    : (lowerIndex + 1.0 + fraction) / (count + 1.0);
            }

            value = FormulaStatisticalNumberResult(Math.Floor(percentRank * factor) / factor);
            return true;
        }

        private bool TryEvaluateFormulaModeSnglAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count is < 1 or > MaxFormulaModeArgumentCount)
                return false;

            var numbers = new List<double>();
            foreach (var argument in arguments)
            {
                if (!AppendFormulaModeSnglNumbers(argument, rowOffset, colOffset, numbers, out var error))
                {
                    value = error ?? ErrorValue.Value;
                    return error is not null;
                }
            }

            if (numbers.Count == 0)
            {
                value = ErrorValue.NA;
                return true;
            }

            var frequencies = new Dictionary<double, int>();
            var order = new List<double>();
            foreach (var number in numbers)
            {
                if (!double.IsFinite(number))
                {
                    value = ErrorValue.Num;
                    return true;
                }

                if (!frequencies.ContainsKey(number))
                    order.Add(number);

                frequencies[number] = frequencies.GetValueOrDefault(number) + 1;
            }

            var maxFrequency = frequencies.Values.Max();
            if (maxFrequency < 2)
            {
                value = ErrorValue.NA;
                return true;
            }

            foreach (var candidate in order)
            {
                if (frequencies[candidate] == maxFrequency)
                {
                    value = FormulaStatisticalNumberResult(candidate);
                    return true;
                }
            }

            value = ErrorValue.NA;
            return true;
        }

        private bool TryEvaluateFormulaProbAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count is < 3 or > 4 ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[0], rowOffset, colOffset, out var xRange) ||
                !TryResolveFormulaStatisticalArrayArgument(arguments[1], rowOffset, colOffset, out var probabilityRange) ||
                !TryResolveFormulaStatisticalScalarArgument(arguments[2], rowOffset, colOffset, out var lowerValue))
            {
                return false;
            }

            var upperValue = lowerValue;
            if (arguments.Count == 4 &&
                !TryResolveFormulaStatisticalScalarArgument(arguments[3], rowOffset, colOffset, out upperValue))
            {
                return false;
            }

            if (lowerValue is ErrorValue lowerError)
            {
                value = lowerError;
                return true;
            }

            if (upperValue is ErrorValue upperError)
            {
                value = upperError;
                return true;
            }

            if (xRange.RowCount != probabilityRange.RowCount ||
                xRange.ColCount != probabilityRange.ColCount)
            {
                value = ErrorValue.NA;
                return true;
            }

            var lowerIsValid = TryGetFormulaStatisticalNumber(lowerValue, out var lower, out var lowerCoercionError);
            var upperIsValid = TryGetFormulaStatisticalNumber(upperValue, out var upper, out var upperCoercionError);
            if (!lowerIsValid || !upperIsValid)
            {
                value = lowerCoercionError ?? upperCoercionError ?? ErrorValue.Value;
                return true;
            }

            if (!double.IsFinite(lower) || !double.IsFinite(upper))
            {
                value = ErrorValue.Num;
                return true;
            }

            var probabilitySum = 0d;
            var result = 0d;
            for (var row = 0; row < xRange.RowCount; row++)
            {
                for (var col = 0; col < xRange.ColCount; col++)
                {
                    var xCell = xRange.Cells[row, col];
                    var probabilityCell = probabilityRange.Cells[row, col];
                    if (xCell is ErrorValue xError)
                    {
                        value = xError;
                        return true;
                    }

                    if (probabilityCell is ErrorValue probabilityError)
                    {
                        value = probabilityError;
                        return true;
                    }

                    if (!TryGetFormulaStatisticalCellNumber(xCell, out var x) ||
                        !TryGetFormulaStatisticalCellNumber(probabilityCell, out var probability))
                    {
                        value = ErrorValue.Value;
                        return true;
                    }

                    if (!double.IsFinite(x) ||
                        !double.IsFinite(probability) ||
                        probability <= 0d ||
                        probability > 1d)
                    {
                        value = ErrorValue.Num;
                        return true;
                    }

                    probabilitySum += probability;
                    if (x >= lower && x <= upper)
                        result += probability;
                }
            }

            value = Math.Abs(probabilitySum - 1.0) <= 1e-12
                ? FormulaStatisticalNumberResult(result)
                : ErrorValue.Num;
            return true;
        }

        private bool TryEvaluateFormulaPercentOfAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count != 2)
                return false;

            if (!TryGetFormulaPercentOfSum(arguments[0], rowOffset, colOffset, out var subset, out var subsetError))
            {
                value = subsetError ?? ErrorValue.Value;
                return subsetError is not null;
            }

            if (!TryGetFormulaPercentOfSum(arguments[1], rowOffset, colOffset, out var total, out var totalError))
            {
                value = totalError ?? ErrorValue.Value;
                return totalError is not null;
            }

            if (total == 0d)
            {
                value = ErrorValue.DivByZero;
                return true;
            }

            value = FormulaStatisticalNumberResult(subset / total);
            return true;
        }

        private static bool IsFormulaStatisticalRangeArgument(ConditionalFormulaAggregateArgument argument) =>
            argument.Kind == ConditionalFormulaAggregateArgumentKind.Range ||
            argument.Kind == ConditionalFormulaAggregateArgumentKind.Operand &&
            argument.Operand is { } operand &&
            operand.Kind == ConditionalFormulaOperandKind.ReferenceRange;

        private bool TryResolveFormulaStatisticalArrayArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out RangeValue range)
        {
            range = default!;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    range = SingleFormulaStatisticalArray(argument.Literal ?? BlankValue.Instance);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    range = SingleFormulaStatisticalArray(targetSheet.GetValue(row, col), row, col, targetSheet.Name);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Range:
                    return TryMaterializeFormulaAggregateArgumentRange(argument, rowOffset, colOffset, out range);
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue)
                        return false;

                    if (argument.Operand.Value.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                        return TryMaterializeFormulaReferenceRange(argument.Operand.Value, rowOffset, colOffset, out range);

                    if (!TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out var value))
                        return false;

                    range = value is RangeValue resolvedRange
                        ? resolvedRange
                        : SingleFormulaStatisticalArray(value);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryResolveFormulaStatisticalScalarArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    value = argument.Literal ?? BlankValue.Instance;
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    value = targetSheet.GetValue(row, col);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Range:
                    if (!TryMaterializeFormulaAggregateArgumentRange(argument, rowOffset, colOffset, out var range))
                        return false;

                    return TryGetSingleFormulaStatisticalRangeValue(range, out value);
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue)
                        return false;

                    if (argument.Operand.Value.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                    {
                        if (!TryMaterializeFormulaReferenceRange(argument.Operand.Value, rowOffset, colOffset, out var referenceRange))
                            return false;

                        return TryGetSingleFormulaStatisticalRangeValue(referenceRange, out value);
                    }

                    if (!TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out value))
                        return false;

                    return value is not RangeValue resolvedRange ||
                        TryGetSingleFormulaStatisticalRangeValue(resolvedRange, out value);
                default:
                    return false;
            }
        }

        private bool TryMaterializeFormulaAggregateArgumentRange(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out RangeValue range)
        {
            range = default!;
            if (!TryResolveFormulaAggregateRange(
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

        private static RangeValue SingleFormulaStatisticalArray(
            ScalarValue value,
            uint row = 1,
            uint col = 1,
            string? sheetName = null) =>
            new(new[,] { { value } }, row, col) { SheetName = sheetName };

        private static bool TryGetSingleFormulaStatisticalRangeValue(RangeValue range, out ScalarValue value)
        {
            if (range.RowCount == 1 && range.ColCount == 1)
            {
                value = range.Cells[0, 0];
                return true;
            }

            value = ErrorValue.Value;
            return false;
        }

        private static bool TryCollectFormulaStatisticalArrayNumbers(
            RangeValue range,
            out List<double> numbers,
            out ErrorValue? error)
        {
            numbers = new List<double>(range.RowCount * range.ColCount);
            error = null;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    var value = range.Cells[row, col];
                    if (value is ErrorValue valueError)
                    {
                        error = valueError;
                        return false;
                    }

                    if (TryGetFormulaStatisticalCellNumber(value, out var number))
                        numbers.Add(number);
                }
            }

            return true;
        }

        private bool AppendFormulaModeSnglNumbers(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    return AppendFormulaModeSnglValue(argument.Literal ?? BlankValue.Instance, isDirectArgument: true, numbers, out error);
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    return AppendFormulaModeSnglValue(targetSheet.GetValue(row, col), isDirectArgument: false, numbers, out error);
                case ConditionalFormulaAggregateArgumentKind.Range:
                    if (!TryMaterializeFormulaAggregateArgumentRange(argument, rowOffset, colOffset, out var range))
                        return false;

                    return AppendFormulaModeSnglRange(range, numbers, out error);
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue)
                        return false;

                    if (argument.Operand.Value.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                    {
                        if (!TryMaterializeFormulaReferenceRange(argument.Operand.Value, rowOffset, colOffset, out var referenceRange))
                            return false;

                        return AppendFormulaModeSnglRange(referenceRange, numbers, out error);
                    }

                    if (!TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out var operandValue))
                        return false;

                    return operandValue is RangeValue operandRange
                        ? AppendFormulaModeSnglRange(operandRange, numbers, out error)
                        : AppendFormulaModeSnglValue(operandValue, isDirectArgument: true, numbers, out error);
                default:
                    return false;
            }
        }

        private static bool AppendFormulaModeSnglRange(
            RangeValue range,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    if (!AppendFormulaModeSnglValue(range.Cells[row, col], isDirectArgument: false, numbers, out error))
                        return false;
                }
            }

            return true;
        }

        private static bool AppendFormulaModeSnglValue(
            ScalarValue value,
            bool isDirectArgument,
            List<double> numbers,
            out ErrorValue? error)
        {
            error = null;
            switch (value)
            {
                case ErrorValue valueError:
                    error = valueError;
                    return false;
                case NumberValue numeric:
                    numbers.Add(numeric.Value);
                    return true;
                case DateTimeValue dateTime:
                    numbers.Add(dateTime.Value);
                    return true;
                case BoolValue boolean when isDirectArgument:
                    numbers.Add(boolean.Value ? 1d : 0d);
                    return true;
                case TextValue text when isDirectArgument:
                    if (!TryParseFormulaStatisticalNumberText(text.Value, out var parsed))
                    {
                        error = ErrorValue.Value;
                        return false;
                    }

                    if (!double.IsFinite(parsed))
                    {
                        error = ErrorValue.Num;
                        return false;
                    }

                    numbers.Add(parsed);
                    return true;
                default:
                    return true;
            }
        }

        private bool TryGetFormulaPercentOfSum(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out double sum,
            out ErrorValue? error)
        {
            sum = 0d;
            error = null;

            if (argument.Kind is ConditionalFormulaAggregateArgumentKind.Range ||
                argument.Kind == ConditionalFormulaAggregateArgumentKind.Operand &&
                argument.Operand is { } operand &&
                operand.Kind == ConditionalFormulaOperandKind.ReferenceRange)
            {
                if (!TryResolveFormulaStatisticalArrayArgument(argument, rowOffset, colOffset, out var range))
                    return false;

                return TryGetFormulaPercentOfRangeSum(range, out sum, out error);
            }

            if (!TryResolveFormulaStatisticalScalarArgument(argument, rowOffset, colOffset, out var value))
                return false;

            if (value is ErrorValue valueError)
            {
                error = valueError;
                return false;
            }

            if (value is RangeValue rangeValue)
                return TryGetFormulaPercentOfRangeSum(rangeValue, out sum, out error);

            if (!TryGetFormulaStatisticalNumber(value, out sum, out error))
                return false;

            if (!double.IsFinite(sum))
            {
                error = ErrorValue.Num;
                return false;
            }

            return true;
        }

        private static bool TryGetFormulaPercentOfRangeSum(
            RangeValue range,
            out double sum,
            out ErrorValue? error)
        {
            sum = 0d;
            error = null;
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

                    if (TryGetFormulaStatisticalCellNumber(cell, out var number))
                    {
                        sum += number;
                        if (!double.IsFinite(sum))
                        {
                            error = ErrorValue.Num;
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool TryGetFormulaStatisticalNumber(
            ScalarValue value,
            out double number,
            out ErrorValue? error)
        {
            error = null;
            switch (value)
            {
                case ErrorValue valueError:
                    number = 0d;
                    error = valueError;
                    return false;
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
                case TextValue text when TryParseFormulaStatisticalNumberText(text.Value, out var parsed):
                    number = parsed;
                    return true;
                default:
                    number = 0d;
                    error = ErrorValue.Value;
                    return false;
            }
        }

        private static bool TryParseFormulaStatisticalNumberText(string text, out double number)
        {
            number = 0d;
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

            var styles = NumberStyles.Float | NumberStyles.AllowThousands;
            if (!double.TryParse(candidate, styles, CultureInfo.InvariantCulture, out number))
                return false;

            if (isPercent)
                number /= 100d;

            return true;
        }

        private static bool TryGetFormulaStatisticalCellNumber(ScalarValue value, out double number)
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
                    number = 0d;
                    return false;
            }
        }

        private static ScalarValue FormulaStatisticalNumberResult(double value) =>
            double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

        private bool TryEvaluateFormulaConditionalAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: > 0 } arguments)
                return false;

            return operand.AggregateKind switch
            {
                ConditionalFormulaAggregateKind.SumIf =>
                    TryEvaluateFormulaSumAverageIfAggregate(arguments, rowOffset, colOffset, average: false, out value),
                ConditionalFormulaAggregateKind.CountIf =>
                    TryEvaluateFormulaCountIfAggregate(arguments, rowOffset, colOffset, out value),
                ConditionalFormulaAggregateKind.AverageIf =>
                    TryEvaluateFormulaSumAverageIfAggregate(arguments, rowOffset, colOffset, average: true, out value),
                ConditionalFormulaAggregateKind.SumIfs =>
                    TryEvaluateFormulaSumAverageIfsAggregate(arguments, rowOffset, colOffset, average: false, out value),
                ConditionalFormulaAggregateKind.CountIfs =>
                    TryEvaluateFormulaCountIfsAggregate(arguments, rowOffset, colOffset, out value),
                ConditionalFormulaAggregateKind.AverageIfs =>
                    TryEvaluateFormulaSumAverageIfsAggregate(arguments, rowOffset, colOffset, average: true, out value),
                _ => false
            };
        }

        private bool TryEvaluateFormulaSumAverageIfAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool average,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count is < 2 or > 3)
                return false;

            if (!TryResolveFormulaConditionalAggregateRangeArgument(arguments[0], rowOffset, colOffset, out var range, out var rangeError))
            {
                value = rangeError ?? ErrorValue.Value;
                return rangeError is not null;
            }

            if (!TryResolveFormulaConditionalAggregateScalarArgument(arguments[1], rowOffset, colOffset, out var criteriaValue))
                return false;

            if (criteriaValue is ErrorValue criteriaError)
            {
                value = criteriaError;
                return true;
            }

            RangeValue? aggregateRange = null;
            if (arguments.Count == 3)
            {
                if (!TryResolveFormulaConditionalAggregateRangeArgument(arguments[2], rowOffset, colOffset, out var resolvedAggregateRange, out var aggregateRangeError))
                {
                    value = aggregateRangeError ?? ErrorValue.Value;
                    return aggregateRangeError is not null;
                }

                aggregateRange = resolvedAggregateRange;
            }

            var criteria = FormulaConditionalCriteriaMatcher.Create(criteriaValue);
            var total = 0d;
            var count = 0;
            var flatCount = range.RowCount * range.ColCount;
            for (var i = 0; i < flatCount; i++)
            {
                if (!criteria.Matches(FormulaConditionalAggregateCellAtFlatIndex(range, i)))
                    continue;

                var aggregateValue = aggregateRange is not null
                    ? FormulaConditionalAggregateCellAtRelativeOffsetOrContext(
                        aggregateRange,
                        i / range.ColCount,
                        i % range.ColCount)
                    : FormulaConditionalAggregateCellAtFlatIndex(range, i);

                if (aggregateValue is ErrorValue aggregateError)
                {
                    value = aggregateError;
                    return true;
                }

                if (TryGetFormulaConditionalAggregateCellNumber(aggregateValue, out var number))
                {
                    total += number;
                    count++;
                    if (!double.IsFinite(total))
                    {
                        value = ErrorValue.Num;
                        return true;
                    }
                }
            }

            value = average
                ? count == 0
                    ? ErrorValue.DivByZero
                    : FormulaConditionalAggregateNumberResult(total / count)
                : FormulaConditionalAggregateNumberResult(total);
            return true;
        }

        private bool TryEvaluateFormulaCountIfAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count != 2)
                return false;

            if (!TryResolveFormulaConditionalAggregateRangeArgument(arguments[0], rowOffset, colOffset, out var range, out var rangeError))
            {
                value = rangeError ?? ErrorValue.Value;
                return rangeError is not null;
            }

            if (!TryResolveFormulaConditionalAggregateScalarArgument(arguments[1], rowOffset, colOffset, out var criteriaValue))
                return false;

            if (criteriaValue is ErrorValue criteriaError)
            {
                value = criteriaError;
                return true;
            }

            var criteria = FormulaConditionalCriteriaMatcher.Create(criteriaValue);
            var count = 0;
            for (var row = 0; row < range.RowCount; row++)
            {
                for (var col = 0; col < range.ColCount; col++)
                {
                    if (criteria.Matches(range.Cells[row, col]))
                        count++;
                }
            }

            value = new NumberValue(count);
            return true;
        }

        private bool TryEvaluateFormulaSumAverageIfsAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            bool average,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count < 3 || (arguments.Count - 1) % 2 != 0)
                return false;

            if (!TryResolveFormulaConditionalAggregateRangeArgument(arguments[0], rowOffset, colOffset, out var aggregateRange, out var aggregateRangeError))
            {
                value = aggregateRangeError ?? ErrorValue.Value;
                return aggregateRangeError is not null;
            }

            var pairCount = (arguments.Count - 1) / 2;
            if (!TryCreateFormulaConditionalCriteriaSet(
                    arguments,
                    firstCriteriaRangeIndex: 1,
                    pairCount,
                    aggregateRange,
                    rowOffset,
                    colOffset,
                    out var criteriaSet,
                    out var criteriaSetError))
            {
                value = criteriaSetError ?? ErrorValue.Value;
                return criteriaSetError is not null;
            }

            var total = 0d;
            var count = 0;
            for (var row = 0; row < aggregateRange.RowCount; row++)
            {
                for (var col = 0; col < aggregateRange.ColCount; col++)
                {
                    if (!criteriaSet.Includes(row, col))
                        continue;

                    var aggregateValue = aggregateRange.Cells[row, col];
                    if (aggregateValue is ErrorValue aggregateError)
                    {
                        value = aggregateError;
                        return true;
                    }

                    if (TryGetFormulaConditionalAggregateCellNumber(aggregateValue, out var number))
                    {
                        total += number;
                        count++;
                        if (!double.IsFinite(total))
                        {
                            value = ErrorValue.Num;
                            return true;
                        }
                    }
                }
            }

            value = average
                ? count == 0
                    ? ErrorValue.DivByZero
                    : FormulaConditionalAggregateNumberResult(total / count)
                : FormulaConditionalAggregateNumberResult(total);
            return true;
        }

        private bool TryEvaluateFormulaCountIfsAggregate(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (arguments.Count < 2 || arguments.Count % 2 != 0)
                return false;

            var pairCount = arguments.Count / 2;
            if (!TryCreateFormulaConditionalCriteriaSet(
                    arguments,
                    firstCriteriaRangeIndex: 0,
                    pairCount,
                    requiredShape: null,
                    rowOffset,
                    colOffset,
                    out var criteriaSet,
                    out var criteriaSetError))
            {
                value = criteriaSetError ?? ErrorValue.Value;
                return criteriaSetError is not null;
            }

            var count = 0;
            var shapeRange = criteriaSet.ShapeRange;
            for (var row = 0; row < shapeRange.RowCount; row++)
            {
                for (var col = 0; col < shapeRange.ColCount; col++)
                {
                    if (criteriaSet.Includes(row, col))
                        count++;
                }
            }

            value = new NumberValue(count);
            return true;
        }

        private bool TryCreateFormulaConditionalCriteriaSet(
            IReadOnlyList<ConditionalFormulaAggregateArgument> arguments,
            int firstCriteriaRangeIndex,
            int pairCount,
            RangeValue? requiredShape,
            int rowOffset,
            int colOffset,
            out FormulaConditionalCriteriaSet criteriaSet,
            out ErrorValue? error)
        {
            criteriaSet = default;
            error = null;
            var pairs = new FormulaConditionalCriteriaPair[pairCount];
            var shapeRange = requiredShape;

            for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                var rangeIndex = firstCriteriaRangeIndex + pairIndex * 2;
                var criteriaIndex = rangeIndex + 1;

                if (!TryResolveFormulaConditionalAggregateRangeArgument(
                        arguments[rangeIndex],
                        rowOffset,
                        colOffset,
                        out var criteriaRange,
                        out error))
                {
                    return false;
                }

                shapeRange ??= criteriaRange;
                if (!FormulaConditionalAggregateSameShape(shapeRange, criteriaRange))
                {
                    error = ErrorValue.Value;
                    return false;
                }

                if (!TryResolveFormulaConditionalAggregateScalarArgument(
                        arguments[criteriaIndex],
                        rowOffset,
                        colOffset,
                        out var criteriaValue))
                {
                    return false;
                }

                if (criteriaValue is ErrorValue criteriaError)
                {
                    error = criteriaError;
                    return false;
                }

                pairs[pairIndex] = new FormulaConditionalCriteriaPair(
                    criteriaRange,
                    FormulaConditionalCriteriaMatcher.Create(criteriaValue));
            }

            if (shapeRange is null)
                return false;

            criteriaSet = new FormulaConditionalCriteriaSet(shapeRange, pairs);
            return true;
        }

        private bool TryResolveFormulaConditionalAggregateRangeArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out RangeValue range,
            out ErrorValue? error)
        {
            range = default!;
            error = null;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    error = argument.Literal is ErrorValue literalError ? literalError : ErrorValue.Value;
                    return false;
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    range = SingleFormulaConditionalAggregateRange(targetSheet.GetValue(row, col), row, col, targetSheet.Name);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Range:
                    return TryMaterializeFormulaAggregateArgumentRange(argument, rowOffset, colOffset, out range);
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue)
                        return false;

                    if (argument.Operand.Value.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                        return TryMaterializeFormulaReferenceRange(argument.Operand.Value, rowOffset, colOffset, out range);

                    if (!TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out var value))
                        return false;

                    if (value is ErrorValue operandError)
                    {
                        error = operandError;
                        return false;
                    }

                    if (value is RangeValue resolvedRange)
                    {
                        range = resolvedRange;
                        return true;
                    }

                    error = ErrorValue.Value;
                    return false;
                default:
                    return false;
            }
        }

        private bool TryResolveFormulaConditionalAggregateScalarArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    value = argument.Literal ?? BlankValue.Instance;
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    value = targetSheet.GetValue(row, col);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Range:
                    if (!TryMaterializeFormulaAggregateArgumentRange(argument, rowOffset, colOffset, out var range))
                        return false;

                    value = FormulaConditionalAggregateScalarFromRange(range);
                    return true;
                case ConditionalFormulaAggregateArgumentKind.Operand:
                    if (!argument.Operand.HasValue)
                        return false;

                    if (argument.Operand.Value.Kind == ConditionalFormulaOperandKind.ReferenceRange)
                    {
                        if (!TryMaterializeFormulaReferenceRange(argument.Operand.Value, rowOffset, colOffset, out var referenceRange))
                            return false;

                        value = FormulaConditionalAggregateScalarFromRange(referenceRange);
                        return true;
                    }

                    if (!TryResolveFormulaOperand(argument.Operand.Value, rowOffset, colOffset, out value))
                        return false;

                    if (value is RangeValue resolvedRange)
                        value = FormulaConditionalAggregateScalarFromRange(resolvedRange);

                    return true;
                default:
                    return false;
            }
        }

        private ScalarValue FormulaConditionalAggregateCellAtRelativeOffsetOrContext(
            RangeValue range,
            int rowOffset,
            int colOffset)
        {
            if (rowOffset < range.RowCount && colOffset < range.ColCount)
                return range.Cells[rowOffset, colOffset];

            var targetRow = range.StartRow + (ulong)rowOffset;
            var targetCol = range.StartCol + (ulong)colOffset;
            if (targetRow > CellAddress.MaxRow || targetCol > CellAddress.MaxCol)
                return ErrorValue.Ref;

            var targetSheet = string.IsNullOrEmpty(range.SheetName)
                ? sheet
                : workbook.GetSheet(range.SheetName);
            return targetSheet is null
                ? ErrorValue.Ref
                : targetSheet.GetValue((uint)targetRow, (uint)targetCol);
        }

        private static ScalarValue FormulaConditionalAggregateScalarFromRange(RangeValue range) =>
            range.RowCount == 1 && range.ColCount == 1
                ? range.Cells[0, 0]
                : range;

        private static RangeValue SingleFormulaConditionalAggregateRange(
            ScalarValue value,
            uint row,
            uint col,
            string? sheetName) =>
            new(new[,] { { value } }, row, col) { SheetName = sheetName };

        private static ScalarValue FormulaConditionalAggregateCellAtFlatIndex(RangeValue range, int index)
        {
            var row = index / range.ColCount;
            var col = index - row * range.ColCount;
            return range.Cells[row, col];
        }

        private static bool FormulaConditionalAggregateSameShape(RangeValue left, RangeValue right) =>
            left.RowCount == right.RowCount && left.ColCount == right.ColCount;

        private static bool TryGetFormulaConditionalAggregateCellNumber(ScalarValue value, out double number)
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
                    number = 0d;
                    return false;
            }
        }

        private static ScalarValue FormulaConditionalAggregateNumberResult(double value) =>
            double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

        private readonly record struct FormulaConditionalCriteriaPair(
            RangeValue Range,
            FormulaConditionalCriteriaMatcher Criteria);

        private readonly record struct FormulaConditionalCriteriaSet(
            RangeValue ShapeRange,
            IReadOnlyList<FormulaConditionalCriteriaPair> Pairs)
        {
            public bool Includes(int row, int col)
            {
                for (var i = 0; i < Pairs.Count; i++)
                {
                    var pair = Pairs[i];
                    if (!pair.Criteria.Matches(pair.Range.Cells[row, col]))
                        return false;
                }

                return true;
            }
        }

        private enum FormulaConditionalCriteriaMatcherKind : byte
        {
            AlwaysFalse,
            NumberEquals,
            BoolEquals,
            TextEquals,
            NumericOrTextEquals,
            WildcardText,
            NumericComparison,
            TextComparison,
            WildcardComparison
        }

        private enum FormulaConditionalCriteriaComparisonOp : byte
        {
            None,
            GreaterThan,
            GreaterThanOrEqual,
            LessThan,
            LessThanOrEqual,
            Equal,
            NotEqual
        }

        private readonly struct FormulaConditionalCriteriaMatcher
        {
            private readonly FormulaConditionalCriteriaMatcherKind _kind;
            private readonly FormulaConditionalCriteriaComparisonOp _op;
            private readonly string _text;
            private readonly double _number;
            private readonly bool _bool;

            private FormulaConditionalCriteriaMatcher(
                FormulaConditionalCriteriaMatcherKind kind,
                FormulaConditionalCriteriaComparisonOp op = FormulaConditionalCriteriaComparisonOp.None,
                string? text = null,
                double number = 0d,
                bool boolean = false)
            {
                _kind = kind;
                _op = op;
                _text = text ?? string.Empty;
                _number = number;
                _bool = boolean;
            }

            public static FormulaConditionalCriteriaMatcher Create(ScalarValue criteria)
            {
                if (criteria is BlankValue)
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.TextEquals, text: string.Empty);

                if (criteria is NumberValue number)
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.NumberEquals, number: number.Value);

                if (criteria is DateTimeValue dateTime)
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.NumberEquals, number: dateTime.Value);

                if (criteria is BoolValue boolean)
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.BoolEquals, boolean: boolean.Value);

                if (criteria is not TextValue text)
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.AlwaysFalse);

                var criteriaText = text.Value;
                if (TrySplitFormulaConditionalCriteriaComparison(criteriaText, out var op, out var rhs))
                {
                    if (TryParseFormulaConditionalCriteriaNumber(rhs, out var rhsNumber))
                    {
                        return new FormulaConditionalCriteriaMatcher(
                            FormulaConditionalCriteriaMatcherKind.NumericComparison,
                            op,
                            number: rhsNumber);
                    }

                    return IsFormulaConditionalWildcardCriteria(rhs) &&
                        op is FormulaConditionalCriteriaComparisonOp.Equal or FormulaConditionalCriteriaComparisonOp.NotEqual
                            ? new FormulaConditionalCriteriaMatcher(
                                FormulaConditionalCriteriaMatcherKind.WildcardComparison,
                                op,
                                rhs)
                            : new FormulaConditionalCriteriaMatcher(
                                FormulaConditionalCriteriaMatcherKind.TextComparison,
                                op,
                                rhs);
                }

                if (IsFormulaConditionalWildcardCriteria(criteriaText))
                    return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.WildcardText, text: criteriaText);

                if (TryParseFormulaConditionalCriteriaNumber(criteriaText, out var numericCriteria))
                {
                    return new FormulaConditionalCriteriaMatcher(
                        FormulaConditionalCriteriaMatcherKind.NumericOrTextEquals,
                        text: criteriaText,
                        number: numericCriteria);
                }

                return new FormulaConditionalCriteriaMatcher(FormulaConditionalCriteriaMatcherKind.TextEquals, text: criteriaText);
            }

            public bool Matches(ScalarValue cellValue) => _kind switch
            {
                FormulaConditionalCriteriaMatcherKind.NumberEquals =>
                    TryGetFormulaConditionalCriteriaCellNumber(cellValue, out var cellNumber) &&
                    cellNumber == _number,

                FormulaConditionalCriteriaMatcherKind.BoolEquals =>
                    cellValue is BoolValue boolValue && boolValue.Value == _bool,

                FormulaConditionalCriteriaMatcherKind.TextEquals =>
                    string.Equals(
                        FormulaConditionalCriteriaComparableText(cellValue),
                        _text,
                        StringComparison.OrdinalIgnoreCase),

                FormulaConditionalCriteriaMatcherKind.NumericOrTextEquals =>
                    TryGetFormulaConditionalCriteriaCellNumber(cellValue, out var comparableNumber)
                        ? comparableNumber == _number
                        : string.Equals(
                            FormulaConditionalCriteriaComparableText(cellValue),
                            _text,
                            StringComparison.OrdinalIgnoreCase),

                FormulaConditionalCriteriaMatcherKind.WildcardText =>
                    cellValue is TextValue wildcardText &&
                    FormulaConditionalCriteriaWildcardMatch(wildcardText.Value, _text),

                FormulaConditionalCriteriaMatcherKind.NumericComparison =>
                    MatchesNumericComparison(cellValue),

                FormulaConditionalCriteriaMatcherKind.TextComparison =>
                    MatchesTextComparison(cellValue),

                FormulaConditionalCriteriaMatcherKind.WildcardComparison =>
                    MatchesWildcardComparison(cellValue),

                _ => false
            };

            private bool MatchesNumericComparison(ScalarValue cellValue)
            {
                if (!TryGetFormulaConditionalCriteriaCellNumber(cellValue, out var value))
                    return false;

                return _op switch
                {
                    FormulaConditionalCriteriaComparisonOp.GreaterThan => value > _number,
                    FormulaConditionalCriteriaComparisonOp.GreaterThanOrEqual => value >= _number,
                    FormulaConditionalCriteriaComparisonOp.LessThan => value < _number,
                    FormulaConditionalCriteriaComparisonOp.LessThanOrEqual => value <= _number,
                    FormulaConditionalCriteriaComparisonOp.Equal => value == _number,
                    FormulaConditionalCriteriaComparisonOp.NotEqual => value != _number,
                    _ => false
                };
            }

            private bool MatchesTextComparison(ScalarValue cellValue)
            {
                var cellText = cellValue is TextValue text ? text.Value : FormulaConditionalCriteriaToText(cellValue);
                var comparison = string.Compare(cellText, _text, StringComparison.OrdinalIgnoreCase);
                return _op switch
                {
                    FormulaConditionalCriteriaComparisonOp.GreaterThan => comparison > 0,
                    FormulaConditionalCriteriaComparisonOp.GreaterThanOrEqual => comparison >= 0,
                    FormulaConditionalCriteriaComparisonOp.LessThan => comparison < 0,
                    FormulaConditionalCriteriaComparisonOp.LessThanOrEqual => comparison <= 0,
                    FormulaConditionalCriteriaComparisonOp.Equal => comparison == 0,
                    FormulaConditionalCriteriaComparisonOp.NotEqual => comparison != 0,
                    _ => false
                };
            }

            private bool MatchesWildcardComparison(ScalarValue cellValue)
            {
                var matches = cellValue is TextValue text &&
                    FormulaConditionalCriteriaWildcardMatch(text.Value, _text);
                return _op == FormulaConditionalCriteriaComparisonOp.Equal
                    ? matches
                    : !matches;
            }
        }

        private static bool TrySplitFormulaConditionalCriteriaComparison(
            string criteria,
            out FormulaConditionalCriteriaComparisonOp op,
            out string rhs)
        {
            if (criteria.StartsWith(">=", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.GreaterThanOrEqual;
                rhs = criteria[2..];
                return true;
            }

            if (criteria.StartsWith("<=", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.LessThanOrEqual;
                rhs = criteria[2..];
                return true;
            }

            if (criteria.StartsWith("<>", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.NotEqual;
                rhs = criteria[2..];
                return true;
            }

            if (criteria.StartsWith(">", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.GreaterThan;
                rhs = criteria[1..];
                return true;
            }

            if (criteria.StartsWith("<", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.LessThan;
                rhs = criteria[1..];
                return true;
            }

            if (criteria.StartsWith("=", StringComparison.Ordinal))
            {
                op = FormulaConditionalCriteriaComparisonOp.Equal;
                rhs = criteria[1..];
                return true;
            }

            op = FormulaConditionalCriteriaComparisonOp.None;
            rhs = string.Empty;
            return false;
        }

        private static bool TryParseFormulaConditionalCriteriaNumber(string text, out double number)
        {
            var trimmed = text.Trim();
            var percentCount = 0;
            while (trimmed.EndsWith('%'))
            {
                percentCount++;
                trimmed = trimmed[..^1].TrimEnd();
            }

            if (percentCount > 0 &&
                double.TryParse(trimmed, NumberStyles.Any, FormulaTextScalarNumberCulture, out var percent))
            {
                for (var i = 0; i < percentCount; i++)
                    percent /= 100d;

                number = percent;
                return true;
            }

            if (double.TryParse(trimmed, NumberStyles.Any, FormulaTextScalarNumberCulture, out number))
                return true;

            if (TryParseFormulaExcelFakeLeapDayValueText(trimmed, out number))
                return true;

            if (DateTime.TryParse(trimmed, FormulaTextScalarNumberCulture, DateTimeStyles.None, out var dateTime))
            {
                number = IsFormulaConditionalCriteriaTimeOnlyText(trimmed)
                    ? dateTime.TimeOfDay.TotalDays
                    : FormulaDateToExcelSerial(dateTime);
                return true;
            }

            number = 0d;
            return false;
        }

        private static bool IsFormulaConditionalCriteriaTimeOnlyText(string text)
        {
            if (text.Contains('/') || text.Contains('-'))
                return false;

            if (FormulaDateTimeTextHasMonthNameRegex.IsMatch(text))
                return false;

            return text.Contains(':') ||
                FormulaDateTimeTextHasAmPmRegex.IsMatch(text);
        }

        private static string FormulaConditionalCriteriaComparableText(ScalarValue value) =>
            value switch
            {
                TextValue text => text.Value,
                BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                ErrorValue error => error.Code,
                _ when TryGetFormulaConditionalCriteriaCellNumber(value, out var number) =>
                    number.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };

        private static string FormulaConditionalCriteriaToText(ScalarValue value) =>
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

        private static bool TryGetFormulaConditionalCriteriaCellNumber(ScalarValue value, out double number)
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
                    number = 0d;
                    return false;
            }
        }

        private static bool IsFormulaConditionalWildcardCriteria(string criteria)
        {
            for (var i = 0; i < criteria.Length; i++)
            {
                var ch = criteria[i];
                if (ch is '*' or '?')
                    return true;

                if (ch == '~' &&
                    i + 1 < criteria.Length &&
                    criteria[i + 1] is '*' or '?' or '~')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FormulaConditionalCriteriaWildcardMatch(string text, string pattern)
        {
            try
            {
                return Regex.IsMatch(
                    text,
                    FormulaWildcardToRegexPattern(pattern, anchored: true),
                    RegexOptions.IgnoreCase,
                    FormulaTextSearchRegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private bool TryEvaluateFormulaDatabaseAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: 3 } arguments)
                return false;

            if (!TryResolveFormulaDatabaseAggregateRangeArgument(arguments[0], rowOffset, colOffset, out var database, out var databaseError))
            {
                value = databaseError ?? ErrorValue.Value;
                return databaseError is not null;
            }

            if (!TryResolveFormulaDatabaseAggregateScalarArgument(arguments[1], rowOffset, colOffset, out var field, out var fieldError))
            {
                value = fieldError ?? ErrorValue.Value;
                return fieldError is not null;
            }

            if (!TryResolveFormulaDatabaseAggregateRangeArgument(arguments[2], rowOffset, colOffset, out var criteria, out var criteriaError))
            {
                value = criteriaError ?? ErrorValue.Value;
                return criteriaError is not null;
            }

            if (!TryResolveFormulaDatabaseAggregateFieldColumn(database, field, out var fieldColumn))
            {
                value = ErrorValue.Value;
                return true;
            }

            if (!TryCollectFormulaDatabaseAggregateMatches(database, fieldColumn, criteria, out var matches, out var matchError))
            {
                value = matchError ?? ErrorValue.Value;
                return matchError is not null;
            }

            value = operand.AggregateKind switch
            {
                ConditionalFormulaAggregateKind.DCount => new NumberValue(FormulaDatabaseAggregateNumericValues(matches).Count),
                ConditionalFormulaAggregateKind.DCountA => new NumberValue(FormulaDatabaseAggregateNonBlankCount(matches)),
                _ => FormulaDatabaseAggregateNumberResult(
                    operand.AggregateKind,
                    FormulaDatabaseAggregateNumericValues(matches))
            };

            return true;
        }

        private bool TryResolveFormulaDatabaseAggregateRangeArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out RangeValue range,
            out ErrorValue? error)
        {
            if (!TryResolveFormulaConditionalAggregateRangeArgument(argument, rowOffset, colOffset, out range, out error))
                return false;

            if (!FormulaDatabaseAggregateIsSameSheet(range) ||
                range.RowCount < 1 ||
                range.ColCount < 1)
            {
                error = ErrorValue.Value;
                return false;
            }

            return true;
        }

        private bool TryResolveFormulaDatabaseAggregateScalarArgument(
            ConditionalFormulaAggregateArgument argument,
            int rowOffset,
            int colOffset,
            out ScalarValue value,
            out ErrorValue? error)
        {
            error = null;
            if (!TryResolveFormulaConditionalAggregateScalarArgument(argument, rowOffset, colOffset, out value))
                return false;

            if (value is ErrorValue scalarError)
            {
                error = scalarError;
                return false;
            }

            if (value is RangeValue)
            {
                error = ErrorValue.Value;
                return false;
            }

            return true;
        }

        private bool FormulaDatabaseAggregateIsSameSheet(RangeValue range) =>
            string.IsNullOrEmpty(range.SheetName) ||
            string.Equals(range.SheetName, sheet.Name, StringComparison.OrdinalIgnoreCase);

        private static bool TryResolveFormulaDatabaseAggregateFieldColumn(
            RangeValue database,
            ScalarValue field,
            out int fieldColumn)
        {
            fieldColumn = 0;
            if (TryGetFormulaDatabaseAggregateFieldIndex(field, out var fieldIndex))
            {
                if (fieldIndex < 1 || fieldIndex > database.ColCount)
                    return false;

                fieldColumn = fieldIndex - 1;
                return true;
            }

            if (!TryGetFormulaDatabaseAggregateHeaderText(field, out var fieldName))
                return false;

            return TryFindFormulaDatabaseAggregateHeaderColumn(database, fieldName, out fieldColumn);
        }

        private static bool TryGetFormulaDatabaseAggregateFieldIndex(ScalarValue value, out int fieldIndex)
        {
            if (TryGetFormulaConditionalAggregateCellNumber(value, out var number) &&
                double.IsFinite(number) &&
                number >= int.MinValue &&
                number <= int.MaxValue)
            {
                fieldIndex = (int)number;
                return true;
            }

            fieldIndex = 0;
            return false;
        }

        private static bool TryFindFormulaDatabaseAggregateHeaderColumn(
            RangeValue database,
            string headerText,
            out int column)
        {
            for (var currentColumn = 0; currentColumn < database.ColCount; currentColumn++)
            {
                if (TryGetFormulaDatabaseAggregateHeaderText(database.Cells[0, currentColumn], out var candidate) &&
                    string.Equals(candidate, headerText, StringComparison.OrdinalIgnoreCase))
                {
                    column = currentColumn;
                    return true;
                }
            }

            column = 0;
            return false;
        }

        private static bool TryGetFormulaDatabaseAggregateHeaderText(ScalarValue value, out string text)
        {
            text = ValueText(value);
            return text.Length > 0;
        }

        private static bool TryCollectFormulaDatabaseAggregateMatches(
            RangeValue database,
            int fieldColumn,
            RangeValue criteria,
            out List<ScalarValue> matches,
            out ErrorValue? error)
        {
            matches = new List<ScalarValue>();
            error = null;

            if (database.RowCount < 1 ||
                criteria.RowCount < 2 ||
                criteria.ColCount < 1 ||
                fieldColumn < 0 ||
                fieldColumn >= database.ColCount)
            {
                error = ErrorValue.Value;
                return false;
            }

            if (!TryCreateFormulaDatabaseAggregateCriteriaRows(database, criteria, out var criteriaRows))
                return true;

            for (var dataRow = 1; dataRow < database.RowCount; dataRow++)
            {
                var rowMatches = false;
                for (var criteriaRowIndex = 0; criteriaRowIndex < criteriaRows.Count; criteriaRowIndex++)
                {
                    if (!criteriaRows[criteriaRowIndex].Matches(database, dataRow))
                        continue;

                    rowMatches = true;
                    break;
                }

                if (!rowMatches)
                    continue;

                var value = database.Cells[dataRow, fieldColumn];
                if (value is ErrorValue valueError)
                {
                    error = valueError;
                    return false;
                }

                matches.Add(value);
            }

            return true;
        }

        private static bool TryCreateFormulaDatabaseAggregateCriteriaRows(
            RangeValue database,
            RangeValue criteria,
            out List<FormulaDatabaseAggregateCriteriaRow> criteriaRows)
        {
            criteriaRows = new List<FormulaDatabaseAggregateCriteriaRow>(criteria.RowCount - 1);
            for (var criteriaRow = 1; criteriaRow < criteria.RowCount; criteriaRow++)
            {
                var pairs = new List<FormulaDatabaseAggregateCriteriaPair>();
                var impossibleRow = false;
                for (var criteriaColumn = 0; criteriaColumn < criteria.ColCount; criteriaColumn++)
                {
                    var criteriaValue = criteria.Cells[criteriaRow, criteriaColumn];
                    if (FormulaDatabaseAggregateCriteriaCellIsBlank(criteriaValue))
                        continue;

                    if (!TryGetFormulaDatabaseAggregateHeaderText(criteria.Cells[0, criteriaColumn], out var criteriaHeader))
                        continue;

                    if (!TryFindFormulaDatabaseAggregateHeaderColumn(database, criteriaHeader, out var databaseColumn))
                    {
                        impossibleRow = true;
                        break;
                    }

                    pairs.Add(new FormulaDatabaseAggregateCriteriaPair(
                        databaseColumn,
                        FormulaConditionalCriteriaMatcher.Create(criteriaValue)));
                }

                if (!impossibleRow)
                    criteriaRows.Add(new FormulaDatabaseAggregateCriteriaRow(pairs));
            }

            return true;
        }

        private static bool FormulaDatabaseAggregateCriteriaCellIsBlank(ScalarValue value) =>
            value is BlankValue ||
            value is TextValue text && text.Value.Length == 0;

        private static List<double> FormulaDatabaseAggregateNumericValues(IReadOnlyList<ScalarValue> values)
        {
            var numbers = new List<double>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                if (TryGetFormulaConditionalAggregateCellNumber(values[i], out var number))
                    numbers.Add(number);
            }

            return numbers;
        }

        private static int FormulaDatabaseAggregateNonBlankCount(IReadOnlyList<ScalarValue> values)
        {
            var count = 0;
            for (var i = 0; i < values.Count; i++)
            {
                if (!FormulaDatabaseAggregateCriteriaCellIsBlank(values[i]))
                    count++;
            }

            return count;
        }

        private static ScalarValue FormulaDatabaseAggregateNumberResult(
            ConditionalFormulaAggregateKind aggregateKind,
            List<double> numbers)
        {
            return aggregateKind switch
            {
                ConditionalFormulaAggregateKind.DSum => FormulaConditionalAggregateNumberResult(numbers.Sum()),
                ConditionalFormulaAggregateKind.DAverage when numbers.Count > 0 => FormulaConditionalAggregateNumberResult(numbers.Average()),
                ConditionalFormulaAggregateKind.DAverage => ErrorValue.DivByZero,
                ConditionalFormulaAggregateKind.DMax when numbers.Count > 0 => FormulaConditionalAggregateNumberResult(numbers.Max()),
                ConditionalFormulaAggregateKind.DMax => ErrorValue.Num,
                ConditionalFormulaAggregateKind.DMin when numbers.Count > 0 => FormulaConditionalAggregateNumberResult(numbers.Min()),
                ConditionalFormulaAggregateKind.DMin => ErrorValue.Num,
                ConditionalFormulaAggregateKind.DProduct => FormulaConditionalAggregateNumberResult(
                    numbers.Count == 0 ? 1d : numbers.Aggregate(1d, (product, number) => product * number)),
                ConditionalFormulaAggregateKind.DStdDev when numbers.Count > 1 =>
                    FormulaConditionalAggregateNumberResult(StandardDeviationFormulaNumbers(numbers, sample: true)),
                ConditionalFormulaAggregateKind.DStdDev => ErrorValue.DivByZero,
                ConditionalFormulaAggregateKind.DStdDevP when numbers.Count > 0 =>
                    FormulaConditionalAggregateNumberResult(StandardDeviationFormulaNumbers(numbers, sample: false)),
                ConditionalFormulaAggregateKind.DStdDevP => ErrorValue.DivByZero,
                ConditionalFormulaAggregateKind.DVar when numbers.Count > 1 =>
                    FormulaConditionalAggregateNumberResult(VarianceFormulaNumbers(numbers, sample: true)),
                ConditionalFormulaAggregateKind.DVar => ErrorValue.DivByZero,
                ConditionalFormulaAggregateKind.DVarP when numbers.Count > 0 =>
                    FormulaConditionalAggregateNumberResult(VarianceFormulaNumbers(numbers, sample: false)),
                ConditionalFormulaAggregateKind.DVarP => ErrorValue.DivByZero,
                _ => ErrorValue.Value
            };
        }

        private readonly record struct FormulaDatabaseAggregateCriteriaPair(
            int DatabaseColumn,
            FormulaConditionalCriteriaMatcher Criteria);

        private readonly record struct FormulaDatabaseAggregateCriteriaRow(
            IReadOnlyList<FormulaDatabaseAggregateCriteriaPair> Pairs)
        {
            public bool Matches(RangeValue database, int dataRow)
            {
                for (var i = 0; i < Pairs.Count; i++)
                {
                    var pair = Pairs[i];
                    if (!pair.Criteria.Matches(database.Cells[dataRow, pair.DatabaseColumn]))
                        return false;
                }

                return true;
            }
        }

        private bool TryEvaluateFormulaAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (IsFormulaDatabaseAggregate(operand.AggregateKind))
                return TryEvaluateFormulaDatabaseAggregate(operand, rowOffset, colOffset, out value);

            if (IsFormulaConditionalAggregate(operand.AggregateKind))
                return TryEvaluateFormulaConditionalAggregate(operand, rowOffset, colOffset, out value);

            if (IsFormulaStatisticalSelectionAggregate(operand.AggregateKind))
                return TryEvaluateFormulaStatisticalSelectionAggregate(operand, rowOffset, colOffset, out value);

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
