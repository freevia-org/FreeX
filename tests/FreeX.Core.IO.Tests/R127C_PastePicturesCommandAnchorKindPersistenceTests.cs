using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R127C (FINAL SCOPE CLOSURE of r127's audit chain): <c>PastePicturesCommand.ClonePictureAtAnchor</c>
/// is a hand-rolled clone that its own doc comment says "Mirrors
/// <c>DuplicateSheetDrawingCloner.ClonePicture</c>'s field-for-field copy" but never actually copied
/// <see cref="PictureModel.DrawingAnchorKind"/> the way <c>DuplicateSheetDrawingCloner.ClonePicture</c>
/// (fixed in R127B, see <see cref="R127B_DrawingObjectAnchorKindClonePersistenceTests"/>) now does.
/// A oneCellAnchor ("move but don't size") or absoluteAnchor ("don't move or size") picture carried
/// along in a plain Ctrl+V range-copy paste, a paste-special picture carry-over, or a tiled/multi-paste
/// (all of which route through <c>PastePicturesCommand</c> via <c>PasteCommandFactory</c>) would
/// silently revert to the <see cref="ChartDrawingAnchorKind.TwoCell"/> default on the pasted copy --
/// reintroducing the original r127 move/resize-on-row/column-insert-delete defect for the copy even
/// though the source picture's own anchor kind was left untouched.
/// <para>
/// Goes through the real product entry point: a real <see cref="PastePicturesCommand"/> applied via
/// <see cref="ICommandContext"/>, mirroring
/// <c>R97_DrawingObjectHyperlinkCopyTests.PastePicturesCommand_RangeCopyCarry_CopyKeepsHyperlink</c>'s
/// established technique for this same command.
/// </para>
/// </summary>
public sealed class R127C_PastePicturesCommandAnchorKindPersistenceTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // ── Primary finding: single-anchor paste of a oneCellAnchor picture. Fail-before/pass-after. ──

    [Fact]
    public void PastePicturesCommand_OneCellAnchorPicture_PastedCopyPreservesDrawingAnchorKind()
    {
        var workbook = new Workbook("PastePictureAnchorKind");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Name = "Pic",
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 60,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell
        };
        sheet.Pictures.Add(picture);
        var ctx = new TestCommandContext(workbook);

        var destination = new CellAddress(sheet.Id, 20, 20);
        var pasteCommand = new PastePicturesCommand(
            sheet.Id,
            new GridRange(picture.Anchor, picture.Anchor),
            destination,
            [picture],
            transpose: false);
        pasteCommand.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.Pictures.Should().HaveCount(2).And.Subject.Single(p => p.Id != picture.Id);
        pasted.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.OneCell,
            "a pasted copy of a oneCellAnchor picture must keep its \"move but don't size\" kind, " +
            "not silently revert to the TwoCell default (mirrors DuplicateSheetDrawingCloner.ClonePicture)");
    }

    // ── Sibling: absoluteAnchor picture, and the tiled/multi-paste destination-range overload. ──

    [Fact]
    public void PastePicturesCommand_AbsoluteAnchorPicture_TiledPasteCopiesPreserveDrawingAnchorKind()
    {
        var workbook = new Workbook("PastePictureAnchorKindTiled");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Name = "Pic",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3],
            ContentType = "image/png",
            Width = 100,
            Height = 60,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute
        };
        sheet.Pictures.Add(picture);
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(picture.Anchor, picture.Anchor);
        // A destination range twice as tall as the (1x1) source range tiles the paste twice.
        var destinationRange = new GridRange(
            new CellAddress(sheet.Id, 10, 10),
            new CellAddress(sheet.Id, 11, 10));
        var pasteCommand = new PastePicturesCommand(
            sheet.Id, sourceRange, destinationRange, [picture], transpose: false);
        pasteCommand.Apply(ctx).Success.Should().BeTrue();

        var pastedCopies = sheet.Pictures.Where(p => p.Id != picture.Id).ToList();
        pastedCopies.Should().HaveCount(2, "the destination range is a whole multiple of the source range, so both tiles paste");
        pastedCopies.Should().OnlyContain(p => p.DrawingAnchorKind == ChartDrawingAnchorKind.Absolute,
            "every tiled-paste copy of an absoluteAnchor picture must keep its \"don't move or size\" kind");
    }

    // ── No-regression sibling: the ordinary freshly-inserted (unset/TwoCell) case is unaffected. ──

    [Fact]
    public void PastePicturesCommand_DefaultTwoCellAnchorPicture_PastedCopyStaysTwoCell_NoRegression()
    {
        var workbook = new Workbook("PastePictureAnchorKindDefault");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Name = "Pic",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            Width = 100,
            Height = 60
        };
        sheet.Pictures.Add(picture);
        var ctx = new TestCommandContext(workbook);

        var destination = new CellAddress(sheet.Id, 20, 20);
        var pasteCommand = new PastePicturesCommand(
            sheet.Id,
            new GridRange(picture.Anchor, picture.Anchor),
            destination,
            [picture],
            transpose: false);
        pasteCommand.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.Pictures.Should().HaveCount(2).And.Subject.Single(p => p.Id != picture.Id);
        pasted.DrawingAnchorKind.Should().Be(ChartDrawingAnchorKind.TwoCell);
    }
}
