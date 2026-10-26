using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r197 (backlog item 60): <c>DuplicateSheetCommand</c> minted structured-table ids from its own scan
/// of the live tables, while <c>CreateStructuredTableCommand.NextTableId</c> — the allocator three earlier
/// rounds of reasoning built — also folds in <c>Workbook.NextStructuredTableIdWatermark</c> and every
/// slicer's and pivot cache's <c>SourceTableId</c>.
///
/// Those extra terms exist precisely so a DELETED table's id is not handed out again: when a table
/// goes away, <c>CommandGuards.PinOrphanedPivotCacheSourceTableIds</c> deliberately leaves any pivot
/// cache or slicer that was bound to it pinned to that now-orphaned id. A live-table scan cannot see
/// it, so Duplicate Sheet re-issued it and the copy's new table silently inherited the old one's
/// pivot/slicer binding — the pivot then reporting on unrelated data.
///
/// Third confirmed instance of the "second allocator for one id space" class, after the two FreeP
/// shape-id cases fixed in r195.
/// </summary>
public sealed class R197_DuplicateSheetTableIdTests
{
    [Fact]
    public void DuplicateSheet_DoesNotReissueTheIdOfADeletedTableAPivotCacheIsStillPinnedTo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var sid = sheet.Id;

        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sid, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(row));
        }

        // Two tables; the second holds the high id.
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 2));
        var ctx = new TestCommandContext(workbook);
        new CreateStructuredTableCommand(sid, range).Apply(ctx).Success.Should().BeTrue();

        var liveTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        var freedId = liveTable.Id + 1;

        // Stand in for the deleted table: a pivot cache still pinned to an id no live table holds,
        // which is exactly the state PinOrphanedPivotCacheSourceTableIds leaves behind, and raise the
        // watermark as that guard does.
        workbook.PivotCaches.Add(new PivotCacheModel { CacheId = 1, SourceTableId = freedId });
        workbook.NextStructuredTableIdWatermark =
            Math.Max(workbook.NextStructuredTableIdWatermark, freedId);

        new DuplicateSheetCommand(sid).Apply(ctx).Success.Should().BeTrue();

        var copiedTable = workbook.Sheets
            .Where(s => s.Id != sid)
            .SelectMany(s => s.StructuredTables)
            .Should().ContainSingle().Subject;

        copiedTable.Id.Should().NotBe(
            freedId,
            "the duplicated table must not inherit the pivot cache's pinned binding");
        copiedTable.Id.Should().NotBe(liveTable.Id, "and must not collide with the live table either");
    }

    [Fact]
    public void DuplicateSheet_StillGivesTheCopyItsOwnTableId()
    {
        // The ordinary case is unchanged: duplicating a sheet with a table produces a distinct id.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("H"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));

        var ctx = new TestCommandContext(workbook);
        new CreateStructuredTableCommand(
                sid,
                new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1)))
            .Apply(ctx).Success.Should().BeTrue();

        new DuplicateSheetCommand(sid).Apply(ctx).Success.Should().BeTrue();

        workbook.Sheets
            .SelectMany(s => s.StructuredTables)
            .Select(t => t.Id)
            .Should().OnlyHaveUniqueItems();
    }
}
