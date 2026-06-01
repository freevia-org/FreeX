using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum AccessibilityIssueKind
{
    MergedCells,
    MissingAltText,
    GenericAltText,
    ChartMissingTitle,
    GenericChartTitle,
    HyperlinkDisplayTextIsUrl,
    DefaultWorksheetName,
    HiddenSheetWithContent,
    HiddenRowWithContent,
    HiddenColumnWithContent,
    TableMissingHeaderText,
    TableDefaultHeaderText,
    TableDuplicateHeaderText,
    TableMissingHeaderRow,
    ChartMissingAxisTitle,
    GenericChartAxisTitle,
    LowContrastCellText,
    LowContrastChartText,
    LowContrastObjectText
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static class AccessibilityCheckerService
{
    private const double DefaultObjectTextFontSize = 11d;

    public static IReadOnlyList<AccessibilityIssue> FindIssues(Workbook workbook)
    {
        var issues = new List<AccessibilityIssue>();
        foreach (var sheet in workbook.Sheets)
        {
            if (AccessibilityTextRules.IsDefaultWorksheetName(sheet.Name))
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.DefaultWorksheetName,
                    sheet.Id,
                    sheet.Name,
                    sheet.Name,
                    "Worksheet tab names should describe their contents."));
            }

            AddHiddenContentIssues(issues, sheet);
            AddStructuredTableIssues(issues, sheet);
            AddLowContrastCellTextIssues(issues, workbook, sheet);

            foreach (var range in sheet.MergedRegions)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.MergedCells,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(range),
                    "Merged cells can make worksheet navigation harder for assistive technologies."));
            }

            foreach (var picture in sheet.Pictures)
            {
                if (!picture.IsVisible)
                    continue;

                AddAltTextIssue(issues, sheet, picture.Anchor, "Picture", picture.AltText);
            }

            foreach (var shape in sheet.DrawingShapes)
            {
                if (!shape.IsVisible)
                    continue;

                AddAltTextIssue(issues, sheet, shape.Anchor, "Shape", shape.AltText);
            }

            foreach (var textBox in sheet.TextBoxes)
            {
                if (!textBox.IsVisible)
                    continue;

                AddAltTextIssue(issues, sheet, textBox.Anchor, "Text box", textBox.AltText);
                AddLowContrastTextBoxTextIssue(issues, workbook, sheet, textBox);
            }

            foreach (var (address, target) in sheet.Hyperlinks)
            {
                if (sheet.GetCell(address)?.Value is TextValue displayText &&
                    AccessibilityTextRules.IsDescriptiveHyperlinkText(displayText.Value, target))
                    continue;

                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HyperlinkDisplayTextIsUrl,
                    sheet.Id,
                    sheet.Name,
                    address.ToA1(),
                    "Hyperlink display text should describe the destination."));
            }

            foreach (var chart in sheet.Charts)
            {
                if (!chart.IsVisible)
                    continue;

                if (string.IsNullOrWhiteSpace(chart.Title))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.ChartMissingTitle,
                        sheet.Id,
                        sheet.Name,
                        FormatRange(chart.DataRange),
                        "Chart is missing a title."));
                    continue;
                }

                if (AccessibilityTextRules.IsGenericChartTitle(chart.Title))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.GenericChartTitle,
                        sheet.Id,
                        sheet.Name,
                        FormatRange(chart.DataRange),
                        "Chart title should describe the chart."));
                }

                AddChartAxisTitleIssues(issues, sheet, chart);
                AddLowContrastChartTextIssues(issues, workbook, sheet, chart);
            }
        }

        return issues;
    }

    private static void AddChartAxisTitleIssues(List<AccessibilityIssue> issues, Sheet sheet, ChartModel chart)
    {
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
            return;

        AddChartAxisTitleIssue(issues, sheet, chart, "X-axis", chart.XAxisTitle, chart.HideXAxis);
        AddChartAxisTitleIssue(issues, sheet, chart, "Y-axis", chart.YAxisTitle, chart.HideYAxis);
    }

    private static void AddChartAxisTitleIssue(
        List<AccessibilityIssue> issues,
        Sheet sheet,
        ChartModel chart,
        string axisName,
        string? axisTitle,
        bool axisHidden)
    {
        if (axisHidden)
            return;

        if (string.IsNullOrWhiteSpace(axisTitle))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.ChartMissingAxisTitle,
                sheet.Id,
                sheet.Name,
                FormatRange(chart.DataRange),
                $"Chart {axisName} is missing a title."));
            return;
        }

        if (AccessibilityTextRules.IsGenericChartAxisTitle(axisTitle))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.GenericChartAxisTitle,
                sheet.Id,
                sheet.Name,
                FormatRange(chart.DataRange),
                $"Chart {axisName} title should describe the axis."));
        }
    }

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
            AddLowContrastChartTextIssue(
                issues,
                sheet,
                chart,
                "Data label text",
                "Data labels",
                chart.ResolveDataLabelTextColor(workbook.Theme) ?? defaultText,
                chart.ResolveDataLabelFillColor(workbook.Theme) ?? plotBackground,
                chart.DataLabelFontSize);
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
            var contrast = GetCellContrastCheck(style, ref contrastCache);
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
        var background = textBox.GetEffectiveFillColor(workbook.Theme, CellColor.White);
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
        ref Dictionary<CellStyle, CellContrastCheck>? contrastCache)
    {
        contrastCache ??= new Dictionary<CellStyle, CellContrastCheck>(CellStyleReferenceComparer.Instance);
        if (!contrastCache.TryGetValue(style, out var check))
        {
            var minimumContrastRatio = MinimumTextContrastRatio(style);
            check = new CellContrastCheck(
                minimumContrastRatio,
                HasSufficientCellTextContrast(style, minimumContrastRatio));
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

    private static bool TryCreateFormulaComparison(string? formulaText, out ConditionalFormulaComparison comparison)
    {
        comparison = default;
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        try
        {
            var text = formulaText.Trim();
            var formula = text[0] == '=' ? text : "=" + text;
            var ast = new Parser(new Lexer(formula).Tokenize()).Parse();
            return TryCreateFormulaComparison(ast, out comparison);
        }
        catch
        {
            return false;
        }
    }

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
            default:
                return false;
        }
    }

    private static ConditionalFormulaOperand LiteralFormulaOperand(ScalarValue value) =>
        new(ConditionalFormulaOperandKind.Literal, value, 0, 0, true, true, null);

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
            DateTimeValue => true,
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

    private static void AddStructuredTableIssues(List<AccessibilityIssue> issues, Sheet sheet)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.HeaderRowCount.GetValueOrDefault(1) <= 0)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.TableMissingHeaderRow,
                    sheet.Id,
                    sheet.Name,
                    FormatRange(table.Range),
                    "Tables should include a header row."));
                continue;
            }

            var seenHeaderTexts = new Dictionary<string, CellAddress>(StringComparer.OrdinalIgnoreCase);
            var startCol = (int)table.Range.Start.Col;
            var endCol = (int)table.Range.End.Col;
            for (var col = startCol; col <= endCol; col++)
            {
                var columnOffset = col - startCol;
                var columnName = columnOffset < table.Columns.Count ? table.Columns[columnOffset].Name : null;
                var headerAddress = new CellAddress(sheet.Id, table.Range.Start.Row, (uint)col);
                var headerText = ReadHeaderText(sheet, headerAddress, columnName);
                if (string.IsNullOrWhiteSpace(headerText))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableMissingHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should not be blank."));
                    continue;
                }

                if (AccessibilityTextRules.IsDefaultTableHeaderText(headerText))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableDefaultHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should describe the column contents."));
                    continue;
                }

                var normalizedHeaderText = NormalizeHeaderText(headerText);
                if (seenHeaderTexts.TryGetValue(normalizedHeaderText, out _))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.TableDuplicateHeaderText,
                        sheet.Id,
                        sheet.Name,
                        headerAddress.ToA1(),
                        "Table headers should be unique."));
                    continue;
                }

                seenHeaderTexts[normalizedHeaderText] = headerAddress;
            }
        }
    }

    private static void AddHiddenContentIssues(List<AccessibilityIssue> issues, Sheet sheet)
    {
        if (!sheet.IsHidden &&
            !sheet.IsVeryHidden &&
            sheet.HiddenRows.Count == 0 &&
            sheet.FilterHiddenRows.Count == 0 &&
            sheet.GroupHiddenRows.Count == 0 &&
            sheet.HiddenCols.Count == 0 &&
            sheet.GroupHiddenCols.Count == 0)
        {
            return;
        }

        var hasContent = false;
        HashSet<uint>? hiddenRows = null;
        HashSet<uint>? hiddenCols = null;
        foreach (var ((row, col), _) in sheet.GetOccupiedCellMap())
        {
            hasContent = true;
            if (sheet.IsRowEffectivelyHidden(row))
            {
                hiddenRows ??= [];
                hiddenRows.Add(row);
            }

            if (sheet.IsColEffectivelyHidden(col))
            {
                hiddenCols ??= [];
                hiddenCols.Add(col);
            }
        }

        if (!hasContent)
            return;

        if (sheet.IsHidden || sheet.IsVeryHidden)
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenSheetWithContent,
                sheet.Id,
                sheet.Name,
                sheet.Name,
                "Hidden sheets with content may not be available to assistive technologies."));
        }

        if (hiddenRows is not null)
        {
            var rows = hiddenRows.ToList();
            rows.Sort();
            foreach (var row in rows)
            {
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenRowWithContent,
                    sheet.Id,
                    sheet.Name,
                    $"{row}:{row}",
                    "Hidden rows with content may not be available to assistive technologies."));
            }
        }

        if (hiddenCols is not null)
        {
            var cols = hiddenCols.ToList();
            cols.Sort();
            foreach (var col in cols)
            {
                var name = CellAddress.NumberToColumnName(col);
                issues.Add(new AccessibilityIssue(
                    AccessibilityIssueKind.HiddenColumnWithContent,
                    sheet.Id,
                    sheet.Name,
                    $"{name}:{name}",
                    "Hidden columns with content may not be available to assistive technologies."));
            }
        }
    }

    private static AccessibilityIssue MissingAltText(Sheet sheet, CellAddress anchor, string objectType) => new(
        AccessibilityIssueKind.MissingAltText,
        sheet.Id,
        sheet.Name,
        anchor.ToA1(),
        $"{objectType} is missing alternate text.");

    private static void AddAltTextIssue(List<AccessibilityIssue> issues, Sheet sheet, CellAddress anchor, string objectType, string? altText)
    {
        if (string.IsNullOrWhiteSpace(altText))
        {
            issues.Add(MissingAltText(sheet, anchor, objectType));
            return;
        }

        if (AccessibilityTextRules.IsGenericAltText(altText))
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.GenericAltText,
                sheet.Id,
                sheet.Name,
                anchor.ToA1(),
                $"{objectType} alternate text should describe the object."));
        }
    }

    private static string? ReadHeaderText(Sheet sheet, CellAddress headerAddress, string? columnName)
    {
        if (sheet.GetCell(headerAddress) is { } cell)
            return ValueText(cell.Value);

        return columnName;
    }

    private static string NormalizeHeaderText(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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

    private static bool HasSufficientCellTextContrast(CellStyle style, double minimumContrastRatio)
    {
        var baseFill = style.FillColor ?? CellColor.White;
        if (ContrastRatio(style.FontColor, baseFill) < minimumContrastRatio)
            return false;

        if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            return true;

        var patternColor = style.FillPatternColor ?? CellColor.Black;
        if (TryGetGrayPatternOpacity(style.FillPatternStyle, out var opacity))
            return ContrastRatio(style.FontColor, Blend(patternColor, baseFill, opacity)) >= minimumContrastRatio;

        return ContrastRatio(style.FontColor, patternColor) >= minimumContrastRatio;
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

    private static string FormatRange(GridRange range) =>
        range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";

    private readonly record struct CellContrastCheck(double MinimumContrastRatio, bool HasSufficientContrast);

    private readonly record struct ConditionalFormulaComparison(
        ConditionalFormulaOperand Left,
        BinaryOperator Operator,
        ConditionalFormulaOperand Right);

    private readonly record struct ConditionalFormulaOperand(
        ConditionalFormulaOperandKind Kind,
        ScalarValue? Literal,
        uint Row,
        uint Col,
        bool IsRowAbsolute,
        bool IsColAbsolute,
        string? SheetName);

    private enum ConditionalFormulaOperandKind
    {
        Literal,
        Reference
    }

    private sealed class ConditionalFormatEvaluationCache(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells)
    {
        private readonly Dictionary<ConditionalFormat, Dictionary<string, int>> _valueCounts = new();
        private readonly Dictionary<ConditionalFormat, RangeAverage> _averages = new();
        private readonly Dictionary<ConditionalFormat, HashSet<CellAddress>?> _topBottomMatches = new();
        private readonly Dictionary<ConditionalFormat, ConditionalFormulaComparison?> _formulaComparisons = new();

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
            if (!TryGetFormulaComparison(rule, out var comparison))
                return false;

            var rowOffset = (int)address.Row - (int)rule.AppliesTo.Start.Row;
            var colOffset = (int)address.Col - (int)rule.AppliesTo.Start.Col;
            if (!TryResolveFormulaOperand(comparison.Left, rowOffset, colOffset, out var left) ||
                !TryResolveFormulaOperand(comparison.Right, rowOffset, colOffset, out var right))
            {
                return false;
            }

            if (left is ErrorValue || right is ErrorValue)
                return false;

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

        private bool TryGetFormulaComparison(ConditionalFormat rule, out ConditionalFormulaComparison comparison)
        {
            if (_formulaComparisons.TryGetValue(rule, out var cached))
            {
                comparison = cached.GetValueOrDefault();
                return cached.HasValue;
            }

            if (TryCreateFormulaComparison(rule.FormulaText, out comparison))
            {
                _formulaComparisons[rule] = comparison;
                return true;
            }

            _formulaComparisons[rule] = null;
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

            var row = ShiftFormulaRow(operand.Row, operand.IsRowAbsolute, rowOffset);
            var col = ShiftFormulaColumn(operand.Col, operand.IsColAbsolute, colOffset);
            if (!row.HasValue || !col.HasValue)
            {
                value = ErrorValue.Ref;
                return false;
            }

            var targetSheet = operand.SheetName is null ? sheet : workbook.GetSheet(operand.SheetName);
            if (targetSheet is null)
            {
                value = ErrorValue.Ref;
                return false;
            }

            value = targetSheet.GetValue(row.Value, col.Value);
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
