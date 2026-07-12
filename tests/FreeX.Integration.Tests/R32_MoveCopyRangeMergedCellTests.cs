using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for two round-32 findings:
///
/// R32-commands-clipboard-deep-1: MoveRangeCommand's merge-collision guard
/// (`sheet.MergedRegions.Any(range => _sourceRange.Overlaps(range) || targetRange.Overlaps(range))`)
/// found the source's OWN merge in MergedRegions and treated the trivial self-overlap as a
/// collision, so cutting/dragging ANY range containing a merged cell was always rejected -- even to
/// a completely empty destination. Real Excel moves the merge along with its content. The fix
/// excludes merges fully contained in the source range (the merge(s) being moved) from the guard,
/// and relocates their geometry in Apply/Revert.
///
/// R32-commands-clipboard-deep-2: the identical bug in CopyRangeCommand (Excel's Ctrl+drag-copy
/// gesture). The fix mirrors the Move fix, but since Copy leaves the source untouched, it clones a
/// translated copy of the source's merge(s) at the destination instead of relocating them.
///
/// Both fixes must NOT loosen the guard for the sibling case where the destination (or a
/// non-fully-contained sliver of the source) collides with a genuinely DIFFERENT, unrelated merge --
/// that must remain rejected exactly like before.
/// </summary>
public sealed class R32_MoveCopyRangeMergedCellTests
{
    [Fact]
    public void Move_CutMergedCellToEmptyDestination_MovesContentAndMergeGeometry()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var source = new GridRange(a1, b1); // A1:B1, merged
        sheet.AddMergedRegion(source);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Q1")));

        var destination = new CellAddress(sheet.Id, 1, 4); // D1, empty
        var command = new MoveRangeCommand(sheet.Id, source, destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedDestinationMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 1, 5)); // D1:E1
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(expectedDestinationMerge);
        sheet.GetCell(a1).Should().BeNull();
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("Q1"));

        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(source);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Q1"));
        sheet.GetCell(destination).Should().BeNull();
    }

    [Fact]
    public void Move_DestinationOverlapsDifferentExistingMerge_IsStillRejected()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var source = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2)); // A1:B1, merged (being moved)
        sheet.AddMergedRegion(source);

        var siblingMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 1, 5)); // D1:E1, a DIFFERENT, unrelated merge sitting at the destination
        sheet.AddMergedRegion(siblingMerge);

        var destination = new CellAddress(sheet.Id, 1, 4); // D1 -- collides with siblingMerge
        var command = new MoveRangeCommand(sheet.Id, source, destination);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot move a range that intersects merged cells.");
        sheet.MergedRegions.Should().BeEquivalentTo([source, siblingMerge]);
    }

    [Fact]
    public void Copy_CtrlDragMergedCellToEmptyDestination_ClonesContentAndMerge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var source = new GridRange(a1, b1); // A1:B1, merged
        sheet.AddMergedRegion(source);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Q1")));

        var destination = new CellAddress(sheet.Id, 1, 4); // D1, empty
        var command = new CopyRangeCommand(sheet.Id, source, destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var expectedDestinationMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 1, 5)); // D1:E1
        sheet.MergedRegions.Should().BeEquivalentTo([source, expectedDestinationMerge]);
        // Source untouched by a copy.
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("Q1"));
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("Q1"));

        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(source);
        sheet.GetCell(destination).Should().BeNull();
    }

    [Fact]
    public void Copy_DestinationOverlapsDifferentExistingMerge_IsStillRejected()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var source = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2)); // A1:B1, merged (being copied)
        sheet.AddMergedRegion(source);

        var siblingMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 1, 5)); // D1:E1, a DIFFERENT, unrelated merge sitting at the destination
        sheet.AddMergedRegion(siblingMerge);

        var destination = new CellAddress(sheet.Id, 1, 4); // D1 -- collides with siblingMerge
        var command = new CopyRangeCommand(sheet.Id, source, destination);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot copy a range that intersects merged cells.");
        sheet.MergedRegions.Should().BeEquivalentTo([source, siblingMerge]);
    }
}
