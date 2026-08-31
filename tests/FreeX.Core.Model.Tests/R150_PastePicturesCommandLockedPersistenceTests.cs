using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// sweep89 F1: the former <c>PastePicturesCommand.ClonePictureAtAnchor</c> hand-rolled its own
/// <see cref="PictureModel"/> clone rather than sharing the canonical authored-picture copy, and
/// its field list never copied
/// <see cref="PictureModel.Locked"/>. Because <see cref="PictureModel.Locked"/> defaults to
/// <c>true</c>, a picture the user explicitly unlocked (Format Picture &gt; Properties &gt;
/// uncheck Locked) silently reverted to locked on every copy/paste that routes through this
/// command -- <see cref="PictureCommandGuards.RejectIfEditObjectsBlocked(Sheet, PictureModel)"/>
/// gates move/resize of a picture on exactly this flag when the sheet is protected with
/// "Edit objects" unchecked, so the pasted copy of a deliberately-unlocked picture became
/// unmovable/unresizable under protection even though the source picture stayed editable.
/// Mirrors the established technique from
/// <c>R127C_PastePicturesCommandAnchorKindPersistenceTests</c>: goes through the real product
/// entry point, a real <see cref="PastePicturesCommand"/> applied via <see cref="ICommandContext"/>.
/// </summary>
public sealed class R150_PastePicturesCommandLockedPersistenceTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // ── Primary finding: pasting an explicitly-unlocked picture must keep it unlocked. ──
    // Fail-before/pass-after: before the fix, Locked was never copied, so the pasted clone
    // defaulted to Locked = true and RejectIfEditObjectsBlocked(sheet, picture) wrongly rejected
    // move/resize on a protected sheet even though the source stayed unlocked.

    [Fact]
    public void PastePicturesCommand_UnlockedPicture_PastedCopyStaysUnlocked_AndMovableUnderProtection()
    {
        var workbook = new Workbook("PastePictureLocked");
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
            Locked = false
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
        pasted.Locked.Should().BeFalse(
            "a pasted copy of an explicitly-unlocked picture must keep Locked = false, " +
            "not silently revert to the PictureModel default of true (mirrors " +
            "DuplicateSheetDrawingCloner.ClonePicture's Locked copy)");

        // Now protect the sheet with "Edit objects" blocked (the default) and confirm the guard
        // that actually consumes PictureModel.Locked allows the pasted copy through, exactly like
        // it allows the still-unlocked source through.
        sheet.IsProtected = true;
        PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, pasted).Should().BeNull(
            "an unlocked pasted picture must stay movable/resizable under sheet protection, " +
            "matching Excel's per-object Locked checkbox");
    }

    // ── No-regression sibling: a normal (default-locked) picture must still be rejected. ──

    [Fact]
    public void PastePicturesCommand_DefaultLockedPicture_PastedCopyStaysLocked_AndRejectedUnderProtection_NoRegression()
    {
        var workbook = new Workbook("PastePictureLockedDefault");
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
            // Locked left at its default (true).
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
        pasted.Locked.Should().BeTrue();

        sheet.IsProtected = true;
        PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, pasted).Should().NotBeNull(
            "a still-locked pasted picture must remain rejected for move/resize under sheet protection");
    }

    [Fact]
    public void PastePictureClone_UsesCanonicalAuthoredCopyAndPreservesCompleteState()
    {
        var sourceSheetId = SheetId.New();
        var destination = new CellAddress(SheetId.New(), 20, 21);
        var linkedRange = new GridRange(
            new CellAddress(sourceSheetId, 2, 3),
            new CellAddress(sourceSheetId, 4, 5));
        var source = new PictureModel
        {
            Name = "Complete",
            Anchor = new CellAddress(sourceSheetId, 7, 8),
            AnchorOffsetX = 1.25,
            AnchorOffsetY = 2.5,
            Kind = PictureKind.Image,
            SourceRowCount = 3,
            SourceColumnCount = 4,
            IsLinkedToSourceRange = true,
            LinkedSourceRange = linkedRange,
            LinkedSourceSheetName = "Source",
            ImageBytes = [1, 2, 3],
            SvgImageBytes = [4, 5, 6],
            ContentType = "image/png",
            LinkedImageTarget = "file:///image.png",
            Title = "Title",
            AltText = "Alt",
            IsDecorative = true,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell,
            Locked = false,
            Width = 101,
            Height = 62,
            LockAspectRatio = false,
            RotationDegrees = 12,
            FlipHorizontal = true,
            FlipVertical = true,
            IsVisible = false,
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.3,
            CropBottom = 0.4,
            Hyperlink = new DrawingObjectHyperlink("https://example.com", TargetMode: "External"),
            IsSourceLoaded = true,
            SourceLoadedWidthPixels = 500,
            SourceLoadedHeightPixels = 300
        };
        source.Cells.AddRange(
        [
            new PictureCellSnapshot(0, 0, "A"),
            new PictureCellSnapshot(1, 2, "B")
        ]);

        var clone = DuplicateSheetDrawingCloner.ClonePictureForPaste(source, destination);

        clone.Id.Should().NotBe(source.Id);
        clone.Anchor.Should().Be(destination);
        clone.LinkedSourceRange.Should().Be(linkedRange);
        clone.LinkedSourceSheetName.Should().Be("Source");
        clone.Should().BeEquivalentTo(source, options => options
            .Excluding(picture => picture.Id)
            .Excluding(picture => picture.Anchor)
            .Excluding(picture => picture.IsSourceLoaded)
            .Excluding(picture => picture.SourceLoadedWidthPixels)
            .Excluding(picture => picture.SourceLoadedHeightPixels));
        clone.ImageBytes.Should().NotBeSameAs(source.ImageBytes);
        clone.SvgImageBytes.Should().NotBeSameAs(source.SvgImageBytes);
        clone.Cells.Should().NotBeSameAs(source.Cells);
        clone.IsSourceLoaded.Should().BeFalse();
        clone.SourceLoadedWidthPixels.Should().BeNull();
        clone.SourceLoadedHeightPixels.Should().BeNull();
    }

    [Fact]
    public void PastePicturesCommand_DelegatesToCanonicalPictureClone()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("PastePicturesCommand.cs");

        source.Should().Contain("DuplicateSheetDrawingCloner.ClonePictureForPaste(");
        source.Should().NotContain("new PictureModel");
        source.Should().NotContain("ClonePictureAtAnchor");
    }
}
