using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Fact]
    public void ColumnRenderer_DefaultColorsStartWithExcelAccentSequence()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "S1"), Cell(1, 3, "S2"), Cell(1, 4, "S3"),
                Cell(2, 1, "A"), Cell(2, 2, "10"), Cell(2, 3, "20"), Cell(2, 4, "30"),
                Cell(3, 1, "B"), Cell(3, 2, "15"), Cell(3, 3, "25"), Cell(3, 4, "35")
            ],
            [],
            []),
            WorkbookTheme.Office);

        // Default Office accent colors: Accent1=(21,96,130), Accent2=(233,113,50), Accent3=(25,107,36)
        model.DefaultColors.Should().NotBeNullOrEmpty();
        model.DefaultColors[0].Should().Be(OxyColor.FromRgb(21, 96, 130),  "Accent1 is blue");
        model.DefaultColors[1].Should().Be(OxyColor.FromRgb(233, 113, 50), "Accent2 is orange");
        model.DefaultColors[2].Should().Be(OxyColor.FromRgb(25, 107, 36),  "Accent3 is green");
        model.DefaultColors[3].Should().Be(OxyColor.FromRgb(15, 158, 213), "Accent4 is light-blue");
        model.DefaultColors[4].Should().Be(OxyColor.FromRgb(160, 43, 147), "Accent5 is purple");
        model.DefaultColors[5].Should().Be(OxyColor.FromRgb(78, 167, 46),  "Accent6 is green");
        model.DefaultColors.Should().HaveCountGreaterThanOrEqualTo(30, "palette extends past 6 for many-series charts");

        var bars = model.Series.OfType<RectangleBarSeries>().ToList();
        bars.Should().HaveCount(3);
        bars[0].FillColor.Should().Be(OxyColor.FromRgb(21, 96, 130));
        bars[1].FillColor.Should().Be(OxyColor.FromRgb(233, 113, 50));
        bars[2].FillColor.Should().Be(OxyColor.FromRgb(25, 107, 36));
        bars.Should().OnlyContain(series => series.StrokeThickness == 0);
    }

    [Fact]
    public void ColumnRenderer_DefaultColorsComeFromSharedStylePlanner()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [Cell(1, 1, "A"), Cell(1, 2, "10"), Cell(2, 1, "B"), Cell(2, 2, "20")],
            [],
            []),
            WorkbookTheme.Office);

        var expected = ChartStylePlanner.BuildExcelSeriesPalette(WorkbookTheme.Office)
            .Select(color => OxyColor.FromRgb(color.R, color.G, color.B));

        model.DefaultColors.Take(30).Should().Equal(expected, "WPF should consume the shared chart style planner");
    }

    [Fact]
    public void ColumnRenderer_DefaultColorsRoundTwoAreLightenedAccents()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [Cell(1, 1, "A"), Cell(1, 2, "10"), Cell(2, 1, "B"), Cell(2, 2, "20")],
            [],
            []),
            WorkbookTheme.Office);

        // Accent1 base = (21,96,130). Tint +0.4 lightens: channel + (255-channel)*0.4
        var accent1Base = WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1);
        var accent1Lightened = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4);
        model.DefaultColors[6].Should().Be(OxyColor.FromRgb(accent1Lightened.R, accent1Lightened.G, accent1Lightened.B),
            "round-2 Accent1 should be tint +0.4");
    }

    [Fact]
    public void ColumnRenderer_DefaultColorsChangedByCustomTheme()
    {
        var sheetId = SheetId.New();
        var customTheme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 10, 10))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(10, 200, 10));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [Cell(1, 1, "A"), Cell(1, 2, "10"), Cell(2, 1, "B"), Cell(2, 2, "20")],
            [],
            []),
            customTheme);

        model.DefaultColors[0].Should().Be(OxyColor.FromRgb(200, 10, 10), "custom Accent1 must be used");
        model.DefaultColors[1].Should().Be(OxyColor.FromRgb(10, 200, 10), "custom Accent2 must be used");
    }

    [Fact]
    public void PieRenderer_SlicesGetDistinctExcelAccentColors()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "Values"),
                Cell(2, 1, "Alpha"),    Cell(2, 2, "10"),
                Cell(3, 1, "Beta"),     Cell(3, 2, "20"),
                Cell(4, 1, "Gamma"),    Cell(4, 2, "30")
            ],
            [],
            []),
            WorkbookTheme.Office);

        var pieSeries = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pieSeries.Slices.Should().HaveCount(3);

        // First three slices should use Accent1, Accent2, Accent3
        pieSeries.Slices[0].Fill.Should().Be(OxyColor.FromRgb(21, 96, 130),  "slice 0 = Accent1");
        pieSeries.Slices[1].Fill.Should().Be(OxyColor.FromRgb(233, 113, 50), "slice 1 = Accent2");
        pieSeries.Slices[2].Fill.Should().Be(OxyColor.FromRgb(25, 107, 36),  "slice 2 = Accent3");

        pieSeries.Slices.Select(s => s.Fill).Should().OnlyHaveUniqueItems("each slice must have a distinct color");
    }

    [Fact]
    public void PieRenderer_SlicesWrapAroundPaletteForMoreThanSixSlices()
    {
        var sheetId = SheetId.New();
        // Row 1 = header, rows 2-9 = 8 data rows (needs >6 to test palette wrap-around)
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 9, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "Value"),
                Cell(2, 1, "A"), Cell(2, 2, "10"),
                Cell(3, 1, "B"), Cell(3, 2, "20"),
                Cell(4, 1, "C"), Cell(4, 2, "30"),
                Cell(5, 1, "D"), Cell(5, 2, "40"),
                Cell(6, 1, "E"), Cell(6, 2, "50"),
                Cell(7, 1, "F"), Cell(7, 2, "60"),
                Cell(8, 1, "G"), Cell(8, 2, "70"),
                Cell(9, 1, "H"), Cell(9, 2, "80")
            ],
            [],
            []),
            WorkbookTheme.Office);

        var pieSeries = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pieSeries.Slices.Should().HaveCount(8);
        pieSeries.Slices.Should().OnlyContain(s => !s.Fill.IsInvisible(), "all slices must have a visible color");
        // Slice 6 (index 6) wraps to the lightened Accent1 (tint +0.4)
        var expectedSlice6 = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4);
        pieSeries.Slices[6].Fill.Should().Be(OxyColor.FromRgb(expectedSlice6.R, expectedSlice6.G, expectedSlice6.B),
            "slice 6 wraps to lightened Accent1");
    }

    [Fact]
    public void DoughnutRenderer_SlicesGetDistinctExcelAccentColors()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "Values"),
                Cell(2, 1, "X"),        Cell(2, 2, "10"),
                Cell(3, 1, "Y"),        Cell(3, 2, "20")
            ],
            [],
            []),
            WorkbookTheme.Office);

        var pieSeries = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pieSeries.Slices.Should().HaveCount(2);
        pieSeries.Slices[0].Fill.Should().Be(OxyColor.FromRgb(21, 96, 130),  "slice 0 = Accent1");
        pieSeries.Slices[1].Fill.Should().Be(OxyColor.FromRgb(233, 113, 50), "slice 1 = Accent2");
    }

    /// <summary>
    /// Regression test: FidelityCompare was calling ChartRenderer.Render(..., WorkbookTheme.Office)
    /// instead of workbook.Theme, causing workbooks with the classic Office 2013 theme
    /// (Accent1 = #4472C4, cornflower blue) to render as teal (#156082, modern Office default).
    /// The palette must honour whatever theme the loaded workbook supplies.
    /// </summary>
    [Fact]
    public void ColumnRenderer_WorkbookThemeAccent1IsUsedForDefaultPalette()
    {
        // Arrange: build a theme with the classic Office 2013 Accent1 = #4472C4 (cornflower blue).
        // This matches the colour found in xl/theme/theme1.xml of 10-Advanced-Excel-Charts.xlsx.
        var classicTheme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(0x44, 0x72, 0xC4)); // #4472C4

        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        // Act
        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"), Cell(1, 2, "S1"), Cell(1, 3, "S2"),
                Cell(2, 1, "A"),   Cell(2, 2, "10"), Cell(2, 3, "20"),
                Cell(3, 1, "B"),   Cell(3, 2, "15"), Cell(3, 3, "25")
            ],
            [],
            []),
            classicTheme);

        // Assert: first default colour must be the classic cornflower blue, NOT the modern teal.
        model.DefaultColors[0].Should().Be(OxyColor.FromRgb(0x44, 0x72, 0xC4),
            "the chart palette Accent1 must come from the loaded workbook theme, not from WorkbookTheme.Office");
        model.DefaultColors[0].Should().NotBe(OxyColor.FromRgb(21, 96, 130),
            "teal #156082 is the modern Office default and must NOT appear when the workbook theme says #4472C4");
    }

    [Fact]
    public void ColumnRenderer_ExplicitFormatColorStillWinsOverWorkbookThemePalette()
    {
        // Workbook theme with Accent1 = #4472C4 but series has an explicit fill colour → explicit wins.
        var classicTheme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(0x44, 0x72, 0xC4));

        var sheetId = SheetId.New();
        var explicitRed = new CellColor(200, 0, 0);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: explicitRed)]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [Cell(1, 1, "A"), Cell(1, 2, "10"), Cell(2, 1, "B"), Cell(2, 2, "20")],
            [],
            []),
            classicTheme);

        // The explicit series fill colour must still win over the theme palette.
        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.FillColor.Should().Be(OxyColor.FromRgb(200, 0, 0),
            "explicit <c:spPr><a:solidFill> must override the workbook theme palette");
    }

    [Fact]
    public void PieRenderer_ExplicitFormatColorStillWinsOverPalette()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(91, 155, 213))
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "A"), Cell(1, 2, "10"),
                Cell(2, 1, "B"), Cell(2, 2, "20"),
                Cell(3, 1, "C"), Cell(3, 2, "30")
            ],
            [],
            []),
            WorkbookTheme.Office);

        var pieSeries = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        pieSeries.Slices.Should().OnlyContain(s => s.Fill == OxyColor.FromRgb(91, 155, 213),
            "explicit SeriesFormat fill color must override the Excel palette");
    }
}
