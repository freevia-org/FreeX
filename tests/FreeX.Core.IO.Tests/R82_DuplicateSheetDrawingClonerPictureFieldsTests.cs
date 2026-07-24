using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-meta-2: DuplicateSheetDrawingCloner.ClonePicture's object initializer omitted
/// PictureModel.LinkedImageTarget (a "Link to File" picture, whose ImageBytes is empty/null and
/// whose only content is the external relationship target) and PictureModel.SvgImageBytes (an
/// Insert &gt; Icons/SVG picture's vector part carried alongside its PNG fallback in ImageBytes) --
/// so Duplicate Sheet either produced an empty/invisible drawing object (linked picture: neither
/// ImageBytes nor LinkedImageTarget survive) or silently downgraded the picture to a flat raster on
/// the next save (SVG picture). Verifies both fields now survive Duplicate Sheet, plus sibling
/// no-regression cases confirming a plain embedded raster picture (neither field populated) still
/// duplicates cleanly.
/// </summary>
public sealed class R82_DuplicateSheetDrawingClonerPictureFieldsTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    // R82-meta-2 (bug case a): a picture inserted via Excel's "Link to File" (LinkedImageTarget set,
    // ImageBytes empty) must keep its LinkedImageTarget on the Duplicate Sheet copy, or the
    // duplicate ends up with neither embedded bytes nor a link target -- an empty/invisible drawing.
    [Fact]
    public void DuplicateSheet_LinkedImagePicture_PreservesLinkedImageTargetOnCopy()
    {
        var workbook = new Workbook("PictureCloneLinkedTarget");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "LinkedPic",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            LinkedImageTarget = "file:///C:/Images/photo.png",
            ImageBytes = null,
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.LinkedImageTarget.Should().Be("file:///C:/Images/photo.png",
            "a Link to File picture's target must not be dropped by Duplicate Sheet");
    }

    // R82-meta-2 (bug case b): a picture inserted via Excel's Insert > Icons/SVG carries a vector
    // SvgImageBytes part alongside its PNG fallback in ImageBytes; the copy must keep SvgImageBytes
    // or the duplicate permanently downgrades to a flat raster on save.
    [Fact]
    public void DuplicateSheet_SvgPicture_PreservesSvgImageBytesOnCopy()
    {
        var workbook = new Workbook("PictureCloneSvgBytes");
        var sheet = workbook.AddSheet("Sheet1");
        var svgBytes = new byte[] { 1, 2, 3, 4 };
        sheet.Pictures.Add(new PictureModel
        {
            Name = "SvgPic",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [9, 9, 9],
            ContentType = "image/png",
            SvgImageBytes = svgBytes,
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.SvgImageBytes.Should().BeEquivalentTo(svgBytes,
            "an SVG picture's vector part must not be dropped by Duplicate Sheet");
    }

    // Sibling no-regression case: a plain embedded raster picture (no LinkedImageTarget, no
    // SvgImageBytes) must still duplicate cleanly with both new fields left null.
    [Fact]
    public void DuplicateSheet_PlainRasterPicture_LeavesNewFieldsNull()
    {
        var workbook = new Workbook("PictureClonePlainRaster");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "PlainPic",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [7, 7, 7],
            ContentType = "image/png",
            Width = 100,
            Height = 60
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedPicture = workbook.Sheets[1].Pictures.Should().ContainSingle().Subject;
        copiedPicture.LinkedImageTarget.Should().BeNull();
        copiedPicture.SvgImageBytes.Should().BeNull();
        copiedPicture.ImageBytes.Should().BeEquivalentTo(new byte[] { 7, 7, 7 });
    }
}
