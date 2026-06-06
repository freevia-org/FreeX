using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
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

    private static bool IsFormulaPredicateOperand(FormulaNode ast) =>
        ast is (BooleanNode
            or CellRefNode
            or NumberNode)
            || ast is UnaryOpNode unary && IsFormulaUnaryArithmeticOperator(unary.Operator)
            || ast is BinaryOpNode binary && IsFormulaArithmeticOperator(binary.Operator)
            || ast is FunctionCallNode function && IsFormulaAggregateFunction(function.FunctionName);

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
            case FunctionCallNode function when TryCreateFormulaAggregateOperand(function, out operand):
                return true;
            default:
                return false;
        }
    }

    private static ConditionalFormulaOperand LiteralFormulaOperand(ScalarValue value) =>
        new(ConditionalFormulaOperandKind.Literal, value, 0, 0, true, true, null);

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

    private static bool TryCreateFormulaAggregateOperand(
        FunctionCallNode function,
        out ConditionalFormulaOperand operand)
    {
        operand = default;
        if (!TryGetFormulaAggregateKind(function.FunctionName, out var aggregateKind) ||
            function.Arguments.Count == 0)
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

    private static bool TryGetFormulaAggregateKind(
        string functionName,
        out ConditionalFormulaAggregateKind kind)
    {
        switch (functionName.ToUpperInvariant())
        {
            case "SUM":
                kind = ConditionalFormulaAggregateKind.Sum;
                return true;
            case "AVERAGE":
                kind = ConditionalFormulaAggregateKind.Average;
                return true;
            case "MIN":
                kind = ConditionalFormulaAggregateKind.Min;
                return true;
            case "MAX":
                kind = ConditionalFormulaAggregateKind.Max;
                return true;
            case "COUNT":
                kind = ConditionalFormulaAggregateKind.Count;
                return true;
            case "COUNTA":
                kind = ConditionalFormulaAggregateKind.CountA;
                return true;
            default:
                kind = default;
                return false;
        }
    }

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
        ConditionalFormulaUnary? Unary = null);

    private enum ConditionalFormulaOperandKind
    {
        Literal,
        Reference,
        Unary,
        Arithmetic,
        Aggregate
    }

    private sealed record ConditionalFormulaUnary(
        UnaryOperator Operator,
        ConditionalFormulaOperand Operand);

    private sealed record ConditionalFormulaArithmetic(
        BinaryOperator Operator,
        ConditionalFormulaOperand Left,
        ConditionalFormulaOperand Right);

    private enum ConditionalFormulaAggregateKind
    {
        Sum,
        Average,
        Min,
        Max,
        Count,
        CountA
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
        string? SheetName);

    private enum ConditionalFormulaAggregateArgumentKind
    {
        Literal,
        Reference,
        Range
    }

    private sealed class ConditionalFormatEvaluationCache(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells)
    {
        private readonly Dictionary<ConditionalFormat, Dictionary<string, int>> _valueCounts = new();
        private readonly Dictionary<ConditionalFormat, RangeAverage> _averages = new();
        private readonly Dictionary<ConditionalFormat, HashSet<CellAddress>?> _topBottomMatches = new();
        private readonly Dictionary<ConditionalFormat, ConditionalFormulaExpression?> _formulaExpressions = new();

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

            if (left is ErrorValue || right is ErrorValue)
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

        private bool TryEvaluateFormulaAggregate(
            ConditionalFormulaOperand operand,
            int rowOffset,
            int colOffset,
            out ScalarValue value)
        {
            value = ErrorValue.Value;
            if (operand.AggregateArguments is not { Count: > 0 } arguments)
                return false;

            var numericValues = new List<double>();
            var nonBlankCount = 0;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (!AppendFormulaAggregateValues(
                        arguments[i],
                        operand.AggregateKind,
                        rowOffset,
                        colOffset,
                        numericValues,
                        ref nonBlankCount))
                {
                    return false;
                }
            }

            value = operand.AggregateKind switch
            {
                ConditionalFormulaAggregateKind.Sum => new NumberValue(numericValues.Sum()),
                ConditionalFormulaAggregateKind.Average when numericValues.Count > 0 => new NumberValue(numericValues.Average()),
                ConditionalFormulaAggregateKind.Min when numericValues.Count > 0 => new NumberValue(numericValues.Min()),
                ConditionalFormulaAggregateKind.Max when numericValues.Count > 0 => new NumberValue(numericValues.Max()),
                ConditionalFormulaAggregateKind.Count => new NumberValue(numericValues.Count),
                ConditionalFormulaAggregateKind.CountA => new NumberValue(nonBlankCount),
                _ => ErrorValue.Value
            };

            return value is not ErrorValue && TryGetNumber(value, out var number) && double.IsFinite(number);
        }

        private bool AppendFormulaAggregateValues(
            ConditionalFormulaAggregateArgument argument,
            ConditionalFormulaAggregateKind aggregateKind,
            int rowOffset,
            int colOffset,
            List<double> numericValues,
            ref int nonBlankCount)
        {
            switch (argument.Kind)
            {
                case ConditionalFormulaAggregateArgumentKind.Literal:
                    return AppendFormulaAggregateValue(
                        argument.Literal ?? BlankValue.Instance,
                        aggregateKind,
                        isDirectArgument: true,
                        numericValues,
                        ref nonBlankCount);
                case ConditionalFormulaAggregateArgumentKind.Reference:
                    if (!TryResolveFormulaAggregateReference(argument, rowOffset, colOffset, out var targetSheet, out var row, out var col))
                        return false;

                    return AppendFormulaAggregateValue(
                        targetSheet.GetValue(row, col),
                        aggregateKind,
                        isDirectArgument: false,
                        numericValues,
                        ref nonBlankCount);
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
                                    ref nonBlankCount))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                default:
                    return false;
            }
        }

        private static bool AppendFormulaAggregateValue(
            ScalarValue value,
            ConditionalFormulaAggregateKind aggregateKind,
            bool isDirectArgument,
            List<double> numericValues,
            ref int nonBlankCount)
        {
            if (value is ErrorValue)
                return false;

            if (value is not BlankValue)
                nonBlankCount++;

            if (isDirectArgument &&
                IsFormulaNumericAggregate(aggregateKind) &&
                value is TextValue directText &&
                !double.TryParse(
                    directText.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
            {
                return false;
            }

            if (TryGetFormulaAggregateNumber(
                    value,
                    aggregateKind,
                    isDirectArgument,
                    out var number))
            {
                numericValues.Add(number);
            }

            return true;
        }

        private static bool TryGetFormulaAggregateNumber(
            ScalarValue value,
            ConditionalFormulaAggregateKind aggregateKind,
            bool isDirectArgument,
            out double number)
        {
            switch (value)
            {
                case NumberValue numeric:
                    number = numeric.Value;
                    break;
                case DateTimeValue dateTime:
                    number = dateTime.Value;
                    break;
                case BoolValue boolean when isDirectArgument:
                    number = boolean.Value ? 1 : 0;
                    break;
                case TextValue text when isDirectArgument &&
                    double.TryParse(
                        text.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed):
                    number = parsed;
                    break;
                default:
                    number = 0;
                    return false;
            }

            return double.IsFinite(number);
        }

        private static bool IsFormulaNumericAggregate(ConditionalFormulaAggregateKind aggregateKind) =>
            aggregateKind is
                ConditionalFormulaAggregateKind.Sum or
                ConditionalFormulaAggregateKind.Average or
                ConditionalFormulaAggregateKind.Min or
                ConditionalFormulaAggregateKind.Max;

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
