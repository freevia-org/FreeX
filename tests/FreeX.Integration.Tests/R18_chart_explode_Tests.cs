using System.IO;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R18-meta-1/R18-meta-2: the round-17 exploded-pie fix added a per-point
/// <see cref="ChartModel.ExplodedSlices"/> collection (authoritative for the XLSX writer) but did
/// not wire it to the UI edit path (<see cref="SetChartLayoutCommand"/>), native .fxl persistence
/// (<see cref="NativeJsonAdapter"/>), or the sheet-duplicate cloner (<c>DuplicateSheetDrawingCloner</c>,
/// internal to FreeX.Core.Commands). Verifies all three are now kept in sync.
/// </summary>
public sealed class R18_chart_explode_Tests
{
    private static Sheet CreatePieSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(50));
        range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        return sheet;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R18-meta-1 — SetChartLayoutCommand must sync ExplodedSlices with the scalar
    // edit so the XLSX writer (which treats ExplodedSlices as authoritative)
    // emits the NEW explosion instead of a stale loaded one.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetChartLayoutCommand_ExplodingDifferentSlice_ReplacesStaleExplodedSlicesOnSave()
    {
        var workbook = new Workbook("ExplodeEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            // Simulates a chart LOADED from XLSX with slice 0 exploded (reader populates both
            // the scalar and the per-point list — see XlsxChartPartReader.PieBubble.cs).
            ExplodedSliceIndex = 0,
            ExplodedSliceDistance = 0.25,
            ExplodedSlices = [new ChartPointExplosion(0, 0, 0.25)]
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        // UI edit: explode slice 1 instead of slice 0.
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ExplodedSliceIndex: 1, ExplodedSliceDistance: 0.3));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.ExplodedSlices.Should().ContainSingle(
            "the stale slice-0 explosion loaded from the file must be replaced, not merged with");
        chart.ExplodedSlices[0].PointIndex.Should().Be(1);
        chart.ExplodedSlices[0].Distance.Should().BeApproximately(0.3, 0.001);

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loadedChart = adapter.Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ExplodedSlices.Should().ContainSingle(
            "the writer must emit the user's new explosion, not the stale one from the loaded file");
        loadedChart.ExplodedSlices[0].PointIndex.Should().Be(1);
        loadedChart.ExplodedSlices[0].Distance.Should().BeApproximately(0.3, 0.01);
    }

    [Fact]
    public void SetChartLayoutCommand_UnexplodingSlice_ClearsExplodedSlicesOnSave()
    {
        var workbook = new Workbook("UnexplodeEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            ExplodedSliceIndex = 0,
            ExplodedSliceDistance = 0.25,
            ExplodedSlices = [new ChartPointExplosion(0, 0, 0.25)]
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        // UI edit: un-explode (Format Pie -> Explosion 0 / no slice selected).
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ExplodedSliceIndex: -1));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.ExplodedSlices.Should().BeEmpty(
            "un-exploding via the scalar must clear the stale per-point collection too");

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loadedChart = adapter.Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ExplodedSlices.Should().BeEmpty(
            "the writer must not re-emit the stale loaded explosion after the UI un-exploded the pie");
        loadedChart.ExplodedSliceIndex.Should().Be(-1);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // R18-meta-2 — a multi/fully-exploded pie must survive a native .fxl
    // round-trip and a sheet duplication, not collapse to a single slice.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FullyExplodedPie_NativeFxlRoundTrip_KeepsAllThreeExplosions()
    {
        var workbook = new Workbook("NativeFxlExplode");
        var sheet = CreatePieSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            ExplodedSliceIndex = 0,
            ExplodedSliceDistance = 0.2,
            ExplodedSlices =
            [
                new ChartPointExplosion(0, 0, 0.2),
                new ChartPointExplosion(0, 1, 0.2),
                new ChartPointExplosion(0, 2, 0.2)
            ]
        });

        var adapter = new NativeJsonAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loadedChart = adapter.Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ExplodedSlices.Should().HaveCount(3,
            "a fully-exploded pie must not collapse to one slice on a native .fxl round-trip");
        foreach (var pointIndex in Enumerable.Range(0, 3))
        {
            loadedChart.ExplodedSlices.Should()
                .ContainSingle(point => point.PointIndex == pointIndex)
                .Which.Distance.Should().BeApproximately(0.2, 0.001);
        }
    }

    [Fact]
    public void FullyExplodedPie_DuplicateSheet_KeepsAllThreeExplosions()
    {
        var workbook = new Workbook("DuplicateSheetExplode");
        var sheet = CreatePieSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            ExplodedSliceIndex = 0,
            ExplodedSliceDistance = 0.2,
            ExplodedSlices =
            [
                new ChartPointExplosion(0, 0, 0.2),
                new ChartPointExplosion(0, 1, 0.2),
                new ChartPointExplosion(0, 2, 0.2)
            ]
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;

        copiedChart.ExplodedSlices.Should().HaveCount(3,
            "duplicating a sheet must not drop a fully-exploded pie down to a single slice");
        foreach (var pointIndex in Enumerable.Range(0, 3))
        {
            copiedChart.ExplodedSlices.Should()
                .ContainSingle(point => point.PointIndex == pointIndex)
                .Which.Distance.Should().BeApproximately(0.2, 0.001);
        }
    }
}
