using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R102: RemoveSheetCommand.Apply already remaps ChartModel.DataRange and clears the various
/// string-based SourceSheetName refs (PivotCacheModel / SlicerModel / TimelineModel /
/// ChartModel.PivotSourceSheetName / PictureModel.LinkedSourceSheetName) when the sheet they name
/// is deleted, but it never touched Sheet.PivotTables[*].SourceRange -- a GridRange field that,
/// like ChartModel.DataRange, can point at a sheet different from the one hosting the pivot table
/// (PivotTableRefreshService.Refresh, PivotSourceContext.ReadHeaders,
/// MainWindow.PivotFilters.ReadPivotFieldMembers, SlicerTimelineSourceReader, and
/// NativeJsonAdapter.Pivot.cs's ToPivotTableDto all resolve
/// workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) directly). Left unfixed, a surviving
/// PivotTableModel keeps a SourceRange whose Start/End.Sheet is a nonexistent SheetId once its
/// source sheet is deleted -- which ToPivotTableDto treats as "silently drop this pivot table from
/// native-format save" (NativeJsonAdapter.Save.cs's OfType&lt;PivotTableDto&gt;() filter). Real
/// Excel instead keeps the pivot table in place showing its last-cached values; only a subsequent
/// manual Refresh against the missing source then errors.
/// </summary>
public sealed class R102_RemoveSheetPivotSourceRangeRemapTests
{
    [Fact]
    public void R102_RemoveSheet_RemapsPivotTableSourceRangeOntoHostSheet()
    {
        // 'Data' feeds the pivot table's source range; the pivot table itself is hosted on
        // 'Report'. Deleting 'Data' must NOT leave PivotTableModel.SourceRange pointing at the
        // now-nonexistent 'Data' SheetId -- it must be remapped onto the pivot's own host sheet,
        // exactly like a chart's cross-sheet DataRange is remapped in the same command.
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 10, 3));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(report.Id, 1, 1),
                new CellAddress(report.Id, 1, 1)),
        };
        report.PivotTables.Add(pivot);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        pivot.SourceRange.Start.Sheet.Should().Be(report.Id,
            because: "a surviving pivot table must not keep a SourceRange naming a sheet id that " +
                     "no longer exists in the workbook -- it must be remapped onto its own host " +
                     "sheet, mirroring the chart DataRange remap in the same command");
        pivot.SourceRange.End.Sheet.Should().Be(report.Id);

        // Confirms this actually reaches the real bug: the workbook must still be able to resolve
        // the pivot's SourceRange.Start.Sheet -- this is exactly the lookup
        // NativeJsonAdapter.Pivot.cs's ToPivotTableDto (and PivotTableRefreshService.Refresh,
        // PivotSourceContext.ReadHeaders, SlicerTimelineSourceReader) perform to decide whether a
        // pivot table survives native-format save / can be refreshed at all.
        wb.GetSheet(pivot.SourceRange.Start.Sheet).Should().NotBeNull();
    }

    [Fact]
    public void R102_RemoveSheetRevert_RestoresPivotTableSourceRange()
    {
        var wb = new Workbook("test");
        var data = wb.AddSheet("Data");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 10, 3));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(report.Id, 1, 1),
                new CellAddress(report.Id, 1, 1)),
        };
        report.PivotTables.Add(pivot);

        var command = new RemoveSheetCommand(data.Id);
        command.Apply(ctx);
        command.Revert(ctx);

        pivot.SourceRange.Should().Be(sourceRange,
            because: "undoing the sheet delete must restore the pivot table's original " +
                     "cross-sheet SourceRange exactly, mirroring the chart DataRange undo restore " +
                     "in the same command");
    }

    [Fact]
    public void R102_RemoveSheet_LeavesUnrelatedPivotTableSourceRangeUntouched()
    {
        // No-regression sibling: a pivot table whose SourceRange has nothing to do with the
        // deleted sheet (same-sheet source, on a sheet that isn't being deleted at all) must be
        // left completely alone by the new remap pass.
        var wb = new Workbook("test");
        var scratch = wb.AddSheet("Scratch");
        var report = wb.AddSheet("Report");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(
            new CellAddress(report.Id, 1, 1),
            new CellAddress(report.Id, 10, 3));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(report.Id, 1, 5),
                new CellAddress(report.Id, 1, 5)),
        };
        report.PivotTables.Add(pivot);

        var command = new RemoveSheetCommand(scratch.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        pivot.SourceRange.Should().Be(sourceRange,
            because: "a pivot table's own-sheet SourceRange must not be touched when an unrelated " +
                     "sheet is deleted");
    }
}
