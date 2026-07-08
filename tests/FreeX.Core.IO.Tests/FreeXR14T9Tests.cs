using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-14 bucket T9 fix verification.
/// See docs/../scratchpad r14-T9.md for the full finding text.
/// </summary>
public sealed class FreeXR14T9Tests
{
    // R14-cell-styles-themes-1: NativeJsonAdapter's CellStyleDto had no ReadingOrder property, so
    // ToCellStyle/FromCellStyle never round-tripped CellStyle.ReadingOrder and an explicit RTL
    // alignment override was silently replaced by the Context (locale) default on a native .fxl
    // save/reload cycle.
    [Fact]
    public void NativeJsonAdapter_RightToLeftReadingOrderCell_RoundTrips()
    {
        var workbook = new Workbook("ReadingOrderNativeRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("שלום")));
        var styleId = workbook.RegisterStyle(new CellStyle { ReadingOrder = CellReadingOrder.RightToLeft });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);
        reloadedStyle.ReadingOrder.Should().Be(CellReadingOrder.RightToLeft,
            "a per-cell RTL readingOrder override must survive a full save/reload round trip through native .fxl, " +
            "not silently fall back to the Context/locale default");
    }

    // R14-image-media-1: XlsxSourceDrawingGeometryRewriter only rewrote a source-loaded picture's
    // anchor geometry (position/size). Crop (srcRect), rotation/flip (xfrm rot/flipH/flipV), and alt
    // text (cNvPr descr) were never patched into the preserved drawing XML, so those edits were
    // silently discarded on save while the picture kept replaying its original, unedited appearance.
    [Fact]
    public void SourceLoadedPicture_CropRotationFlipAltTextEdits_SurviveSave()
    {
        var adapter = new XlsxFileAdapter();

        var workbook1 = new Workbook("PictureVisualEditRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.Pictures.Add(new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            AltText = "Original alt text"
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        // Reload so the picture becomes source-loaded/preserved, exactly like opening a real .xlsx.
        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        var picture = reloadedSheet1.Pictures.Should().ContainSingle().Subject;
        picture.IsSourceLoaded.Should().BeTrue("the picture came from the source package on reload");

        // Apply the same edits SetPictureCropCommand / RotatePictureCommand / ResizePictureCommand
        // (flip) / SetPictureAltTextCommand make to the in-memory model.
        picture.CropLeft = 0.10;
        picture.CropTop = 0.20;
        picture.CropRight = 0.05;
        picture.CropBottom = 0.15;
        picture.RotationDegrees = 45;
        picture.FlipHorizontal = true;
        picture.FlipVertical = true;
        picture.AltText = "Edited alt text";

        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);
        var finalPicture = reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle().Subject;

        finalPicture.CropLeft.Should().BeApproximately(0.10, 0.0005,
            "a crop edit on a source-loaded picture must survive save, not replay the original uncropped source");
        finalPicture.CropTop.Should().BeApproximately(0.20, 0.0005);
        finalPicture.CropRight.Should().BeApproximately(0.05, 0.0005);
        finalPicture.CropBottom.Should().BeApproximately(0.15, 0.0005);
        finalPicture.RotationDegrees.Should().Be(45,
            "a rotation edit on a source-loaded picture must survive save, not replay the original unrotated source");
        finalPicture.FlipHorizontal.Should().BeTrue(
            "a horizontal-flip edit on a source-loaded picture must survive save");
        finalPicture.FlipVertical.Should().BeTrue(
            "a vertical-flip edit on a source-loaded picture must survive save");
        finalPicture.AltText.Should().Be("Edited alt text",
            "an alt-text edit on a source-loaded picture must survive save, not replay the original alt text");
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
