using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests for cleanup batch group 8 (P79, P80): row/column shift must rekey Allow-Edit-Range
/// passwords/unlocks alongside AllowEditRanges, and must shift pivot tables hosted on a
/// different sheet than the one whose source range is being structurally edited.
/// </summary>
public sealed class FreeXCleanupB8Tests
{
    // ── P79: AllowEditRangePasswords/UnlockedAllowEditRanges must rekey with AllowEditRanges ──

    [Fact]
    public void InsertRows_RekeysAllowEditRangePasswordAndUnlockOntoShiftedRange()
    {
        var (_, sheet, ctx) = Setup();

        var original = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 10, 4));
        var shifted = new GridRange(new CellAddress(sheet.Id, 6, 2), new CellAddress(sheet.Id, 11, 4));

        // Protection is granted here purely so the range password/unlock exist and can be asserted
        // through CommandGuards; InsertRows itself is permitted while protected so the structural
        // edit under test is not itself rejected by the (unrelated) protection guard.
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.InsertRows);
        sheet.IsProtected = true;
        sheet.AllowEditRanges.Add(original);
        sheet.AllowEditRangePasswords[original] = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("secret");
        sheet.UnlockedAllowEditRanges.Add(original);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        // AllowEditRanges itself shifts (pre-existing behavior).
        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(shifted);

        // P79: the password must follow the range to its new key, not stay orphaned under the old
        // (now-stale) GridRange.
        CommandGuards.IsPasswordProtected(sheet, shifted).Should().BeTrue();
        sheet.AllowEditRangePasswords.Should().NotContainKey(original);

        // P79: the per-session unlock must also follow, so an already-unlocked range does not
        // spuriously re-prompt for its password merely because the shift moved it.
        sheet.UnlockedAllowEditRanges.Should().Contain(shifted);
        sheet.UnlockedAllowEditRanges.Should().NotContain(original);

        // CommandGuards must therefore treat the shifted range as already unlocked (no re-prompt).
        var cellInRange = new CellAddress(sheet.Id, 7, 2);
        CommandGuards.CanEditCell(ctx.Workbook, sheet, cellInRange).Should().BeTrue();

        command.Revert(ctx);

        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(original);
        CommandGuards.IsPasswordProtected(sheet, original).Should().BeTrue();
        sheet.AllowEditRangePasswords.Should().NotContainKey(shifted);
        sheet.UnlockedAllowEditRanges.Should().Contain(original);
    }

    [Fact]
    public void DeleteRows_DropsAllowEditRangePasswordWhenRangeIsFullyDeleted()
    {
        var (_, sheet, ctx) = Setup();

        var original = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 6, 4));
        sheet.AllowEditRanges.Add(original);
        sheet.AllowEditRangePasswords[original] = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("secret");
        sheet.UnlockedAllowEditRanges.Add(original);

        // Delete rows 5-6, entirely removing the allow-edit range.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AllowEditRanges.Should().BeEmpty();
        // The password/unlock entries for a range that no longer exists must not linger keyed by a
        // stale GridRange forever (would never be reachable again, but also must not silently
        // resurrect under some future coincidentally-identical range).
        sheet.AllowEditRangePasswords.Should().NotContainKey(original);
        sheet.UnlockedAllowEditRanges.Should().NotContain(original);
    }

    // ── P80: pivot tables hosted on a different sheet than their source data ──────────────────

    [Fact]
    public void InsertRows_ShiftsSourceRangeOfPivotTableHostedOnAnotherSheet()
    {
        var workbook = new Workbook("test");
        var dataSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("PivotSheet");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(dataSheet.Id, 1, 1),
            new CellAddress(dataSheet.Id, 100, 4));
        var targetRange = new GridRange(
            new CellAddress(pivotSheet.Id, 1, 1),
            new CellAddress(pivotSheet.Id, 5, 3));

        pivotSheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        // Insert 5 rows at the top of the DATA sheet (not the sheet the pivot is placed on).
        var command = new InsertRowsCommand(dataSheet.Id, beforeRow: 1, count: 5);
        command.Apply(ctx).Success.Should().BeTrue();

        var pivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        pivot.SourceRange.Should().Be(new GridRange(
            new CellAddress(dataSheet.Id, 6, 1),
            new CellAddress(dataSheet.Id, 105, 4)));
        // TargetRange lives on pivotSheet, unaffected by an edit to dataSheet.
        pivot.TargetRange.Should().Be(targetRange);

        command.Revert(ctx);

        var revertedPivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        revertedPivot.SourceRange.Should().Be(sourceRange);
        revertedPivot.TargetRange.Should().Be(targetRange);
    }

    [Fact]
    public void DeleteRows_ShiftsSourceRangeOfPivotTableHostedOnAnotherSheet()
    {
        var workbook = new Workbook("test");
        var dataSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("PivotSheet");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(dataSheet.Id, 10, 1),
            new CellAddress(dataSheet.Id, 100, 4));
        var targetRange = new GridRange(
            new CellAddress(pivotSheet.Id, 1, 1),
            new CellAddress(pivotSheet.Id, 5, 3));

        pivotSheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        // Delete 5 rows above the pivot's source range on the DATA sheet.
        var command = new DeleteRowsCommand(dataSheet.Id, startRow: 1, count: 5);
        command.Apply(ctx).Success.Should().BeTrue();

        var pivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        pivot.SourceRange.Should().Be(new GridRange(
            new CellAddress(dataSheet.Id, 5, 1),
            new CellAddress(dataSheet.Id, 95, 4)));

        command.Revert(ctx);

        var revertedPivot = pivotSheet.PivotTables.Should().ContainSingle().Subject;
        revertedPivot.SourceRange.Should().Be(sourceRange);
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }
}
