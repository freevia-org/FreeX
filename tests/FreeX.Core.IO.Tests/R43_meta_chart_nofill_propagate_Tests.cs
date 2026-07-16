using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R43-meta-1 / R43-meta-2: round-42 added ChartModel.ChartAreaNoFill/ChartAreaNoLine/
/// PlotAreaNoFill/PlotAreaNoLine (explicit "No Fill"/"No Line" on a chart's chart-area or plot-area,
/// as opposed to merely having no fill/border color set) and wired them into the XLSX chart
/// formatting reader/writer only. Two other chart paths never learned about the 4 new fields:
/// (1) DuplicateSheetDrawingCloner.CloneChart, used by Home &gt; Sheet &gt; Duplicate Sheet, and
/// (2) FreeX's native .fxl JSON save/load (NativeJsonAdapter ToChartDto/FromChartDto/ChartDto).
/// Both silently dropped an explicit No-Fill/No-Line back to null (filled/themed) on their
/// respective round-trip. Verifies both paths now preserve the 4 fields, alongside sibling fields
/// (VaryColorsByPoint for the cloner test, ChartAreaFillColor/PlotAreaBorderThickness for the
/// native round-trip test) that already worked before this fix.
/// </summary>
public sealed class R43_meta_chart_nofill_propagate_Tests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateChartSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return sheet;
    }

    // R43-meta-1 (the bug case): an explicit chart-area/plot-area No-Fill/No-Line must survive
    // Duplicate Sheet, not silently revert to a filled/themed area on the copy.
    [Fact]
    public void DuplicateSheet_ChartWithExplicitNoFillNoLine_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneNoFill");
        var sheet = CreateChartSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartAreaNoFill = true,
            ChartAreaNoLine = true,
            PlotAreaNoFill = true,
            PlotAreaNoLine = true,
            VaryColorsByPoint = true
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;

        copiedChart.ChartAreaNoFill.Should().BeTrue(
            "an explicit chart-area No-Fill must not be dropped by Duplicate Sheet");
        copiedChart.ChartAreaNoLine.Should().BeTrue(
            "an explicit chart-area No-Line must not be dropped by Duplicate Sheet");
        copiedChart.PlotAreaNoFill.Should().BeTrue(
            "an explicit plot-area No-Fill must not be dropped by Duplicate Sheet");
        copiedChart.PlotAreaNoLine.Should().BeTrue(
            "an explicit plot-area No-Line must not be dropped by Duplicate Sheet");

        // Sibling flag the cloner already handled correctly before this fix -- must keep working.
        copiedChart.VaryColorsByPoint.Should().BeTrue();
    }

    // Sibling no-regression case: a chart with no explicit No-Fill/No-Line (the common case) must
    // still duplicate cleanly with the 4 fields left null.
    [Fact]
    public void DuplicateSheet_ChartWithoutNoFillNoLine_CopiesNullUnchanged()
    {
        var workbook = new Workbook("ChartCloneNoFillDefault");
        var sheet = CreateChartSheet(workbook, out var range);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartAreaFillColor = new CellColor(245, 245, 245)
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedChart = copy.Charts.Should().ContainSingle().Subject;

        copiedChart.ChartAreaNoFill.Should().BeNull();
        copiedChart.ChartAreaNoLine.Should().BeNull();
        copiedChart.PlotAreaNoFill.Should().BeNull();
        copiedChart.PlotAreaNoLine.Should().BeNull();
        copiedChart.ChartAreaFillColor.Should().Be(new CellColor(245, 245, 245));
    }

    // R43-meta-2 (the bug case): a native .fxl JSON round-trip must preserve an explicit
    // chart-area/plot-area No-Fill/No-Line, not silently revert to a filled/themed area on reload.
    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartAreaAndPlotAreaNoFillNoLine()
    {
        var workbook = new Workbook("ChartNativeNoFillTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ChartAreaNoFill = true,
            ChartAreaNoLine = true,
            PlotAreaNoFill = true,
            PlotAreaNoLine = true,
            ChartAreaFillColor = new CellColor(245, 245, 245),
            PlotAreaBorderThickness = 2.25
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedChart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ChartAreaNoFill.Should().BeTrue(
            "an explicit chart-area No-Fill must survive a native .fxl save/load round-trip");
        loadedChart.ChartAreaNoLine.Should().BeTrue(
            "an explicit chart-area No-Line must survive a native .fxl save/load round-trip");
        loadedChart.PlotAreaNoFill.Should().BeTrue(
            "an explicit plot-area No-Fill must survive a native .fxl save/load round-trip");
        loadedChart.PlotAreaNoLine.Should().BeTrue(
            "an explicit plot-area No-Line must survive a native .fxl save/load round-trip");

        // Sibling fields that already round-tripped correctly before this fix -- must keep working.
        loadedChart.ChartAreaFillColor.Should().Be(new CellColor(245, 245, 245));
        loadedChart.PlotAreaBorderThickness.Should().Be(2.25);
    }

    // Sibling no-regression case: a chart with no explicit No-Fill/No-Line must still round-trip
    // through native .fxl with the 4 fields left null.
    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartWithoutNoFillNoLine_StaysNull()
    {
        var workbook = new Workbook("ChartNativeNoFillDefaultTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ChartAreaFillColor = new CellColor(10, 20, 30)
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedChart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        loadedChart.ChartAreaNoFill.Should().BeNull();
        loadedChart.ChartAreaNoLine.Should().BeNull();
        loadedChart.PlotAreaNoFill.Should().BeNull();
        loadedChart.PlotAreaNoLine.Should().BeNull();
        loadedChart.ChartAreaFillColor.Should().Be(new CellColor(10, 20, 30));
    }
}
