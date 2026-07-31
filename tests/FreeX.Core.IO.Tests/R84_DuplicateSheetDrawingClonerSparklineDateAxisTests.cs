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

    // R107-cmd-duplicate-sheet-sparkline-cross-sheet-datarange (bug case): a sparkline hosted on the
    // sheet being duplicated but sourced from a DIFFERENT sheet's data (a normal, supported
    // cross-sheet sparkline -- see XlsxSparklineMapper) must keep its DataRange pointing at the
    // original source-data sheet on the duplicate, not get silently rewritten onto the copy sheet's
    // own (likely blank/unrelated) cells. Mirrors CloneChart's same-sheet-only DataRange guard.
    [Fact]
    public void DuplicateSheet_SparklineWithCrossSheetDataRange_LeavesDataRangeOnOriginalSheet()
    {
        var workbook = new Workbook("SparklineCloneCrossSheetDataRange");
        var dataSheet = workbook.AddSheet("Data");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new NumberValue(20));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 3), new NumberValue(15));
        var crossSheetDataRange = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 1, 3));

        var dashboardSheet = workbook.AddSheet("Dashboard");
        var location = new CellAddress(dashboardSheet.Id, 1, 4);
        dashboardSheet.Sparklines.Add(new SparklineModel
        {
            DataRange = crossSheetDataRange,
            Location = location,
            Kind = SparklineKind.Line
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(dashboardSheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedSheet = workbook.Sheets[2];
        copiedSheet.Name.Should().Be("Dashboard (2)");
        var copiedSparkline = copiedSheet.Sparklines.Should().ContainSingle().Subject;
        copiedSparkline.DataRange.Start.Sheet.Should().Be(dataSheet.Id,
            "a cross-sheet DataRange must keep pointing at the original source-data sheet, matching Excel's " +
            "Duplicate Sheet behavior, not get rewritten onto the duplicated sheet's own unrelated cells");
        copiedSparkline.DataRange.Start.Row.Should().Be(crossSheetDataRange.Start.Row);
        copiedSparkline.DataRange.Start.Col.Should().Be(crossSheetDataRange.Start.Col);
        copiedSparkline.DataRange.End.Row.Should().Be(crossSheetDataRange.End.Row);
        copiedSparkline.DataRange.End.Col.Should().Be(crossSheetDataRange.End.Col);

        // The Location, in contrast, MUST follow the duplicate -- it identifies which cell on the
        // duplicated sheet hosts the sparkline, so it always remaps regardless of DataRange sheet.
        copiedSparkline.Location.Sheet.Should().Be(copiedSheet.Id);
        copiedSparkline.Location.Row.Should().Be(location.Row);
        copiedSparkline.Location.Col.Should().Be(location.Col);
    }

    // Sibling no-regression case: a same-sheet DateAxisRange living on a DIFFERENT sheet than the
    // sparkline's own DataRange host must independently be left alone too, mirroring the DataRange
    // guard -- a date axis range is exactly as capable of being cross-sheet as the DataRange is.
    [Fact]
    public void DuplicateSheet_SparklineWithCrossSheetDateAxisRange_LeavesDateAxisRangeOnOriginalSheet()
    {
        var workbook = new Workbook("SparklineCloneCrossSheetDateAxisRange");
        var dataSheet = workbook.AddSheet("Data");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new NumberValue(20));
        var crossSheetDataRange = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 1, 2));
        var crossSheetDateAxisRange = new GridRange(new CellAddress(dataSheet.Id, 2, 1), new CellAddress(dataSheet.Id, 2, 2));

        var dashboardSheet = workbook.AddSheet("Dashboard");
        var location = new CellAddress(dashboardSheet.Id, 1, 4);
        dashboardSheet.Sparklines.Add(new SparklineModel
        {
            DataRange = crossSheetDataRange,
            Location = location,
            Kind = SparklineKind.Line,
            DateAxisRange = crossSheetDateAxisRange
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(dashboardSheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedSheet = workbook.Sheets[2];
        var copiedSparkline = copiedSheet.Sparklines.Should().ContainSingle().Subject;
        copiedSparkline.DateAxisRange.Should().NotBeNull();
        copiedSparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(dataSheet.Id,
            "a cross-sheet DateAxisRange must also keep pointing at the original source sheet, not get " +
            "rewritten onto the duplicated sheet");
    }
}
