using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-app-accessibility-checker-5-3: <c>AddChartIssues</c> only ever inspected the chart's visible
/// <see cref="ChartModel.Title"/>, never <see cref="ChartModel.AltTextTitle"/>/
/// <see cref="ChartModel.AltTextDescription"/> -- so a chart with "Show chart title" turned off but
/// real Alt Text set via Excel's "Edit Alt Text" pane was always (incorrectly) flagged as missing a
/// title. These tests drive the real product entry point, <see cref="AccessibilityCheckerService.FindIssues"/>.
/// </summary>
public sealed class R90_AccessibilityChartAltTextTests
{
    private static GridRange MakeDataRange(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2));

    [Fact]
    public void FindIssues_DoesNotFlagChartWithNoVisibleTitle_WhenAltTextTitleIsSet()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = MakeDataRange(sheet.Id),
            Title = null,
            AltTextTitle = "Quarterly sales by region"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().NotContain(i => i.Kind == AccessibilityIssueKind.ChartMissingTitle);
    }

    [Fact]
    public void FindIssues_DoesNotFlagChartWithNoVisibleTitle_WhenAltTextDescriptionIsSet()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = MakeDataRange(sheet.Id),
            Title = null,
            AltTextDescription = "A column chart showing quarterly sales broken down by region."
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().NotContain(i => i.Kind == AccessibilityIssueKind.ChartMissingTitle);
    }

    [Fact]
    public void FindIssues_StillFlagsChartWithNoVisibleTitleAndNoAltText()
    {
        // No-regression sibling: a chart with neither a visible title nor any alt text (the common,
        // genuinely-inaccessible case covered by the pre-existing
        // FindIssues_FlagsChartsWithoutTitleText test) must still be flagged.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = MakeDataRange(sheet.Id),
            Title = null
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.ChartMissingTitle);
    }
}
