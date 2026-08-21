using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R161-pivot-refresh-fully-consumed-source: RowColumnShiftHelpers.AddressState.cs's ShiftPivotTables
/// clears every host sheet's PivotTables up front, then only re-adds a surviving PivotTableModel when
/// BOTH shift.ShiftRange(SourceRange) and shift.ShiftRange(TargetRange) return a value. A whole-row or
/// whole-column delete that fully consumes the pivot's SourceRange (while its TargetRange survives --
/// a common layout, since the pivot is usually placed to the side of/below its source) made ShiftRange
/// return null for SourceRange, so the `continue` before PivotTables.Add silently dropped the
/// PivotTableModel from the live workbook model entirely: the bound PivotCacheModel became a permanent
/// orphan, the previously-rendered output cells were left as dead static values with no model behind
/// them, and any later command addressing the pivot by name (Refresh, Options, a bound slicer/chart)
/// failed via CommandGuards.TryFindPivotTable -> RejectPivotTableNotFound ("PivotTable was not
/// found."). Real Excel never deletes the pivot object itself just because its source data went away.
/// </summary>
public sealed class R161_PivotFullyConsumedSourceRangeRowColumnDeleteTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // ── Primary finding: whole-row delete fully consuming SourceRange must not orphan the pivot ──

    [Fact]
    public void DeleteRows_FullyConsumingSourceRangeWithSurvivingTargetRange_KeepsPivotAliveAndRefreshable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Source data A1:C4 (header row + 3 data rows), rendered pivot output at F1:H5 (row 5
        // survives the row 1-4 delete, so nothing else in the pipeline rejects the operation).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(30));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        var targetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 5, 8));

        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivotTable.RowFields.Add(new PivotFieldModel(0));
        pivotTable.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivotTable);

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C4"
        };
        workbook.PivotCaches.Add(cache);

        // Delete whole rows 1-4: fully consumes the pivot's SourceRange (A1:C4), while its
        // TargetRange (F1:H5) survives because row 5 is untouched.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 4);
        var deleteOutcome = command.Apply(ctx);

        deleteOutcome.Success.Should().BeTrue();

        sheet.PivotTables.Should().ContainSingle(
            "the PivotTableModel must survive a delete that fully consumes only its SourceRange, " +
            "not vanish from the live workbook model");
        workbook.PivotCaches.Should().ContainSingle(
            "the pivot cache must stay bound to a live pivot table, not become a permanently orphaned cache");

        var refreshOutcome = new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);
        refreshOutcome.ErrorMessage.Should().NotBe(
            "PivotTable was not found.",
            "the pivot must remain addressable by name for Refresh/Options/slicer commands after the delete");

        // Undo must still restore the original pivot exactly as before (verified pre-existing
        // behavior; this fix must not regress it).
        command.Revert(ctx);

        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(sourceRange);
        sheet.PivotTables.Single().TargetRange.Should().Be(targetRange);
    }

    // ── Column-delete counterpart of the primary finding ──────────────────────────────────────────

    [Fact]
    public void DeleteColumns_FullyConsumingSourceRangeWithSurvivingTargetRange_KeepsPivotAliveAndRefreshable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        // Target range in column F..H survives a delete of columns 1-3 (A:C) because it starts past
        // the deleted band.
        var targetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 3, 8));

        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivotTable.RowFields.Add(new PivotFieldModel(0));
        pivotTable.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivotTable);

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C2"
        };
        workbook.PivotCaches.Add(cache);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 1, count: 3);
        var deleteOutcome = command.Apply(ctx);

        deleteOutcome.Success.Should().BeTrue();

        sheet.PivotTables.Should().ContainSingle(
            "a column delete that fully consumes only the SourceRange must not drop the PivotTableModel either");
        workbook.PivotCaches.Should().ContainSingle();

        var refreshOutcome = new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);
        refreshOutcome.ErrorMessage.Should().NotBe("PivotTable was not found.");
    }

    // ── Sibling no-regression: a PARTIAL overlap must still shift/shrink normally (unchanged) ──────

    [Fact]
    public void DeleteRows_PartiallyOverlappingSourceRange_StillShrinksNormally()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Source spans rows 1-6; only rows 2-3 (strictly inside it) are deleted, so the range must
        // shrink to rows 1-4 exactly like the existing (already-correct) partial-overlap behavior --
        // this must NOT go through the new "fully consumed" fallback path.
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        var targetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 3, 8));

        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivotTable.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivotTable);

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C6"
        });

        var command = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            "a partial overlap must still shrink the SourceRange to the surviving rows, not fall back to the pre-shift range");
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("A1:C4");

        command.Revert(ctx);

        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(sourceRange);
    }
}
