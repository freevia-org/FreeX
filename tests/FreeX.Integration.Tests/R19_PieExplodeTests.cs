using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R19-meta-1: <see cref="SetChartLayoutCommand"/>'s ApplyOptions unconditionally rebuilt
/// <see cref="ChartModel.ExplodedSlices"/> from the scalar <see cref="ChartModel.ExplodedSliceIndex"/>/
/// <see cref="ChartModel.ExplodedSliceDistance"/> whenever the pie/doughnut format dialog supplied those
/// fields at all -- even when they were merely echoed back unchanged (which
/// <c>ChartPieFormatPlanner.Plan()</c> always does, since it seeds every field from
/// <c>ChartPieFormatPlanner.Read()</c>). That silently collapsed a real multi-slice-exploded pie
/// (e.g. loaded from an XLSX authored in Excel) down to a single slice on any innocuous, unrelated
/// pie-format edit (changing only <see cref="ChartModel.FirstSliceAngle"/> or
/// <see cref="ChartModel.DoughnutHoleSize"/>). Verifies the fix: the scalar fields must actually
/// change relative to what <see cref="ChartModel.ExplodedSlices"/> already encodes before the list is
/// rebuilt; otherwise it is preserved untouched.
/// </summary>
public sealed class R19_pie_explode_Tests
{
    private static Sheet CreatePieSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("D"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));
        range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        return sheet;
    }

    /// <summary>
    /// Builds a chart the way it looks after being LOADED from an Excel-authored XLSX with two
    /// independently exploded slices (idx=1 at 20%, idx=3 at 40%): per
    /// XlsxChartPartReader.PieBubble.cs's ApplyPieExplosion, only the FIRST exploded point is
    /// mirrored onto the scalar ExplodedSliceIndex/ExplodedSliceDistance, while ExplodedSlices holds
    /// both.
    /// </summary>
    private static ChartModel CreateMultiExplodedPie(Sheet sheet, GridRange range)
    {
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = range,
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.2,
            ExplodedSlices =
            [
                new ChartPointExplosion(0, 1, 0.2),
                new ChartPointExplosion(0, 3, 0.4)
            ]
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    [Fact]
    public void UnrelatedPieEdit_EchoingUnchangedScalar_PreservesMultiSliceExplosion()
    {
        var workbook = new Workbook("PieUnrelatedEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = CreateMultiExplodedPie(sheet, range);
        var ctx = new TestCommandContext(workbook);

        // Mirrors what ChartPieFormatPlanner.Plan() actually sends when the user edits ONLY
        // FirstSliceAngle: it always echoes back the current (unchanged) scalar explosion values
        // read from the chart, alongside the one field the user really touched.
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                FirstSliceAngle: 45,
                ExplodedSliceIndex: chart.ExplodedSliceIndex,
                ExplodedSliceDistance: chart.ExplodedSliceDistance));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.FirstSliceAngle.Should().Be(45);
        chart.ExplodedSlices.Should().HaveCount(2,
            "an unrelated edit must not collapse a multi-slice-exploded pie down to one slice");
        chart.ExplodedSlices.Should().ContainSingle(p => p.PointIndex == 1)
            .Which.Distance.Should().BeApproximately(0.2, 0.001);
        chart.ExplodedSlices.Should().ContainSingle(p => p.PointIndex == 3)
            .Which.Distance.Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public void UnrelatedDoughnutEdit_EchoingUnchangedScalar_PreservesMultiSliceExplosion()
    {
        var workbook = new Workbook("DoughnutUnrelatedEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = CreateMultiExplodedPie(sheet, range);
        chart.Type = ChartType.Doughnut;
        var ctx = new TestCommandContext(workbook);

        // Changing only DoughnutHoleSize, with the dialog echoing back the unchanged explode scalar.
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                DoughnutHoleSize: 0.6,
                ExplodedSliceIndex: chart.ExplodedSliceIndex,
                ExplodedSliceDistance: chart.ExplodedSliceDistance));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.DoughnutHoleSize.Should().BeApproximately(0.6, 0.001);
        chart.ExplodedSlices.Should().HaveCount(2,
            "an unrelated doughnut edit must not collapse a multi-slice-exploded pie down to one slice");
    }

    [Fact]
    public void GenuineExplodeEdit_ChangingScalar_StillRebuildsExplodedSlices()
    {
        var workbook = new Workbook("PieGenuineEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = CreateMultiExplodedPie(sheet, range);
        var ctx = new TestCommandContext(workbook);

        // The user genuinely changes the explode index/distance -- this must still collapse to
        // the single new scalar explosion, exactly as before the fix.
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ExplodedSliceIndex: 2, ExplodedSliceDistance: 0.3));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.ExplodedSlices.Should().ContainSingle(
            "a genuine scalar explode edit must still replace the stale multi-slice list");
        chart.ExplodedSlices[0].PointIndex.Should().Be(2);
        chart.ExplodedSlices[0].Distance.Should().BeApproximately(0.3, 0.001);
    }

    [Fact]
    public void GenuineUnexplodeEdit_ChangingScalarToNone_ClearsExplodedSlices()
    {
        var workbook = new Workbook("PieUnexplodeEdit");
        var sheet = CreatePieSheet(workbook, out var range);
        var chart = CreateMultiExplodedPie(sheet, range);
        var ctx = new TestCommandContext(workbook);

        // The user genuinely un-explodes via the scalar (index -1); still clears as before the fix.
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ExplodedSliceIndex: -1));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.ExplodedSlices.Should().BeEmpty(
            "explicitly un-exploding via the scalar must still clear the per-point collection");
    }
}
