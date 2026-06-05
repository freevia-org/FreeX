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
    [InlineData("Chart")]
    [InlineData("Graph")]
    [InlineData("Graph Title")]
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
    public void FindIssues_AllowsDescriptiveChartTitleTextContainingGenericWords()
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
            Title = "Sales chart",
            XAxisTitle = "Quarter",
            YAxisTitle = "Sales"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = dataRange,
            Title = "Pipeline graph",
            XAxisTitle = "Pipeline stage",
            YAxisTitle = "Deal value"
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should()
            .NotContain(i => i.Kind == AccessibilityIssueKind.GenericChartTitle);
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

    [Theory]
    [InlineData("Axis Title", "Value Axis 1")]
    [InlineData("X Axis Title", "Y Axis Title")]
    [InlineData("x-axis title", "y-axis title")]
    [InlineData("Horizontal Axis Title", "Vertical Axis Title")]
    [InlineData("Category Axis Title", "Value Axis Title")]
    public void FindIssues_FlagsChartsWithGenericAxisTitles(string xAxisTitle, string yAxisTitle)
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
            XAxisTitle = xAxisTitle,
            YAxisTitle = yAxisTitle
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.GenericChartAxisTitle)
            .ToList();

        issues.Select(i => i.Message).Should().Equal(
            "Chart X-axis title should describe the axis.",
            "Chart Y-axis title should describe the axis.");
    }

    [Fact]
    public void FindIssues_AllowsDescriptiveChartAxisTitlesContainingGenericWords()
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
            XAxisTitle = "Horizontal axis quarter",
            YAxisTitle = "Value axis revenue"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Pipeline by stage",
            XAxisTitle = "Category axis pipeline stage",
            YAxisTitle = "Y axis open deal value"
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should()
            .NotContain(i => i.Kind == AccessibilityIssueKind.GenericChartAxisTitle);
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
    public void FindIssues_FlagsLowContrastChartSeriesDataLabelOverrideText()
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
            ShowDataLabels = true,
            DataLabelTextColor = CellColor.Black,
            DataLabelFillColor = CellColor.White,
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(
                    0,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White)
            ]
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastChartText).Subject;

        issue.Location.Should().Be("A1:B4");
        issue.Message.Should().Be("Series data label text should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastChartPointDataLabelOverrideText()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(242, 242, 242))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(230, 230, 230))
        };
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
            ShowDataLabels = true,
            DataLabelTextColor = CellColor.Black,
            DataLabelFillColor = CellColor.White,
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    FontSize: 11,
                    FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                    TextThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
            ]
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Sales by quarter font",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ShowDataLabels = true,
            DataLabelTextColor = new CellColor(120, 120, 120),
            DataLabelFillColor = CellColor.White,
            DataLabelFontSize = 18,
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    FontSize: 11)
            ]
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastChartText)
            .ToList();

        issues.Select(i => i.Location).Should().Equal("A1:B4", "A1:B4");
        issues.Select(i => i.Message).Should().Equal(
            "Point data label text should have at least 4.5:1 contrast against its background.",
            "Point data label text should have at least 4.5:1 contrast against its background.");
    }

    [Fact]
    public void FindIssues_IgnoresHiddenAndDeletedChartDataLabelOverrides()
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
            Title = "Hidden labels",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ShowDataLabels = false,
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(
                    0,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White)
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White)
            ]
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Deleted point label",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ShowDataLabels = true,
            DataLabelTextColor = CellColor.Black,
            DataLabelFillColor = CellColor.White,
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White,
                    IsDeleted: true)
            ]
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = dataRange,
            Title = "Blank series labels",
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
            ShowDataLabels = true,
            DataLabelTextColor = CellColor.Black,
            DataLabelFillColor = CellColor.White,
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(
                    0,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White,
                    ShowValue: false)
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    TextColor: new CellColor(230, 230, 230),
                    FillColor: CellColor.White,
                    ShowValue: false)
            ]
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastChartText);
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
