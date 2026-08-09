using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the finding: Sheet.Clone's ClonePivotTable correctly remaps a same-sheet
/// pivot's SourceRange onto a duplicated sheet, but left PivotTableModel.CacheId pointing at the
/// exact same PivotCacheModel INSTANCE the source sheet's pivot still uses (workbook.PivotCaches is
/// a flat, CacheId-keyed list -- not sheet-scoped -- and CommandGuards.FindPivotCache /
/// XlsxPivotTableWriter's cacheById both resolve purely by that id). The shared cache's own
/// SourceSheetName/SourceReference/SourceTableId never got rebased onto the copy, so the copy's own
/// pivot data reads back as the ORIGINAL sheet's data on save, and (for a table-backed cache) a
/// later refresh on the copy's pivot silently snaps its SourceRange back onto the ORIGINAL table.
/// DuplicateSheetCommand must instead give the copy's same-sheet-sourced pivot(s) their own,
/// independent PivotCacheModel, mirroring UniquifyClonedTables giving a cloned StructuredTable its
/// own workbook-unique identity for the identical "must not share identity with the source" reason.
/// </summary>
public sealed class R127_DuplicateSheetPivotCacheIdentityTests
{
    [Fact]
    public void DuplicateSheet_SameSheetPivotCache_GetsIndependentCacheInstance()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));

        var originalCache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = sourceRange.ToString()
        };
        workbook.PivotCaches.Add(originalCache);

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;

        // The core defect: the copy must NOT keep sharing the original's CacheId.
        copiedPivot.CacheId.Should().NotBe(originalCache.CacheId,
            because: "the copy's pivot must resolve to its OWN cache, not the source sheet's");

        var copiedCache = workbook.PivotCaches.Should().Contain(c => c.CacheId == copiedPivot.CacheId).Subject;
        copiedCache.Should().NotBeSameAs(originalCache,
            because: "sharing the literal same PivotCacheModel instance means any mutation to one leaks into the other");

        // The copy's own cache must describe the COPY's sheet, not the original's.
        copiedCache.SourceSheetName.Should().Be(copy.Name);

        // The original cache/pivot must be completely untouched.
        originalCache.SourceSheetName.Should().Be("Sheet1");
        workbook.PivotCaches.Should().Contain(c => c.CacheId == originalCache.CacheId);
        sheet.PivotTables.Single().CacheId.Should().Be(originalCache.CacheId);

        // workbook.PivotCaches must now hold both the original and the clone.
        workbook.PivotCaches.Should().HaveCount(2);
    }

    [Fact]
    public void DuplicateSheet_SameSheetPivotCache_Undo_RemovesClonedCache()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = sourceRange.ToString()
        });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        workbook.PivotCaches.Should().HaveCount(2);

        command.Revert(ctx);

        workbook.Sheets.Should().ContainSingle();
        workbook.PivotCaches.Should().ContainSingle(c => c.CacheId == 1,
            because: "undo must remove the cloned cache along with the rest of the duplicated sheet");
    }

    [Fact]
    public void DuplicateSheet_TableBackedPivotCache_RebasesOntoCopysRenamedTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var tableRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange
        });

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = "Sheet1",
            SourceReference = tableRange.ToString(),
            SourceTableName = "Table1",
            SourceTableId = 1
        });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = tableRange,
            TargetRange = targetRange
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        var copiedCache = workbook.PivotCaches.Should().Contain(c => c.CacheId == copiedPivot.CacheId).Subject;

        // UniquifyClonedTables always renames a cloned table (see R17-table-listobject-3), so the
        // copy's table no longer shares Table1's identity.
        copiedTable.Name.Should().NotBe("Table1");
        copiedTable.Id.Should().NotBe(1);

        // The copy's own cache must point at the COPY's (renamed, re-identified) table, not the
        // source's -- otherwise PivotTableRefreshService's workbook-wide id/name lookup would
        // silently re-resolve the copy's pivot onto the ORIGINAL table.
        copiedCache.SourceTableId.Should().Be(copiedTable.Id);
        copiedCache.SourceTableName.Should().Be(copiedTable.Name);
        copiedCache.SourceSheetName.Should().Be(copy.Name);
    }
}
