using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-172 regression coverage:
/// F1 -- a formula-error cell (e.g. #DIV/0!) plotted on a Line series must be treated the same as a
/// blank cell under the chart's BlankDisplayMode (Gap emits a NaN break point, Zero substitutes 0),
/// instead of being silently omitted -- which let OxyPlot draw a straight line across the error
/// (round-29 finding R29-chart-render-pixel-deep-2, already fixed for the portable/Avalonia layout
/// engine but not for this WPF-only AddLinePoints loop).
/// F2 -- the secondary value axis must show the workbook's own authored
/// <see cref="ChartModel.SecondaryAxisTitle"/> instead of a hardcoded "Secondary" literal.
/// </summary>
public sealed partial class ChartRendererTests
{
    private static DisplayCell ErrorCell(uint row, uint col, string errorCode) =>
        new(row, col, new ErrorValue(errorCode), errorCode, null, StyleId.Default, null);

    [Theory]
    [InlineData(ChartBlankDisplayMode.Gap, 3, true, false)]
    [InlineData(ChartBlankDisplayMode.Span, 2, false, false)]
    [InlineData(ChartBlankDisplayMode.Zero, 3, false, true)]
    public void LineRenderer_ErrorValuedCell_HonorsBlankDisplayMode(
        ChartBlankDisplayMode blankDisplayMode,
        int expectedPointCount,
        bool expectedGapPoint,
        bool expectedZeroPoint)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            BlankDisplayMode = blankDisplayMode,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                ErrorCell(3, 2, "#DIV/0!"),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(
            expectedPointCount,
            "a formula-error cell must be treated the same as a blank cell under BlankDisplayMode, not silently omitted from the series entirely");
        series.Points.Any(point => double.IsNaN(point.Y)).Should().Be(expectedGapPoint);
        series.Points.Any(point => point.X == 1 && point.Y == 0).Should().Be(expectedZeroPoint);
    }

    [Fact]
    public void LineRenderer_BlankCell_StillHonorsGapDisplayModeAfterErrorCellFix()
    {
        // Sibling no-regression check for F1: unifying the error-cell path onto the same
        // BlankDisplayMode branches as the pre-existing blank-cell path (also covered by
        // LineRenderer_HonorsBlankDisplayMode in ChartRendererTests.BlanksAndDataTables.cs) must not
        // change the blank-cell behavior itself.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            BlankDisplayMode = ChartBlankDisplayMode.Gap,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, ""),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(3);
        double.IsNaN(series.Points[1].Y).Should().BeTrue();
    }

    [Fact]
    public void SecondaryAxis_UsesChartAuthoredTitleInsteadOfHardcodedLiteral()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SecondaryAxisTitle = "Profit %"
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "6"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "11")
            ],
            [],
            []));

        var secondaryAxis = model.Axes.Should().ContainSingle(axis => axis.Key == "SecondaryY").Subject;
        secondaryAxis.Title.Should().Be("Profit %");
    }

    [Fact]
    public void SecondaryAxis_NoAuthoredTitleLeavesAxisUntitled()
    {
        // Adjacent case (rule 10): a chart with no authored secondary-axis title must not regress to
        // showing anything -- in particular must not resurrect the old hardcoded "Secondary" literal.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "6"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "11")
            ],
            [],
            []));

        var secondaryAxis = model.Axes.Should().ContainSingle(axis => axis.Key == "SecondaryY").Subject;
        secondaryAxis.Title.Should().BeNull();
    }
}
