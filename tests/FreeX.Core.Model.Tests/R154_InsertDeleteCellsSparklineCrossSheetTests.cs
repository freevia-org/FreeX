using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// freex-sparklines F1: the band-scoped Insert/Delete Cells sparkline helpers
/// (ShrinkColRangeForBandLeft/ShrinkRowRangeForBandUp/GrowColRangeForBandRight/GrowRowRangeForBandDown,
/// and their ShiftSparklinesInBand* callers) compared only row/col NUMBERS against the edited band,
/// never checking which sheet a sparkline's DataRange/DateAxisRange actually lives on. A sparkline
/// hosted on one sheet (Location) can legitimately have its DataRange on a different sheet (Excel's
/// "Edit Data" dialog allows this, and XlsxSparklineMapper.Read round-trips it) — editing the HOST
/// sheet must never mutate a DataRange that numerically coincides with the edited band but actually
/// lives on an untouched sheet. RowColumnShiftHelpers.AddressState.cs's AddressShift.ShiftRange
/// already guards this correctly (range.Start.Sheet != SheetId => no-op); this band-scoped path was
/// missing the same guard.
/// </summary>
public sealed class R154_InsertDeleteCellsSparklineCrossSheetTests
{
    [Fact]
    public void InsertCellsShiftRight_ForeignSheetDataRange_IsNotGrown()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        // Sparkline hosted on Dashboard!H1, sourced from Data!A1:E1 (a real cross-sheet source).
        var dataRange = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 1, 5)); // Data!A1:E1
        var location = new CellAddress(dashboard.Id, 1, 8); // Dashboard!H1
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        dashboard.Sparklines.Add(sparkline);

        // Insert Cells > Shift Cells Right on Dashboard!C1 — entirely on Dashboard, never touches Data.
        var range = new GridRange(new CellAddress(dashboard.Id, 1, 3), new CellAddress(dashboard.Id, 1, 3));
        var command = new InsertCellsCommand(dashboard.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();

        dashboard.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange,
            "Data!A1:E1 numerically coincides with the edited Dashboard band, but it lives on a " +
            "different, untouched sheet and must not be grown by an edit to Dashboard");
    }

    [Fact]
    public void DeleteCellsShiftLeft_ForeignSheetDataRange_IsNotDropped()
    {
        var wb = new Workbook("test");
        var dashboard = wb.AddSheet("Dashboard");
        var data = wb.AddSheet("Data");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 1, 5)); // Data!A1:E1
        var location = new CellAddress(dashboard.Id, 1, 8); // Dashboard!H1
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        dashboard.Sparklines.Add(sparkline);

        // Delete Cells > Shift Cells Left on Dashboard!A1:E1 — entirely on Dashboard, never touches Data.
        var range = new GridRange(new CellAddress(dashboard.Id, 1, 1), new CellAddress(dashboard.Id, 1, 5));
        var command = new DeleteCellsCommand(dashboard.Id, range, DeleteCellsShiftDirection.Left);

        command.Apply(ctx).Success.Should().BeTrue();

        dashboard.Sparklines.Should().ContainSingle(
            "Data!A1:E1 numerically coincides with the deleted Dashboard band, but it lives on a " +
            "different, untouched sheet and must not cause the sparkline to be dropped");
        sparkline.DataRange.Should().Be(dataRange);
        // The host's Location (Dashboard!H1) IS in the deleted-and-shifted row band, so it must still
        // move like any other same-sheet annotation -- this proves the fix didn't over-suppress the
        // in-band, same-sheet Location handling while fixing the foreign-sheet DataRange case.
        sparkline.Location.Should().Be(new CellAddress(dashboard.Id, 1, 3), "H1 shifts left by 5 columns to C1");
    }

    // ── Sibling: same-sheet band handling must still work exactly as before (R84/R86 coverage) ─────

    [Fact]
    public void InsertCellsShiftRight_SameSheetDataRange_StillGrows()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var dataRange = new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 5)); // B10:E10
        var location = new CellAddress(sheet.Id, 10, 1); // A10
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        var range = new GridRange(new CellAddress(sheet.Id, 10, 3), new CellAddress(sheet.Id, 10, 3));
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 6)),
            "same-sheet DataRange that straddles the insert point must still grow, exactly as R86 pins");
    }
}
