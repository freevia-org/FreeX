using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R47-commands-merge-unmerge-2-1: Merge Cells/Merge &amp; Center/Merge Across must discard only the
/// VALUE of a swallowed (non-top-left) cell -- Excel keeps that cell's own formatting (fill/font/
/// number-format/borders) alive so a later, genuine Unmerge (not Undo) brings back an empty cell that
/// still carries its original look. A cell with no custom formatting to begin with has nothing to
/// preserve and is still fully removed, matching the pre-existing MergeCellsCommandTests coverage.
/// </summary>
public sealed class R47_MergeCellsFormatPreservationTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void Merge_PreservesCustomStyle_OfSwallowedCell_AcrossPlainUnmerge()
    {
        var (wb, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var yellowFill = wb.RegisterStyle(new CellStyle { FillColor = CellColor.FromArgb(255, 255, 0) });

        sheet.SetCell(a1, Cell.FromValue(new TextValue("Total")));
        sheet.SetCell(b1, new Cell { Value = new NumberValue(42), StyleId = yellowFill });

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        // Right after Merge, B1's value is gone but its own formatting must still be there --
        // pre-fix, MergeCellsCommand.Apply hard-deleted the whole Cell record (ClearCell), so this
        // would already be null/default here.
        var afterMerge = sheet.GetCell(b1);
        afterMerge.Should().NotBeNull("merge only discards the value, not the swallowed cell's own formatting");
        afterMerge!.Value.Should().Be(BlankValue.Instance);
        afterMerge.StyleId.Should().Be(yellowFill);

        // A genuine (non-Undo) Unmerge afterwards must still show B1 with its original fill.
        new UnmergeCellsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        var afterUnmerge = sheet.GetCell(b1);
        afterUnmerge.Should().NotBeNull();
        afterUnmerge!.Value.Should().Be(BlankValue.Instance);
        afterUnmerge.StyleId.Should().Be(yellowFill, "Excel keeps a swallowed cell's own formatting alive across a later plain Unmerge");
    }

    [Fact]
    public void Merge_ClearsSwallowedCell_WithNoCustomStyle_LeavesNoCellRecord()
    {
        // Sibling no-regression case: a plain value cell with no custom formatting has nothing worth
        // preserving, so it must still be fully removed (matching the existing, unchanged
        // MergeCellsCommandTests.Merge_ClearsNonTopLeftCells coverage) rather than the fix
        // over-correcting into always leaving a blank-but-default-styled Cell record behind.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(99));
        sheet.SetCell(b1, new NumberValue(42));

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(99));
        sheet.GetCell(b1).Should().BeNull();
    }
}
