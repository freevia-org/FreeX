using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-65 fix bucket io-chart-enums regression tests.
///   - R65-default-fallback-swallow-sweep-1: chart-level c:dLblPos val="l"/"r"/"t"/"b" (the only
///     side positions Excel allows for the Line/3-D Line/Scatter/Bubble plot-group family) fell
///     through <c>FromXlsxDataLabelPosition</c>'s reader switch to BestFit, and the writer's
///     <c>GateDataLabelPosition</c> unconditionally forced that family down to "ctr", so the side
///     was lost both on read and on write. These tests pin the full round-trip (save -&gt; reload)
///     for the newly-supported directional positions on a Line chart, confirm the bar-family valid
///     positions still round-trip unchanged, and confirm a position genuinely invalid for a chart
///     type is still gated.
///   - R65-default-fallback-swallow-sweep-2: series marker styles "x"/"star"/"plus"/"dot"/"dash"
///     (and "auto") fell through <c>FromXlsxMarkerStyle</c>'s reader switch to Circle, and the
///     writer's <c>ToXlsxMarkerStyle</c> collapsed them back to "circle" because
///     <see cref="ChartMarkerStyle"/> had no members for them. These tests pin the round-trip for
///     the newly-added members and confirm the pre-existing members still round-trip unchanged.
/// </summary>
public sealed class R65_ChartDataLabelPositionAndMarkerStyleTests
{
    // ── R65-default-fallback-swallow-sweep-1: chart-level data-label position ──────────────────

    [Theory]
    [InlineData(ChartDataLabelPosition.Left)]
    [InlineData(ChartDataLabelPosition.Right)]
    [InlineData(ChartDataLabelPosition.Top)]
    [InlineData(ChartDataLabelPosition.Bottom)]
    public void ChartLevelDataLabelPosition_OnLineChart_SideDirectionsRoundTrip(ChartDataLabelPosition position)
    {
        // Before the fix: l/r/t/b were gated to "ctr" on write (GateDataLabelPosition forced the
        // whole line/scatter/bubble family to ctr) and, even if written, would have been read back
        // as BestFit rather than the original directional member.
        var loadedChart = SaveAndReloadLineChartWithDataLabelPosition(position);

        loadedChart.DataLabelPosition.Should().Be(position,
            "l/r/t/b are exactly the c:dLblPos values Excel allows for a Line chart's data labels, " +
            "so they must survive a save/reload cycle unchanged");
    }

    [Theory]
    [InlineData(ChartDataLabelPosition.Center)]
    [InlineData(ChartDataLabelPosition.InsideEnd)]
    [InlineData(ChartDataLabelPosition.OutsideEnd)]
    [InlineData(ChartDataLabelPosition.InsideBase)]
    public void ChartLevelDataLabelPosition_OnClusteredColumnChart_BarFamilyPositionsStillRoundTrip(
        ChartDataLabelPosition position)
    {
        // Sibling no-regression test: ctr/inEnd/outEnd/inBase are all valid for clustered column and
        // must keep round-tripping unchanged after the line/scatter/bubble gate fix.
        var loadedChart = SaveAndReloadChartWithDataLabelPosition(ChartType.Column, position);

        loadedChart.DataLabelPosition.Should().Be(position);
    }

    [Fact]
    public void ChartLevelDataLabelPosition_OnLineChart_InvalidPositionIsStillGatedToCenter()
    {
        // outEnd/inEnd/bestFit/inBase are genuinely invalid c:dLblPos values for the line/scatter/
        // bubble family and must still be gated to ctr (Center), not passed through like l/r/t/b now
        // are.
        var loadedChart = SaveAndReloadLineChartWithDataLabelPosition(ChartDataLabelPosition.OutsideEnd);

        loadedChart.DataLabelPosition.Should().Be(ChartDataLabelPosition.Center,
            "outEnd is not a valid c:dLblPos value for a Line chart, so it must be gated down to ctr, " +
            "unlike the newly-supported l/r/t/b side positions");
    }

    private static ChartModel SaveAndReloadLineChartWithDataLabelPosition(ChartDataLabelPosition position) =>
        SaveAndReloadChartWithDataLabelPosition(ChartType.Line, position);

    private static ChartModel SaveAndReloadChartWithDataLabelPosition(ChartType chartType, ChartDataLabelPosition position)
    {
        var workbook = new Workbook("ChartDataLabelPositionRoundTripR65");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = chartType.ToString(),
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ShowDataLabels = true,
            DataLabelPosition = position
        });

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    // ── R65-default-fallback-swallow-sweep-2: series marker style ───────────────────────────────

    [Theory]
    [InlineData(ChartMarkerStyle.X)]
    [InlineData(ChartMarkerStyle.Star)]
    [InlineData(ChartMarkerStyle.Plus)]
    [InlineData(ChartMarkerStyle.Dot)]
    [InlineData(ChartMarkerStyle.Dash)]
    [InlineData(ChartMarkerStyle.Auto)]
    public void SeriesMarkerStyle_NewlySupportedShapes_RoundTrip(ChartMarkerStyle markerStyle)
    {
        // Before the fix: ChartMarkerStyle had no members for these ST_MarkerStyle values, so the
        // writer collapsed them to "circle" and the reader collapsed "x"/"star"/"plus"/"dot"/"dash"/
        // "auto" back to Circle.
        var loadedChart = SaveAndReloadLineChartWithMarkerStyle(markerStyle);

        loadedChart.SeriesFormats.Should().ContainSingle()
            .Which.MarkerStyle.Should().Be(markerStyle);
    }

    [Theory]
    [InlineData(ChartMarkerStyle.None)]
    [InlineData(ChartMarkerStyle.Circle)]
    [InlineData(ChartMarkerStyle.Square)]
    [InlineData(ChartMarkerStyle.Diamond)]
    [InlineData(ChartMarkerStyle.Triangle)]
    public void SeriesMarkerStyle_PreExistingShapes_StillRoundTrip(ChartMarkerStyle markerStyle)
    {
        // Sibling no-regression test: the five original marker styles must keep round-tripping
        // unchanged after adding the six new members.
        var loadedChart = SaveAndReloadLineChartWithMarkerStyle(markerStyle);

        loadedChart.SeriesFormats.Should().ContainSingle()
            .Which.MarkerStyle.Should().Be(markerStyle);
    }

    private static ChartModel SaveAndReloadLineChartWithMarkerStyle(ChartMarkerStyle markerStyle)
    {
        var workbook = new Workbook("ChartMarkerStyleRoundTripR65");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Sales",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, MarkerStyle: markerStyle)]
        });

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }
}
