using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R24-sparklines-1: MoveRangeCommand relocates a chart's plain DataRange when it
/// is fully contained in the moved source range (TranslateFullyContainedChartDataRanges), but had no
/// equivalent handling for a sparkline's DataRange. A sparkline anchored OUTSIDE the moved range (so
/// its own Location is never touched) whose DataRange sits entirely inside the moved range must have
/// that DataRange relocated too, or it silently keeps pointing at the now-vacated source cells.
/// </summary>
public class R24_MoveRangeSparklineDataRangeTests
{
    [Fact]
    public void Apply_MovingSparklineSourceData_RelocatesSparklineDataRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1:D1 = [1,2,3,4]
        for (uint col = 1; col <= 4; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4)); // A1:D1
        var sparklineLocation = new CellAddress(sheet.Id, 1, 6); // F1, outside the moved range
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = sparklineLocation,
            Kind = SparklineKind.Line,
        });

        var ctx = new TestCommandContext(wb);
        var destination = new CellAddress(sheet.Id, 1, 7); // G1
        var command = new MoveRangeCommand(sheet.Id, dataRange, destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var sparkline = sheet.Sparklines.Should().ContainSingle().Subject;
        sparkline.Location.Should().Be(sparklineLocation, "the sparkline's own anchor was never part of the moved range");
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 7),
            new CellAddress(sheet.Id, 1, 10)), "the sparkline's data moved from A1:D1 to G1:J1 and its DataRange must follow");

        command.Revert(ctx);

        var revertedSparkline = sheet.Sparklines.Should().ContainSingle().Subject;
        revertedSparkline.DataRange.Should().Be(dataRange, "undo must restore the sparkline's original DataRange");
    }
}
