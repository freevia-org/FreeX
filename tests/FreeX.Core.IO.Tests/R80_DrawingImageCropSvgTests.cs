using System.Text;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-80 bucket io-drawing-image-5 fix verification.
/// </summary>
public sealed class R80_DrawingImageCropSvgTests
{
    // R80-io-drawing-image-5-2: the read/write source-rectangle conversions clamped every
    // srcRect inset to [0, 1], silently flooring a NEGATIVE (outward/"crop past the image edge")
    // inset to 0 on both read and write, and HasPictureCrop's "> 0" check meant a picture cropped
    // ONLY outward (all insets <= 0) was treated as having no crop at all and lost its srcRect
    // entirely on save.
    [Fact]
    public void NegativeSourceRectangleInset_RoundTripsThroughXlsxSave()
    {
        var adapter = new XlsxFileAdapter();

        var workbook = new Workbook("NegativeCropRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            // Outward crop (padding) on the left/top only -- a common way to add a border/matte
            // around a picture, or to compensate for an image smaller than its frame.
            CropLeft = -0.15,
            CropTop = -0.10,
            CropRight = 0,
            CropBottom = 0
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var picture = reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle().Subject;
        picture.CropLeft.Should().BeApproximately(-0.15, 0.0005,
            "an outward (negative) crop on the left edge is a valid Excel srcRect value and must not " +
            "be floored to 0 on round trip");
        picture.CropTop.Should().BeApproximately(-0.10, 0.0005,
            "an outward (negative) crop on the top edge is a valid Excel srcRect value and must not " +
            "be floored to 0 on round trip");
        picture.CropRight.Should().Be(0);
        picture.CropBottom.Should().Be(0);
    }

    // No-regression sibling: an ordinary positive (inward) crop, and a picture with NO crop at all,
    // must keep behaving exactly as before -- positive insets still round-trip, and a fully uncropped
    // picture still emits no srcRect (HasPictureCrop's widened "!= 0" check must not treat 0 as crop).
    [Fact]
    public void PositiveOrAbsentSourceRectangleInset_StillRoundTripsCorrectly()
    {
        var adapter = new XlsxFileAdapter();

        var workbook = new Workbook("PositiveAndNoCropRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "CroppedPhoto",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            CropLeft = 0.10,
            CropTop = 0.20,
            CropRight = 0.05,
            CropBottom = 0.15
        });
        sheet.Pictures.Add(new PictureModel
        {
            Name = "UncroppedPhoto",
            Anchor = new CellAddress(sheet.Id, 6, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        var cropped = reloadedSheet.Pictures.Single(p => p.Name == "CroppedPhoto");
        cropped.CropLeft.Should().BeApproximately(0.10, 0.0005);
        cropped.CropTop.Should().BeApproximately(0.20, 0.0005);
        cropped.CropRight.Should().BeApproximately(0.05, 0.0005);
        cropped.CropBottom.Should().BeApproximately(0.15, 0.0005);

        var uncropped = reloadedSheet.Pictures.Single(p => p.Name == "UncroppedPhoto");
        uncropped.CropLeft.Should().Be(0);
        uncropped.CropTop.Should().Be(0);
        uncropped.CropRight.Should().Be(0);
        uncropped.CropBottom.Should().Be(0);
    }

    // R80-io-drawing-image-5-3: a picture inserted via Excel's Insert > Icons/SVG carries a PNG
    // rasterization as the universal-compatibility fallback (ImageBytes/ContentType) AND a vector
    // .svg part referenced through the picture's <a:blip><a:extLst> asvg:svgBlip extension so the
    // picture stays editable as a vector in Excel. The reader never looked at that extension at all,
    // and the writer never re-emitted it, so any edit that cleared IsSourceLoaded (crop/rotate/
    // recolor/resize) permanently downgraded the picture to a flat raster.
    [Fact]
    public void SvgBlipWithPngFallback_RoundTripsBothPartsThroughXlsxSave()
    {
        var adapter = new XlsxFileAdapter();
        var svgBytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>");

        var workbook = new Workbook("SvgFallbackRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Icon",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            SvgImageBytes = svgBytes,
            Width = 48,
            Height = 48
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var picture = reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle().Subject;
        picture.ImageBytes.Should().BeEquivalentTo(MinimalPngBytes(),
            "the PNG universal-compatibility fallback must still be present after save/reload");
        picture.SvgImageBytes.Should().NotBeNull(
            "the vector .svg part referenced by asvg:svgBlip must survive a save/reload round trip, " +
            "not be silently dropped in favor of the flat PNG fallback");
        picture.SvgImageBytes.Should().BeEquivalentTo(svgBytes,
            "the round-tripped vector part must be byte-identical to the originally authored SVG");
    }

    // No-regression sibling: an ordinary raster picture with NO SVG fallback must round-trip exactly
    // as before, with SvgImageBytes staying null (the new field must not spuriously appear).
    [Fact]
    public void OrdinaryRasterPictureWithNoSvgFallback_StillRoundTripsWithNullSvgBytes()
    {
        var adapter = new XlsxFileAdapter();

        var workbook = new Workbook("NoSvgFallbackRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Photo",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 48,
            Height = 48
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var picture = reloaded.GetSheet("Sheet1")!.Pictures.Should().ContainSingle().Subject;
        picture.ImageBytes.Should().BeEquivalentTo(MinimalPngBytes());
        picture.SvgImageBytes.Should().BeNull(
            "an ordinary raster picture with no vector fallback must not spuriously gain SvgImageBytes");
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
