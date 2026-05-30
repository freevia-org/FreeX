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
    LowContrastCellText,
    LowContrastChartText
}

public sealed record AccessibilityIssue(
    AccessibilityIssueKind Kind,
    SheetId SheetId,
    string SheetName,
    string Location,
    string Message);

public static class AccessibilityCheckerService
{
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

                AddLowContrastChartTextIssues(issues, workbook, sheet, chart);
            }
        }

        return issues;
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
        foreach (var (address, cell) in sheet.GetUsedCells())
        {
            if (cell.Value is not TextValue text || string.IsNullOrWhiteSpace(text.Value))
                continue;

            var style = GetEffectiveContrastStyle(workbook, sheet, address, cell);
            var background = style.FillColor ?? CellColor.White;
            var minimumContrastRatio = MinimumTextContrastRatio(style);
            if (ContrastRatio(style.FontColor, background) >= minimumContrastRatio)
                continue;

            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.LowContrastCellText,
                sheet.Id,
                sheet.Name,
                address.ToA1(),
                $"Cell text should have at least {minimumContrastRatio:0.0}:1 contrast against its fill."));
        }
    }

    private static CellStyle GetEffectiveContrastStyle(Workbook workbook, Sheet sheet, CellAddress address, Cell cell)
    {
        var style = workbook.GetStyle(cell.StyleId);
        foreach (var rule in sheet.ConditionalFormats
                     .Where(rule => rule.FormatIfTrue is not null && rule.AppliesTo.Contains(address))
                     .OrderBy(rule => rule.Priority))
        {
            if (!IsConditionalFormatTrue(rule, cell.Value))
                continue;

            style = rule.FormatIfTrue!;
            if (rule.StopIfTrue)
                break;
        }

        return style;
    }

    private static bool IsConditionalFormatTrue(ConditionalFormat rule, ScalarValue value) =>
        rule.RuleType switch
        {
            CfRuleType.NoBlanks => value is not BlankValue,
            CfRuleType.Blanks => value is BlankValue,
            CfRuleType.Errors => value is ErrorValue,
            CfRuleType.NoErrors => value is not ErrorValue,
            CfRuleType.ContainsText => ValueText(value).Contains(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.NotContainsText => !ValueText(value).Contains(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.BeginsWith => ValueText(value).StartsWith(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.EndsWith => ValueText(value).EndsWith(rule.TextRuleText ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            CfRuleType.CellValue => IsCellValueRuleTrue(rule, value),
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
                continue;

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
        var usedCells = sheet.GetUsedCells().Keys.ToList();
        if (usedCells.Count == 0)
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

        foreach (var row in usedCells
                     .Select(address => address.Row)
                     .Where(sheet.IsRowEffectivelyHidden)
                     .Distinct()
                     .Order())
        {
            issues.Add(new AccessibilityIssue(
                AccessibilityIssueKind.HiddenRowWithContent,
                sheet.Id,
                sheet.Name,
                $"{row}:{row}",
                "Hidden rows with content may not be available to assistive technologies."));
        }

        foreach (var col in usedCells
                     .Select(address => address.Col)
                     .Where(sheet.IsColEffectivelyHidden)
                     .Distinct()
                     .Order())
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
}
