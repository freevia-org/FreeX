using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R96: <see cref="ChartModel.SeriesFormats"/>, <see cref="ChartModel.PointFillColors"/>,
/// <see cref="ChartModel.SeriesDataLabelFormats"/>, <see cref="ChartModel.PointDataLabelFormats"/>,
/// <see cref="ChartModel.AdditionalSeriesErrorBarsXml"/> and
/// <see cref="ChartModel.AdditionalSeriesTrendlinesXml"/> are all keyed by SeriesIndex, exactly like
/// <see cref="ChartModel.SeriesOrderOverrides"/>/<see cref="ChartModel.PointMarkerFormats"/>/etc.
/// (which <see cref="RemoveChartSeriesCommand"/> and <see cref="ChangeChartSourceCommand"/> already
/// remap/clear on a series removal or source-range edit). Before this fix these six lists were left
/// holding their OLD SeriesIndex values, so XlsxChartXmlWriter's exact-SeriesIndex-equality lookups
/// (GetSeriesFormat, the PointFillColors lookup, etc.) would silently bind stale formatting to
/// whichever series now happens to occupy that index -- a color/format swap Excel never does.
/// </summary>
public sealed class R96_ChartSeriesFormatReindexTests
{
    private static GridRange ThreeSeriesRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = ThreeSeriesRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        return (sheet, ctx, chart);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RemapsSeriesFormatsAndPointFillColorsAboveRemovedIndexAndDropsAtRemovedIndex()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        // SeriesIndex 0 (untouched), 1 (being removed), 2 (must shift down to 1).
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        chart.PointFillColors.Add(new ChartPointFillFormat(0, 0, CellColor.FromArgb(1, 1, 1)));
        chart.PointFillColors.Add(new ChartPointFillFormat(1, 0, CellColor.FromArgb(2, 2, 2)));
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(3, 3, 3)));
        chart.SeriesDataLabelFormats.Add(new ChartSeriesDataLabelFormat(2, FillColor: CellColor.FromArgb(9, 9, 9)));
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(2, 0, FillColor: CellColor.FromArgb(8, 8, 8)));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(2, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(2, "<c:trendline/>"));

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 0, 255)) // was SeriesIndex 2
        ]);
        chart.PointFillColors.Should().BeEquivalentTo(
        [
            new ChartPointFillFormat(0, 0, CellColor.FromArgb(1, 1, 1)),
            new ChartPointFillFormat(1, 0, CellColor.FromArgb(3, 3, 3)) // was SeriesIndex 2
        ]);
        chart.SeriesDataLabelFormats.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(1); // was 2
        chart.PointDataLabelFormats.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(1); // was 2
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(1); // was 2
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(1); // was 2
    }

    [Fact]
    public void RemoveChartSeriesCommand_IsUndoableForSeriesFormatsAndPointFillColors()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(3, 3, 3)));
        var command = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesFormats.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);

        command.Revert(ctx);

        chart.SeriesFormats.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.PointFillColors.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
    }

    [Fact]
    public void ChangeChartSourceCommand_ClearsAndRevertsSeriesFormatsAndPointFillColorsOnSourceChange()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.PointFillColors.Add(new ChartPointFillFormat(1, 0, CellColor.FromArgb(2, 2, 2)));
        chart.SeriesDataLabelFormats.Add(new ChartSeriesDataLabelFormat(1, FillColor: CellColor.FromArgb(9, 9, 9)));
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(1, 0, FillColor: CellColor.FromArgb(8, 8, 8)));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(1, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(1, "<c:trendline/>"));
        var newRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 5));
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Widening/relocating the data range re-indexes series, so stale per-series/per-point
        // formatting must be cleared -- otherwise a brand-new series at index 1 would silently
        // inherit the OLD series' colors/formats.
        chart.SeriesFormats.Should().BeEmpty();
        chart.PointFillColors.Should().BeEmpty();
        chart.SeriesDataLabelFormats.Should().BeEmpty();
        chart.PointDataLabelFormats.Should().BeEmpty();
        chart.AdditionalSeriesErrorBarsXml.Should().BeEmpty();
        chart.AdditionalSeriesTrendlinesXml.Should().BeEmpty();

        command.Revert(ctx);

        chart.SeriesFormats.Should().ContainSingle();
        chart.PointFillColors.Should().ContainSingle();
        chart.SeriesDataLabelFormats.Should().ContainSingle();
        chart.PointDataLabelFormats.Should().ContainSingle();
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle();
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle();
    }

    [Fact]
    public void ChangeChartSourceCommand_KeepsSeriesFormatsAndPointFillColorsWhenSourceUnchanged()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.PointFillColors.Add(new ChartPointFillFormat(1, 0, CellColor.FromArgb(2, 2, 2)));
        var range = ThreeSeriesRange(sheet);
        // Same range and orientation as the chart already has: not a source change, so nothing
        // that's keyed by SeriesIndex should be touched.
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, range, firstRowIsHeader: chart.FirstRowIsHeader);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesFormats.Should().ContainSingle();
        chart.PointFillColors.Should().ContainSingle();
    }
}
