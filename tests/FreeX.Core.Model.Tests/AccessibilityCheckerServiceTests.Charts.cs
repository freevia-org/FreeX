using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsChartsWithoutTitleText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "   "
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().HaveCount(2);
        issues.Should().OnlyContain(i => i.Kind == AccessibilityIssueKind.ChartMissingTitle);
        issues.Should().OnlyContain(i => i.SheetId == sheet.Id);
        issues.Should().OnlyContain(i => i.SheetName == "Charts");
        issues.Should().OnlyContain(i => i.Location == "A1:B4");
        issues.Should().OnlyContain(i => i.Message == "Chart is missing a title.");
    }

    [Theory]
    [InlineData("Chart Title")]
    [InlineData("chart title")]
    [InlineData("Title")]
    [InlineData("Chart 1")]
    public void FindIssues_FlagsChartsWithGenericTitleText(string title)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = title,
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.GenericChartTitle).Subject;

        issue.Location.Should().Be("A1:B4");
        issue.Message.Should().Be("Chart title should describe the chart.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastChartTitleText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales",
            ChartAreaFillColor = new CellColor(130, 130, 130),
            ChartTitleTextColor = new CellColor(120, 120, 120)
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastChartText).Subject;

        issue.Location.Should().Be("A1:B4");
        issue.Message.Should().Be("Chart title should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_FlagsChartsWithMissingAxisTitles()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "",
            YAxisTitle = "   "
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.ChartMissingAxisTitle)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1:B4", "A1:B4");
        issues.Select(i => i.Message).Should().Equal(
            "Chart X-axis is missing a title.",
            "Chart Y-axis is missing a title.");
    }

    [Fact]
    public void FindIssues_FlagsChartsWithGenericAxisTitles()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Axis Title",
            YAxisTitle = "Value Axis 1"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericChartAxisTitle)
            .ToList();

        issues.Select(i => i.Message).Should().Equal(
            "Chart X-axis title should describe the axis.",
            "Chart Y-axis title should describe the axis.");
    }

    [Fact]
    public void FindIssues_IgnoresAxisTitleRulesForHiddenAxesAndChartsWithoutAxes()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = "Product mix"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 6, 1),
                new CellAddress(sheet.Id, 9, 2)),
            Title = "Hidden axes",
            HideXAxis = true,
            HideYAxis = true
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should()
            .NotContain(i =>
                i.Kind == AccessibilityIssueKind.ChartMissingAxisTitle ||
                i.Kind == AccessibilityIssueKind.GenericChartAxisTitle);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastChartAxisLabelsDataTableAndTrendlineText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ChartAreaFillColor = new CellColor(130, 130, 130),
            ChartTitleTextColor = CellColor.Black,
            AxisTitleTextColor = CellColor.Black,
            XAxisLabelTextColor = new CellColor(120, 120, 120),
            YAxisLabelTextColor = CellColor.Black,
            DataTable = new ChartDataTableModel
            {
                FillColor = new CellColor(130, 130, 130),
                TextColor = new CellColor(120, 120, 120),
                FontSize = 10
            },
            ShowLinearTrendline = true,
            ShowTrendlineEquation = true,
            TrendlineLabelFillColor = new CellColor(130, 130, 130),
            TrendlineLabelTextColor = new CellColor(120, 120, 120)
        });

        var messages = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastChartText)
            .Select(i => i.Message);

        messages.Should().Equal(
            "X-axis labels should have at least 4.5:1 contrast against its background.",
            "Chart data table text should have at least 4.5:1 contrast against its background.",
            "Trendline label text should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_IgnoresChartTextWithSufficientContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Charts");
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Sales by quarter",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ChartAreaFillColor = CellColor.White,
            ChartTitleTextColor = CellColor.Black,
            AxisTitleTextColor = CellColor.Black,
            LegendTextColor = CellColor.Black,
            LegendFillColor = CellColor.White
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastChartText);
    }
}
