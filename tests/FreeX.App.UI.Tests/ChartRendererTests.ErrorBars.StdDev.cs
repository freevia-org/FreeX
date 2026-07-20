using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    // R52-model-not-wired-sweep-1: ChartErrorBarKind.StdDev previously fell into GetErrorBarAmount's
    // `default` arm and was computed identically to Standard Error (sample stddev / sqrt(n)), silently
    // ignoring the user's "number of standard deviations" multiplier (ChartModel.ErrorBarValue). Excel's
    // Standard Deviation error-bar amount is ErrorBarValue * the series' own sample standard deviation
    // (STDEV.S), the same amount applied to every point. For values [10, 20]: sample stddev = sqrt(50)
    // ~= 7.0710678, so with ErrorBarValue = 2 the whisker should extend the bar top by ~14.1421356 --
    // NOT by the Standard Error amount (sqrt(50)/sqrt(2) = 5.0, which also ignores the "2" entirely).
    [Fact]
    public void ColumnRenderer_StdDevErrorBarsUseSampleStdDevTimesMultiplier_NotStandardError()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.StdDev,
            ErrorBarValue = 2
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "S1"),
                Cell(2, 1, "A"), Cell(2, 2, "10"),
                Cell(3, 1, "B"), Cell(3, 2, "20")
            ],
            [],
            []));

        var barSeries = model.Series.OfType<RectangleBarSeries>().Should().ContainSingle().Subject;
        barSeries.Items.Should().HaveCount(2);

        var whiskers = model.Series.OfType<LineSeries>().Should().ContainSingle(
            "a chart-wide Standard Deviation amount applies whiskers to the single plotted series").Subject;

        var sampleStdDev = Math.Sqrt(50); // mean 15, sumSquares 50, variance 50/(2-1)
        var expectedAmount = 2 * sampleStdDev; // ErrorBarValue (2) * sample stddev
        var wrongStandardErrorAmount = sampleStdDev / Math.Sqrt(2); // = 5.0, the pre-fix (buggy) result

        expectedAmount.Should().NotBeApproximately(wrongStandardErrorAmount, 1e-6,
            "the fixture must distinguish StdDev's amount from Standard Error's amount");

        // First whisker's "plus" endpoint sits at the bar-top value (10) plus the resolved amount.
        var firstWhiskerTop = whiskers.Points[0].Y;
        firstWhiskerTop.Should().BeApproximately(10 + expectedAmount, 1e-6,
            "StdDev amount must be ErrorBarValue * sample standard deviation, not Standard Error");
        firstWhiskerTop.Should().NotBeApproximately(10 + wrongStandardErrorAmount, 1e-6,
            "StdDev must no longer collapse into the Standard Error calculation");
    }

    // Sibling/no-regression case: Standard Error (the `default` arm StdDev used to incorrectly share)
    // must still compute the plain sample-stddev-over-sqrt(n) amount, unaffected by adding a dedicated
    // StdDev arm alongside it.
    [Fact]
    public void ColumnRenderer_StandardErrorKindStillComputesStandardErrorAmount()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.StandardError
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "S1"),
                Cell(2, 1, "A"), Cell(2, 2, "10"),
                Cell(3, 1, "B"), Cell(3, 2, "20")
            ],
            [],
            []));

        var whiskers = model.Series.OfType<LineSeries>().Should().ContainSingle().Subject;

        var standardError = Math.Sqrt(50) / Math.Sqrt(2); // = 5.0
        whiskers.Points[0].Y.Should().BeApproximately(10 + standardError, 1e-6,
            "Standard Error's own amount calculation must be unchanged by the new StdDev arm");
    }
}
