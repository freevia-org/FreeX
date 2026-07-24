using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R84-meta-1: CloneSparkline is a sibling of r83's CloneChart fix (which re-audited every
/// ChartModel property and found 5 more dropped fields) but was never itself given the same
/// per-property audit. It copies every other SparklineModel property (DataRange, Location,
/// MinAxisType/MaxAxisType, all colors, LineWeight, DisplayEmptyCellsAs, ...) but silently dropped
/// <see cref="SparklineModel.DateAxisRange"/> (Excel's Sparkline Tools &gt; Design &gt; Axis &gt;
/// "Date Axis Type" group setting): Duplicate Sheet on a sparkline group with a date axis enabled
/// reverted the copy to an evenly-spaced (non-date) axis layout.
/// </summary>
public sealed class R84_DuplicateSheetDrawingClonerSparklineDateAxisTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateSparklineSheet(Workbook workbook, out GridRange dataRange, out CellAddress location, out GridRange dateAxisRange)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(15));
        dataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3));
        location = new CellAddress(sheet.Id, 1, 4);
        dateAxisRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 3));
        return sheet;
    }

    // R84-meta-1 (bug case): a sparkline group with Excel's Date Axis Type enabled must keep its
    // date axis range on the duplicate, remapped to the new sheet, or the copy silently reverts to
    // an evenly-spaced (non-date) axis layout.
    [Fact]
    public void DuplicateSheet_SparklineWithDateAxisRange_PreservesRemappedOnCopy()
    {
        var workbook = new Workbook("SparklineCloneDateAxisRange");
        var sheet = CreateSparklineSheet(workbook, out var dataRange, out var location, out var dateAxisRange);
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = location,
            Kind = SparklineKind.Line,
            DateAxisRange = dateAxisRange
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedSheet = workbook.Sheets[1];
        var copiedSparkline = copiedSheet.Sparklines.Should().ContainSingle().Subject;
        copiedSparkline.DateAxisRange.Should().NotBeNull(
            "a sparkline group's date-axis range must not be dropped by Duplicate Sheet");
        copiedSparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(copiedSheet.Id,
            "the date-axis range must be remapped onto the duplicate sheet, not still point at the source");
        copiedSparkline.DateAxisRange.Value.Start.Row.Should().Be(dateAxisRange.Start.Row);
        copiedSparkline.DateAxisRange.Value.Start.Col.Should().Be(dateAxisRange.Start.Col);
        copiedSparkline.DateAxisRange.Value.End.Row.Should().Be(dateAxisRange.End.Row);
        copiedSparkline.DateAxisRange.Value.End.Col.Should().Be(dateAxisRange.End.Col);
    }

    // Sibling no-regression case: a sparkline group without a date axis must still duplicate
    // cleanly, leaving DateAxisRange null (not accidentally populated).
    [Fact]
    public void DuplicateSheet_SparklineWithoutDateAxisRange_LeavesFieldNull()
    {
        var workbook = new Workbook("SparklineCloneDateAxisRangeDefault");
        var sheet = CreateSparklineSheet(workbook, out var dataRange, out var location, out _);
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = location,
            Kind = SparklineKind.Line
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedSparkline = workbook.Sheets[1].Sparklines.Should().ContainSingle().Subject;
        copiedSparkline.DateAxisRange.Should().BeNull();
    }
}
