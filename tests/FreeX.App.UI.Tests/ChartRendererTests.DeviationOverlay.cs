using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    // Budget-vs-Actual combo: clustered Budget/Actual columns plus two invisible combo line
    // series carrying the same values, with <c:upDownBars> drawing a sign-colored deviation
    // bar between Budget and Actual per category. Columns 2=Budget, 3=Actual.
    private static ChartModel BuildBudgetActualComboChart(SheetId sheetId) => new()
    {
        Type = ChartType.Column,
        DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
        UseComboLineForSecondarySeries = true,
        // idx 0,1 = bar (Budget, Actual); idx 2,3 = invisible combo line (Budget, Actual)
        ComboLineSeriesIndexes = [2, 3],
        SeriesColumnMappings =
        [
            new ChartSeriesColumnMapping(0, 2),
            new ChartSeriesColumnMapping(1, 3),
            new ChartSeriesColumnMapping(2, 2),
            new ChartSeriesColumnMapping(3, 3)
        ],
        ShowUpDownBars = true,
        UpBarFillColor = new CellColor(0x4C, 0xAF, 0x50),   // green: Actual > Budget
        DownBarFillColor = new CellColor(0x21, 0x96, 0xF3)  // blue: Actual < Budget
    };

    private static ViewportModel BudgetActualViewport() => new(
        [
            Cell(1, 1, "Cat"),
            Cell(1, 2, "Budget"),
            Cell(1, 3, "Actual"),
            // A: Budget 500 > Actual 350  -> down (blue)
            Cell(2, 1, "A"),
            Cell(2, 2, "500"),
            Cell(2, 3, "350"),
            // B: Budget 550 < Actual 600  -> up (green)
            Cell(3, 1, "B"),
            Cell(3, 2, "550"),
            Cell(3, 3, "600")
        ],
        [],
        []);

    [Fact]
    public void BudgetActualCombo_RendersSignColoredDeviationBars()
    {
        var sheetId = SheetId.New();
        var chart = BuildBudgetActualComboChart(sheetId);

        var model = BuildPlotModel(chart, BudgetActualViewport());

        // A dedicated deviation overlay series is added (a RectangleBarSeries) on top of the
        // two clustered Budget/Actual columns and the two invisible combo line series.
        var deviationBars = model.Series.OfType<RectangleBarSeries>()
            .SelectMany(s => s.Items)
            .Where(IsBudgetActualDeviationBar)
            .ToList();
        deviationBars.Should().HaveCount(2, "one deviation bar per category");

        // Category A: bar spans Actual(350)..Budget(500); down-colored (blue).
        var a = deviationBars[0];
        Math.Min(a.Y0, a.Y1).Should().BeApproximately(350, 0.5);
        Math.Max(a.Y0, a.Y1).Should().BeApproximately(500, 0.5);
        a.Color.Should().Be(OxyColor.FromRgb(0x21, 0x96, 0xF3));

        // Category B: bar spans Budget(550)..Actual(600); up-colored (green).
        var b = deviationBars[1];
        Math.Min(b.Y0, b.Y1).Should().BeApproximately(550, 0.5);
        Math.Max(b.Y0, b.Y1).Should().BeApproximately(600, 0.5);
        b.Color.Should().Be(OxyColor.FromRgb(0x4C, 0xAF, 0x50));
    }

    [Fact]
    public void BudgetActualCombo_RendersRangeDataLabelTextAboveCategories()
    {
        var sheetId = SheetId.New();
        var chart = BuildBudgetActualComboChart(sheetId);
        chart.RangeDataLabels =
        [
            new ChartRangeDataLabel(2, 0, "\U0001F44E 30%"), // 👎 on category A (Budget line)
            new ChartRangeDataLabel(3, 1, "\U0001F44D 9%")   // 👍 on category B (Actual line)
        ];

        var model = BuildPlotModel(chart, BudgetActualViewport());

        var labels = model.Annotations.OfType<TextAnnotation>().Select(a => a.Text).ToList();
        labels.Should().Contain(text => text.Contains("30%"));
        labels.Should().Contain(text => text.Contains("9%"));
    }

    [Fact]
    public void BudgetActualCombo_WithoutUpDownBars_DrawsNoDeviationOverlay()
    {
        var sheetId = SheetId.New();
        var chart = BuildBudgetActualComboChart(sheetId);
        chart.ShowUpDownBars = false;

        var model = BuildPlotModel(chart, BudgetActualViewport());

        var deviationBars = model.Series.OfType<RectangleBarSeries>()
            .SelectMany(s => s.Items)
            .Where(IsBudgetActualDeviationBar)
            .ToList();
        deviationBars.Should().BeEmpty("deviation overlay only renders when upDownBars are present");
    }

    private static bool IsBudgetActualDeviationBar(RectangleBarItem item) =>
        item.Color == OxyColor.FromRgb(0x21, 0x96, 0xF3) ||
        item.Color == OxyColor.FromRgb(0x4C, 0xAF, 0x50);
}
