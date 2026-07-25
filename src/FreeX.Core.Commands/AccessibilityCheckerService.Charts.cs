using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class AccessibilityCheckerService
{
    private static void AddChartIssues(List<AccessibilityIssue> issues, Workbook workbook, Sheet sheet)
    {
        foreach (var chart in sheet.Charts)
        {
            if (!chart.IsVisible)
                continue;

            if (string.IsNullOrWhiteSpace(chart.Title))
            {
                // R90-app-accessibility-checker-5-3: a chart with "Show chart title" turned off has
                // no visible Title, but the user may have set real Alt Text via Excel's "Edit Alt
                // Text" pane -- that alt text is exactly what the Accessibility Checker is meant to
                // find, per ChartModel.AltTextTitle/AltTextDescription's own doc comment. Only flag
                // MissingTitle when there is no accessible alt text either.
                if (string.IsNullOrWhiteSpace(chart.AltTextTitle) && string.IsNullOrWhiteSpace(chart.AltTextDescription))
                {
                    issues.Add(new AccessibilityIssue(
                        AccessibilityIssueKind.ChartMissingTitle,
                        sheet.Id,
                        sheet.Name,
                        FormatRange(chart.DataRange),
                        "Chart is missing a title."));
                }
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
}
