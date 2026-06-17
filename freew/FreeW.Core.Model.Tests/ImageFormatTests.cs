namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="ImageFormat"/> field on <see cref="InlineImage"/> plus the
/// magic-byte detector and extension mapping that let arbitrary picture formats (jpeg/gif/bmp/tiff/emf/wmf)
/// round-trip through docx without transcoding. PNG remains the default so existing construction is unchanged.
/// </summary>
public class ImageFormatTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static byte[] Gif() => [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00];

    [Fact]
    public void NewImage_DefaultsToPng()
    {
        var image = new InlineImage(Png(), widthPt: 100, heightPt: 50);

        image.Format.Should().Be(ImageFormat.Png);
        // PngBytes alias still returns the raw bytes for source compatibility.
        image.PngBytes.Should().Equal(image.Bytes);
    }

    [Fact]
    public void Format_IsCarried_WhenSpecified()
    {
        var bytes = Jpeg();
        var image = new InlineImage(bytes, 10, 10, ImageFormat.Jpeg);

        image.Format.Should().Be(ImageFormat.Jpeg);
        image.Bytes.Should().BeSameAs(bytes);
        image.PngBytes.Should().BeSameAs(bytes);
    }

    [Theory]
    [InlineData(ImageFormat.Png, "png")]
    [InlineData(ImageFormat.Jpeg, "jpeg")]
    [InlineData(ImageFormat.Gif, "gif")]
    [InlineData(ImageFormat.Bmp, "bmp")]
    [InlineData(ImageFormat.Tiff, "tiff")]
    [InlineData(ImageFormat.Emf, "emf")]
    [InlineData(ImageFormat.Wmf, "wmf")]
    public void ExtensionFor_MapsEveryFormat(ImageFormat format, string expected) =>
        InlineImage.ExtensionFor(format).Should().Be(expected);

    [Theory]
    [InlineData("png", ImageFormat.Png)]
    [InlineData(".PNG", ImageFormat.Png)]
    [InlineData("jpg", ImageFormat.Jpeg)]
    [InlineData("jpeg", ImageFormat.Jpeg)]
    [InlineData("GIF", ImageFormat.Gif)]
    [InlineData("bmp", ImageFormat.Bmp)]
    [InlineData("tif", ImageFormat.Tiff)]
    [InlineData("tiff", ImageFormat.Tiff)]
    [InlineData("emf", ImageFormat.Emf)]
    [InlineData("wmf", ImageFormat.Wmf)]
    public void FormatForExtension_MapsKnownExtensions(string extension, ImageFormat expected) =>
        InlineImage.FormatForExtension(extension).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("svg")]
    [InlineData("xml")]
    public void FormatForExtension_ReturnsNull_ForUnknown(string? extension) =>
        InlineImage.FormatForExtension(extension).Should().BeNull();

    [Fact]
    public void DetectFormat_RecognisesPng() =>
        InlineImage.DetectFormat(Png()).Should().Be(ImageFormat.Png);

    [Fact]
    public void DetectFormat_RecognisesJpeg() =>
        InlineImage.DetectFormat(Jpeg()).Should().Be(ImageFormat.Jpeg);

    [Fact]
    public void DetectFormat_RecognisesGif() =>
        InlineImage.DetectFormat(Gif()).Should().Be(ImageFormat.Gif);

    [Fact]
    public void DetectFormat_RecognisesBmp() =>
        InlineImage.DetectFormat([0x42, 0x4D, 0x00, 0x00]).Should().Be(ImageFormat.Bmp);

    [Theory]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 })] // little-endian TIFF
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A })] // big-endian TIFF
    public void DetectFormat_RecognisesTiff(byte[] bytes) =>
        InlineImage.DetectFormat(bytes).Should().Be(ImageFormat.Tiff);

    [Fact]
    public void DetectFormat_RecognisesEmf()
    {
        // EMF: 0x00000001 record type then " EMF" at byte offset 40.
        var bytes = new byte[44];
        bytes[0] = 0x01;
        bytes[40] = 0x20; bytes[41] = 0x45; bytes[42] = 0x4D; bytes[43] = 0x46;
        InlineImage.DetectFormat(bytes).Should().Be(ImageFormat.Emf);
    }

    [Theory]
    [InlineData(new byte[] { 0xD7, 0xCD, 0xC6, 0x9A })] // placeable WMF header
    [InlineData(new byte[] { 0x01, 0x00, 0x09, 0x00 })] // classic WMF header
    public void DetectFormat_RecognisesWmf(byte[] bytes) =>
        InlineImage.DetectFormat(bytes).Should().Be(ImageFormat.Wmf);

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x12, 0x34, 0x56, 0x78 })]
    public void DetectFormat_FallsBackToPng_ForUnrecognised(byte[] bytes) =>
        InlineImage.DetectFormat(bytes).Should().Be(ImageFormat.Png);
}
