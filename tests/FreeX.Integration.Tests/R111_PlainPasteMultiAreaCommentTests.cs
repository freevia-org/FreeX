using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R111-paste-comments-multiarea-1: PasteCommentsCommand's own sourceAreas constructor parameter
/// (R78-commands-paste-special-5-3) exists specifically to prevent a comment anchored purely in the
/// untouched GAP between disjoint Ctrl+click copied areas from being swept in by the bounding-box
/// overlap check in EnumerateSourceCells. r107/r108/r110 threaded the equivalent sourceAreas fix
/// through PasteConditionalFormatsCommand and PasteDataValidationCommand at every formatting-carrying
/// paste call site in PasteCommandFactory (non-tiled Paste Special, plain Ctrl+V, and tiled), but the
/// sibling comment-carry helpers BuildCommentCarryCommands/BuildTiledCommentCarryCommands sitting
/// right next to each of those call sites never accepted or forwarded sourceAreas at all, so
/// PasteCommentsCommand always received sourceAreas: null from the factory regardless of whether the
/// source selection was multi-area -- a plain Ctrl+V (or any formatting-carrying Paste Special) of a
/// multi-area selection would still leak a gap-only comment onto the destination. This mirrors
/// R108_PlainPasteMultiAreaDataValidationTests's coverage of the identical bug for data validation.
/// </summary>
public sealed class R111_PlainPasteMultiAreaCommentTests
{
    /// <summary>
    /// The core failing-before-fix case: a Ctrl+click multi-area copy of row1,col1 and row3,col1
    /// (bounding box spans rows 1-3) with a comment anchored ONLY in the untouched gap cell
    /// (row2,col1 -- never part of either copied area) must NOT paste that comment to the destination
    /// on a plain Ctrl+V. Before the fix, BuildCommentCarryCommands had no sourceAreas parameter to
    /// forward CreateInternalPasteCommand's own sourceAreas down to PasteCommentsCommand, so the gap
    /// comment's overlap with the whole bounding-box sourceRange caused it to be treated as "copied"
    /// and cloned onto the destination.
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_MultiArea_ExcludesGapCellComment()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var gapCell = new CellAddress(sheet.Id, 2, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        // A comment anchored purely in the gap between the two Ctrl+clicked areas -- never selected
        // or copied.
        sheet.Comments[gapCell] = "INTERNAL DRAFT - do not share";

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        var destinationStart = new CellAddress(sheet.Id, 10, 1);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var pastedGapDestination = new CellAddress(sheet.Id, 11, 1); // aligned with the never-selected gap row
        sheet.Comments.Should().NotContainKey(pastedGapDestination,
            "the destination cell aligned with the never-selected gap row must not receive the gap comment");
    }

    /// <summary>
    /// Tiled counterpart of the non-tiled case above: a plain Ctrl+V of the same disjoint multi-area
    /// copy onto a larger (whole-multiple) destination selection tiles the values as before, and
    /// still must not carry the gap-only comment -- covering CreateTiledInternalPasteCommand's own
    /// BuildTiledCommentCarryCommands construction site.
    /// </summary>
    [Fact]
    public void PlainPaste_Tiled_MultiArea_ExcludesGapCellComment()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var gapCell = new CellAddress(sheet.Id, 2, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        sheet.Comments[gapCell] = "INTERNAL DRAFT - do not share";

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        // 6-row destination selection = exactly 2 whole tiles of the 3-row bounding source range.
        var destinationRange = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 15, 1));
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationRange,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 11, 1), "tile 1's gap row must not receive the gap comment");
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 14, 1), "tile 2's gap row must not receive the gap comment");
    }

    /// <summary>
    /// No-regression sibling: a comment anchored inside one of the ACTUAL copied areas (not the gap)
    /// must still be carried to the destination on a plain multi-area Ctrl+V, proving the sourceAreas
    /// filtering only suppresses gap-only overlaps and does not regress genuine multi-area comment
    /// carrying.
    /// </summary>
    [Fact]
    public void PlainPaste_NonTiled_MultiArea_StillCarriesCommentInsideCopiedArea()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1Cell = new CellAddress(sheet.Id, 1, 1);
        var area2Cell = new CellAddress(sheet.Id, 3, 1);
        var area1 = new GridRange(area1Cell, area1Cell);
        var area2 = new GridRange(area2Cell, area2Cell);
        var boundingSourceRange = new GridRange(area1Cell, area2Cell);

        // The comment is anchored directly in area1 -- an actual copied cell, not the gap.
        sheet.Comments[area1Cell] = "keep me";

        var cell1 = Cell.FromValue(new NumberValue(1));
        var cell2 = Cell.FromValue(new NumberValue(3));
        sheet.SetCell(area1Cell, cell1);
        sheet.SetCell(area2Cell, cell2);

        var destinationStart = new CellAddress(sheet.Id, 10, 1);
        var sourceAreas = new List<GridRange> { area1, area2 };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            boundingSourceRange,
            [(area1Cell, cell1.Clone()), (area2Cell, cell2.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(),
            sourceAreas);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.Comments[destinationStart].Should().Be("keep me", "the comment anchored in the actually-copied area1 must still travel with the paste");
    }
}
