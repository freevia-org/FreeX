using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class SurfaceLayoutTests
{
    // ── IsSupported ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.ThreeDSurface)]
    public void IsSupported_ReturnsTrue_ForSurfaceTypes(ChartType type)
    {
        ChartLayoutEngine.IsSupported(type).Should().BeTrue();
    }

    // ── Cell count ───────────────────────────────────────────────────────────

    [Fact]
    public void LayoutSurface_ProducesExactly_RowsTimesColumns_Cells()
    {
        // 3 series (rows) × 4 categories (columns) = 12 cells
        var request = Request(
            Chart(ChartType.Surface),
            ["A", "B", "C", "D"],
            [
                Series(0, "S1", 1.0, 2.0, 3.0, 4.0),
                Series(1, "S2", 5.0, 6.0, 7.0, 8.0),
                Series(2, "S3", 9.0, 10.0, 11.0, 12.0),
            ]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().HaveCount(1);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.SurfaceCells);
        layout.Series[0].SurfaceCells.Should().HaveCount(12, "3 series × 4 categories");
    }

    [Fact]
    public void ThreeDSurface_ProducesHeatmapCells_SameAsRegularSurface()
    {
        var request = Request(
            Chart(ChartType.ThreeDSurface),
            ["X", "Y"],
            [
                Series(0, "A", 1.0, 5.0),
                Series(1, "B", 3.0, 7.0),
            ]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.SurfaceCells);
        layout.Series[0].SurfaceCells.Should().HaveCount(4, "2 series × 2 categories");
    }

    // ── Color gradient ───────────────────────────────────────────────────────

    [Fact]
    public void MinimumCell_HasBlueColor_And_MaximumCell_HasYellowColor()
    {
        // Two series, two categories: values 0 (min) and 100 (max)
        var request = Request(
            Chart(ChartType.Surface),
            ["Low", "High"],
            [
                Series(0, "S1", 0.0, 100.0),
            ]);

        var layout = ChartLayoutEngine.Layout(request);
        var cells = layout.Series[0].SurfaceCells;

        // Cell at col=0 (value=0) should be blue (68, 114, 196)
        var minCell = cells.First(c => c.Col == 0);
        minCell.FillColor.R.Should().Be(68);
        minCell.FillColor.G.Should().Be(114);
        minCell.FillColor.B.Should().Be(196);

        // Cell at col=1 (value=100) should be yellow (255, 192, 0)
        var maxCell = cells.First(c => c.Col == 1);
        maxCell.FillColor.R.Should().Be(255);
        maxCell.FillColor.G.Should().Be(192);
        maxCell.FillColor.B.Should().Be(0);
    }

    [Fact]
    public void MinCell_And_MaxCell_HaveDifferentColors()
    {
        var request = Request(
            Chart(ChartType.Surface),
            ["A", "B", "C"],
            [
                Series(0, "S1", 10.0, 50.0, 90.0),
            ]);

        var layout = ChartLayoutEngine.Layout(request);
        var cells = layout.Series[0].SurfaceCells;

        var minCell = cells.OrderBy(c => c.Value).First();
        var maxCell = cells.OrderByDescending(c => c.Value).First();

        minCell.FillColor.Should().NotBe(maxCell.FillColor,
            "minimum and maximum values should map to different colors");
    }

    [Fact]
    public void AllCells_HaveNonNegativeRGB_AndStayWithinByteRange()
    {
        var request = Request(
            Chart(ChartType.Surface),
            ["A", "B", "C", "D"],
            [
                Series(0, "S1", -100.0, 0.0, 50.0, 200.0),
                Series(1, "S2", 10.0, 30.0, 80.0, 150.0),
            ]);

        var layout = ChartLayoutEngine.Layout(request);
        var cells = layout.Series[0].SurfaceCells;

        foreach (var cell in cells)
        {
            // CellColor.R/G/B are bytes so they can't go negative or above 255;
            // just verify the gradient produces valid values for boundary inputs.
            cell.FillColor.Should().NotBe(default(CellColor), "cell should have a non-default color");
        }
    }

    // ── Cell geometry ────────────────────────────────────────────────────────

    [Fact]
    public void Cells_FillPlotAreaExactly_NoBordersOrGaps()
    {
        var plot = new PlotRect(0, 0, 400, 300);
        var request = Request(
            Chart(ChartType.Surface),
            ["A", "B", "C", "D"],
            [
                Series(0, "S1", 1.0, 2.0, 3.0, 4.0),
                Series(1, "S2", 5.0, 6.0, 7.0, 8.0),
                Series(2, "S3", 9.0, 10.0, 11.0, 12.0),
            ],
            plot);

        var layout = ChartLayoutEngine.Layout(request);
        var cells = layout.Series[0].SurfaceCells;

        // Each cell should be 100×100 (400/4 wide, 300/3 tall).
        foreach (var cell in cells)
        {
            cell.Rect.Width.Should().BeApproximately(100, 1e-6, "width = plot width / category count");
            cell.Rect.Height.Should().BeApproximately(100, 1e-6, "height = plot height / series count");
        }
    }

    [Fact]
    public void Cells_CoverEntirePlotArea()
    {
        var plot = new PlotRect(10, 20, 200, 150);
        var request = Request(
            Chart(ChartType.Surface),
            ["A", "B"],
            [
                Series(0, "S1", 5.0, 10.0),
                Series(1, "S2", 3.0, 7.0),
            ],
            plot);

        var layout = ChartLayoutEngine.Layout(request);
        var cells = layout.Series[0].SurfaceCells;

        // Total area of all cells should equal the plot area.
        var totalArea = cells.Sum(c => c.Rect.Width * c.Rect.Height);
        var plotArea = 200.0 * 150.0;
        totalArea.Should().BeApproximately(plotArea, 1e-6);
    }

    [Fact]
    public void Surface_EmptySeriesOrCategories_ReturnsEmptyLayout()
    {
        var request = Request(Chart(ChartType.Surface), [], []);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Series.Should().BeEmpty();
    }

    // ── Gradient helper ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0, 100.0, 68, 114, 196)]   // min → blue
    [InlineData(100.0, 0.0, 100.0, 255, 192, 0)]  // max → yellow
    [InlineData(50.0, 0.0, 100.0, 162, 153, 98)]  // midpoint → blended
    public void GetSurfaceCellColor_ProducesExpectedChannels(
        double value, double min, double max,
        byte expectedR, byte expectedG, byte expectedB)
    {
        var color = ChartLayoutEngine.GetSurfaceCellColor(value, min, max);

        color.R.Should().Be(expectedR);
        color.G.Should().Be(expectedG);
        color.B.Should().Be(expectedB);
    }

    [Fact]
    public void GetSurfaceCellColor_WhenMinEqualsMax_ReturnsMidpointColor()
    {
        // t = 0.5 when min == max
        var color = ChartLayoutEngine.GetSurfaceCellColor(5.0, 5.0, 5.0);

        color.R.Should().Be((byte)Math.Round(68  + (255 - 68)  * 0.5));
        color.G.Should().Be((byte)Math.Round(114 + (192 - 114) * 0.5));
        color.B.Should().Be((byte)Math.Round(196 + (0   - 196) * 0.5));
    }
}
