using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-io-clipboard-image-formats-5-2: a plain Ctrl+C/Ctrl+V of a cell range that contains a
/// floating picture (anchored inside the copied range) must carry the picture along to the paste
/// destination, exactly like real Excel. Before the fix, PasteCommandFactory.CreateInternalPasteCommand
/// only ever built cell-value/format edits from sourceCells and never consulted sheet.Pictures, so
/// the picture was silently left behind.
/// </summary>
public sealed class R91_PasteCarriesFloatingPicturesTests
{
    private static PictureModel MakePicture(CellAddress anchor) => new()
    {
        Anchor = anchor,
        Kind = PictureKind.Image,
        ImageBytes = [1, 2, 3, 4],
        ContentType = "image/png",
        Width = 100,
        Height = 80,
    };

    [Fact]
    public void InternalPaste_PlainPasteCarriesPictureAnchoredInsideCopiedRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)); // B2:D4
        var destination = new CellAddress(sheet.Id, 10, 10);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("hi")));

        var picture = MakePicture(new CellAddress(sheet.Id, 1, 1)); // anchored at the copied range's top-left
        sheet.Pictures.Add(picture);

        var sourceCells = sourceRange.AllCells()
            .Select(a => (a, sheet.GetCell(a) ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().HaveCount(2, "the original picture stays put and a new copy is created at the destination");
        var pasted = sheet.Pictures.Single(p => p.Id != picture.Id);
        pasted.Anchor.Row.Should().Be(destination.Row);
        pasted.Anchor.Col.Should().Be(destination.Col);
        pasted.ImageBytes.Should().Equal(picture.ImageBytes);
        pasted.Width.Should().Be(picture.Width);
        pasted.Height.Should().Be(picture.Height);

        // Undo removes only the pasted copy, leaving the original picture where it was.
        command.Revert(ctx);
        sheet.Pictures.Should().ContainSingle().Which.Id.Should().Be(picture.Id);
    }

    [Fact]
    public void InternalPaste_PasteValuesDoesNotCarryPicture()
    {
        // No-regression sibling: Paste Special "Values" must NOT bring a picture along, mirroring
        // the existing comment-carry rule (InternalPaste_PasteValuesDoesNotCarryComments) -- only a
        // full (mode All, default options) paste carries objects/comments.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var destination = new CellAddress(sheet.Id, 10, 10);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("hi")));

        var picture = MakePicture(new CellAddress(sheet.Id, 1, 1));
        sheet.Pictures.Add(picture);

        var sourceCells = new List<(CellAddress, Cell)> { (sourceRange.Start, sheet.GetCell(sourceRange.Start)!) };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.Values,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().ContainSingle("Paste Values must not carry the picture along");
    }

    [Fact]
    public void InternalPaste_PictureAnchoredOutsideCopiedRangeIsNotCarried()
    {
        // No-regression sibling: a picture anchored OUTSIDE the copied range (even on the same
        // sheet) must be left alone -- only pictures whose anchor falls inside the source range
        // travel with the paste.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)); // B2 only
        var destination = new CellAddress(sheet.Id, 10, 10);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("hi")));

        var outsidePicture = MakePicture(new CellAddress(sheet.Id, 20, 20)); // far away, not in sourceRange
        sheet.Pictures.Add(outsidePicture);

        var sourceCells = new List<(CellAddress, Cell)> { (sourceRange.Start, sheet.GetCell(sourceRange.Start)!) };

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            sourceRange,
            sourceCells,
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Pictures.Should().ContainSingle().Which.Id.Should().Be(outsidePicture.Id);
    }
}
