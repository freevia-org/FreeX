using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R14-chart-editing-1: <see cref="ChartModel.SeriesColumnMappings"/>' ValueColumn is an ABSOLUTE
/// worksheet column index (see ChartModel.Support.cs), parsed once at load time from each series'
/// &lt;c:val&gt; range. Inserting/deleting whole columns shifted <see cref="ChartModel.DataRange"/>
/// but never shifted SeriesColumnMappings, so a mapping kept pointing at its OLD absolute column
/// while the underlying worksheet data physically moved: the chart silently rendered a phantom
/// blank series in the newly inserted column and dropped the real series that slid into the
/// mapped column's old slot (ChartRenderer.SeriesFormatting.cs HasAuthoritativeSeriesColumns /
/// ShouldRenderColumnAsSeries / GetSeriesIndex all consume SeriesColumnMappings as absolute).
/// </summary>
public sealed class FreeXR14T12Tests
{
    [Fact]
    public void InsertColumn_ShiftsSeriesColumnMappings_KeepingDataRangeInSync_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Combo chart reading B1:E10 (cols 2..5), plotting columns B(2), D(4), E(5) and
        // deliberately skipping C(3) — exactly the scenario in R14-chart-editing-1.
        var originalDataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 5));
        var originalMappings = new List<ChartSeriesColumnMapping>
        {
            new(SeriesXmlIndex: 0, ValueColumn: 2), // B
            new(SeriesXmlIndex: 1, ValueColumn: 4), // D
            new(SeriesXmlIndex: 2, ValueColumn: 5)  // E
        };
        var chart = new ChartModel
        {
            DataRange = originalDataRange,
            Type = ChartType.Column,
            SeriesColumnMappings = new List<ChartSeriesColumnMapping>(originalMappings)
        };
        sheet.Charts.Add(chart);

        // Insert one column before D (absolute column 4): old D->E, old E->F, new blank D.
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 4, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 6)),
            because: "DataRange itself already shifted from B1:E10 to B1:F10");

        chart.SeriesColumnMappings.Select(m => m.ValueColumn).Should().Equal(
            new uint[] { 2u, 5u, 6u },
            because: "column B (2) is untouched, but D (4) and E (5) are both at/after the insert " +
                     "point and must shift right by 1 in lockstep with DataRange, or the renderer " +
                     "plots the blank inserted column as a series and drops the real series that " +
                     "slid into column F");
        chart.SeriesColumnMappings.Select(m => m.SeriesXmlIndex).Should().Equal(
            new[] { 0, 1, 2 },
            because: "the chart-XML series identity must be preserved — only the absolute column moves");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalDataRange, because: "undo must restore the original DataRange");
        chart.SeriesColumnMappings.Select(m => m.ValueColumn).Should().Equal(
            originalMappings.Select(m => m.ValueColumn),
            because: "undo must restore the original absolute column mappings alongside DataRange");
    }

    [Fact]
    public void DeleteColumn_ShiftsSurvivingSeriesColumnMappingsDown_AndDropsTheDeletedOne_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Same combo chart (B1:E10 plotting B, D, E) but this time column C (the already-skipped,
        // unmapped helper column) is deleted: every mapped column at/after C must shift left by 1.
        var originalDataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 5));
        var originalMappings = new List<ChartSeriesColumnMapping>
        {
            new(SeriesXmlIndex: 0, ValueColumn: 2), // B
            new(SeriesXmlIndex: 1, ValueColumn: 4), // D
            new(SeriesXmlIndex: 2, ValueColumn: 5)  // E
        };
        var chart = new ChartModel
        {
            DataRange = originalDataRange,
            Type = ChartType.Column,
            SeriesColumnMappings = new List<ChartSeriesColumnMapping>(originalMappings)
        };
        sheet.Charts.Add(chart);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 4)),
            because: "DataRange shrank from B1:E10 to B1:D10 after deleting column C");
        chart.SeriesColumnMappings.Select(m => m.ValueColumn).Should().Equal(
            new uint[] { 2u, 3u, 4u },
            because: "column B (2) is untouched; old D (4) and old E (5) both slide left by 1 to " +
                     "occupy C's and D's old slots, matching where their data physically moved to");

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(originalDataRange);
        chart.SeriesColumnMappings.Select(m => m.ValueColumn).Should().Equal(
            originalMappings.Select(m => m.ValueColumn),
            because: "undo must restore the original absolute column mappings alongside DataRange");
    }
}
