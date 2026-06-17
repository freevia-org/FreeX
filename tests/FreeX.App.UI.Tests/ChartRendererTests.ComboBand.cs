using System;
using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests for the "shaded target band" combo chart shape (Contextures file 04): a stacked-column
/// base (transparent spacer + shaded band) overlaid by a value series drawn as a line, with a
/// date-formatted category axis and out-of-order / sparse series columns.
/// </summary>
public sealed partial class ChartRendererTests
{
    private static DisplayCell NumericCell(uint row, uint col, double value, string text) =>
        new(row, col, new NumberValue(value), text, null, StyleId.Default, null);

    [Fact]
    public void StackedColumnCombo_RendersLineSeriesAtIndexZero()
    {
        // Excel commonly puts the combo line first (series idx 0) over bar helper columns.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [0]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"),
                Cell(1, 2, "Qty"),
                Cell(1, 3, "Band"),
                Cell(2, 1, "Jan"),
                Cell(2, 2, "150"),
                Cell(2, 3, "100"),
                Cell(3, 1, "Feb"),
                Cell(3, 2, "140"),
                Cell(3, 3, "100")
            ],
            [],
            []));

        model.Series.Should().Contain(series => series is LineSeries);
        var line = (LineSeries)model.Series.First(series => series is LineSeries);
        line.Title.Should().Be("Qty");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 150), (1, 140));
    }

    [Fact]
    public void StackedColumnCombo_SkipsUnreferencedColumnsViaSeriesColumnMappings()
    {
        // Columns B,D,E are real series (line idx0 over bars idx1/idx2); column C is NOT plotted.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 5)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [0],
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 2), // Qty -> col B
                new ChartSeriesColumnMapping(1, 4), // T_Low -> col D
                new ChartSeriesColumnMapping(2, 5)  // Target -> col E
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"),
                Cell(1, 2, "Qty"),
                Cell(1, 3, "Ignored"),
                Cell(1, 4, "T_Low"),
                Cell(1, 5, "Target"),
                Cell(2, 1, "Jan"),
                Cell(2, 2, "150"),
                Cell(2, 3, "999"),
                Cell(2, 4, "250"),
                Cell(2, 5, "100")
            ],
            [],
            []));

        // Exactly three series — the "Ignored" column (C) must not become a phantom series.
        model.Series.Should().HaveCount(3);
        model.Series.Select(series => series.Title).Should().NotContain("Ignored");
        var line = model.Series.OfType<LineSeries>().Single();
        line.Title.Should().Be("Qty");
    }

    [Fact]
    public void CategoryAxisLabels_AreFormattedWithAxisNumberFormatCode()
    {
        // Date-serial categories must render via the axis numFmt (1-Jan), not the raw serial.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormatCode = "d-mmm"
        };

        // 44562 = 2022-01-01, 44593 = 2022-02-01
        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"),
                Cell(1, 2, "Band"),
                NumericCell(2, 1, 44562, "44562"),
                Cell(2, 2, "100"),
                NumericCell(3, 1, 44593, "44593"),
                Cell(3, 2, "100")
            ],
            [],
            []));

        var bottomAxis = model.Axes.First(axis => axis.Position == OxyPlot.Axes.AxisPosition.Bottom);
        bottomAxis.LabelFormatter.Should().NotBeNull();
        bottomAxis.LabelFormatter!(0).Should().Be("1-Jan");
        bottomAxis.LabelFormatter!(1).Should().Be("1-Feb");
    }

    [Fact]
    public void TransparentSpacerSeries_RendersWithNoFillAndNoStroke()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, NoFill: true, NoLine: true)]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"),
                Cell(1, 2, "Spacer"),
                Cell(2, 1, "Jan"),
                Cell(2, 2, "250")
            ],
            [],
            []));

        var bar = model.Series.OfType<RectangleBarSeries>().Single();
        bar.FillColor.Should().Be(OxyColors.Transparent);
        bar.StrokeColor.Should().Be(OxyColors.Transparent);
        bar.StrokeThickness.Should().Be(0);
    }

    [Fact]
    public void PieChart_RendersPerSliceLegendEntries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Status"),
                Cell(1, 2, "Count"),
                Cell(2, 1, "Completed"),
                Cell(2, 2, "5"),
                Cell(3, 1, "Remaining"),
                Cell(3, 2, "95")
            ],
            [],
            []));

        // A swatch + label annotation per slice; labels carry the category names.
        var legendTexts = model.Annotations.OfType<TextAnnotation>().Select(a => a.Text).ToList();
        legendTexts.Should().Contain("Completed");
        legendTexts.Should().Contain("Remaining");
        model.Annotations.OfType<RectangleAnnotation>().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void PieChart_DistinctDataPointFills_ProduceDistinctSliceColors()
    {
        var sheetId = SheetId.New();
        var theme = WorkbookTheme.Office;
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            PointFillColors =
            [
                // Same accent slot, different luminance (shade vs tint) — must resolve distinct.
                new ChartPointFillFormat(0, 0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6, -0.24)),
                new ChartPointFillFormat(0, 1, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6, 0.23))
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Status"),
                Cell(1, 2, "Count"),
                Cell(2, 1, "Completed"),
                Cell(2, 2, "5"),
                Cell(3, 1, "Remaining"),
                Cell(3, 2, "95")
            ],
            [],
            []),
            theme);

        var pie = model.Series.OfType<PieSeries>().Single();
        pie.Slices.Should().HaveCount(2);
        pie.Slices[0].Fill.Should().NotBe(pie.Slices[1].Fill);
    }
}
