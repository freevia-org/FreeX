using System.Buffers.Binary;
using System.IO.Compression;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Xps;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// XPS expresses a picture crop as an absolute ImageBrush <c>Viewbox</c>. A negative inset
/// instead shrinks the destination rectangle, preserving the shared PDF crop plan.
/// </summary>
public sealed class PortableXpsWriterImageCropTests
{
    private const double PageWidth = 612;
    private const double PageHeight = 792;

    [Fact]
    public void Analyze_CroppedImageIsExportable()
    {
        var report = PortableXpsWriter.Analyze(
            DocumentWith(new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375)));

        report.IsExportable.Should().BeTrue();
        report.ImageOperationCount.Should().Be(1);
        report.Requirements.Should().BeEmpty();
    }

    [Fact]
    public void WriteToBytes_PositiveCropNarrowsTheViewboxAndKeepsTheFrame()
    {
        // 16x16 source, crop l=.25 t=.125 r=.25 b=.375 -> source pixels (4,2,8,8).
        var xml = WritePageXml(new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375));

        xml.Should().Contain("Viewbox=\"4,2,8,8\"").And.Contain("ViewboxUnits=\"Absolute\"");
        xml.Should().Contain("Viewport=\"100,552,80,40\"", "a positive crop still fills the frame");
        xml.Should().Contain("Data=\"M 100,552 L 180,552 L 180,592 L 100,592 Z\"");
    }

    [Fact]
    public void WriteToBytes_NegativeCropLetterboxesTheDestinationAndKeepsTheWholeViewbox()
    {
        // l=-25000 outsets: the frame spans source fractions [-0.25, 1], so the whole bitmap covers
        // the right 80% of the 80pt-wide frame (64pt starting 16pt in) and the left 20% stays empty.
        var xml = WritePageXml(new PdfImageSourceCrop(-0.25, 0, 0, 0));

        xml.Should().Contain("Viewbox=\"0,0,16,16\"", "an outset crops nothing off the bitmap");
        xml.Should().Contain("Viewport=\"116,552,64,40\"");
        xml.Should().Contain(
            "Data=\"M 116,552 L 180,552 L 180,592 L 116,592 Z\"",
            "the painted geometry shrinks so the padding is left unmarked");
    }

    [Fact]
    public void WriteToBytes_UncroppedImageIsUnchanged()
    {
        var xml = WritePageXml(default);

        xml.Should().Contain("Viewbox=\"0,0,16,16\"");
        xml.Should().Contain("Viewport=\"100,552,80,40\"");
    }

    [Fact]
    public void WriteToBytes_JpegDensityScalesEachAxisOfTheAbsoluteViewbox()
    {
        var jpeg = MinimalJpegBytes();
        jpeg[13] = 1; // JFIF stores densities in dots per inch when this unit byte is 1.
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(14), 192);
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(16), 96);

        var xml = WritePageXml(new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375), jpeg);

        xml.Should().Contain("Viewbox=\"2,2,4,8\"");
        xml.Should().Contain("Viewport=\"100,552,80,40\"");
    }

    [Fact]
    public void WriteToBytes_UsesExifDensityWhenJpegHasNoAbsoluteJfifDensity()
    {
        var jpeg = MinimalJpegBytes();
        jpeg[13] = 0; // Use the JFIF fields as pixel aspect ratio, so EXIF supplies density.
        byte[] exifSegment =
        [
            0xFF, 0xE1, 0, 74, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, // Little-endian TIFF, first IFD at byte 8.
            3, 0,
            0x1A, 1, 5, 0, 1, 0, 0, 0, 50, 0, 0, 0, // XResolution at byte 50.
            0x1B, 1, 5, 0, 1, 0, 0, 0, 58, 0, 0, 0, // YResolution at byte 58.
            0x28, 1, 3, 0, 1, 0, 0, 0, 2, 0, 0, 0, // ResolutionUnit = inch.
            0, 0, 0, 0,
            192, 0, 0, 0, 1, 0, 0, 0,
            96, 0, 0, 0, 1, 0, 0, 0,
        ];
        byte[] jpegWithExif = [.. jpeg.AsSpan(0, 20), .. exifSegment, .. jpeg.AsSpan(20)];

        var xml = WritePageXml(default, jpegWithExif);

        xml.Should().Contain("Viewbox=\"0,0,8,16\"");
    }

    [Fact]
    public void WriteToBytes_PngDensityScalesTheAbsoluteViewbox()
    {
        // Add IHDR's remaining payload/CRC and an absolute pHYs chunk. The writer
        // preserves the encoded PNG and only inspects its header for these assertions.
        var png = new byte[54];
        PngWithSize(16, 16).CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(33), 9);
        "pHYs"u8.CopyTo(png.AsSpan(37));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(41), 15000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(45), 15000);
        png[49] = 1;

        var xml = WritePageXml(default, png, "image/png");

        // 15000 px/m is 381 dpi, so 16 pixels occupy about 4.0315 units.
        xml.Should().Contain("Viewbox=\"0,0,4.031496062992,4.031496062992\"");
    }

    [Fact]
    public void Analyze_CropOnUnreadableBytesStaysUnsupported()
    {
        var document = new PdfContentDocument([
            new PdfContentPage(PageWidth, PageHeight, [
                new PdfImage(
                    100,
                    200,
                    80,
                    40,
                    [1, 2, 3, 4],
                    "image/png",
                    SourceCrop: new PdfImageSourceCrop(0.25, 0, 0, 0)),
            ])]);

        var report = PortableXpsWriter.Analyze(document);

        report.IsExportable.Should().BeFalse(
            "a crop cannot be placed without the source pixel grid, and guessing would emit the whole image");
        var write = () => PortableXpsWriter.WriteToBytes(document);
        write.Should().Throw<XpsUnsupportedContentException>();
    }

    [Fact]
    public void ImageDimensions_ReadPngAndJpegSizesWithoutDecodingPixels()
    {
        PdfImageDimensions.TryReadSize(PngWithSize(40, 20), "image/png", out var pngWidth, out var pngHeight)
            .Should().BeTrue();
        pngWidth.Should().Be(40);
        pngHeight.Should().Be(20);

        PdfImageDimensions.TryReadSize(MinimalJpegBytes(), "image/jpeg", out var jpegWidth, out var jpegHeight)
            .Should().BeTrue();
        jpegWidth.Should().Be(16);
        jpegHeight.Should().Be(16);

        PdfImageDimensions.TryReadSize([1, 2, 3], "image/png", out _, out _).Should().BeFalse();
        PdfImageDimensions.TryReadSize(MinimalJpegBytes(), "image/gif", out _, out _).Should().BeFalse();
    }

    private static PdfContentDocument DocumentWith(
        PdfImageSourceCrop crop, byte[]? bytes = null, string contentType = "image/jpeg") =>
        new([
            new PdfContentPage(PageWidth, PageHeight, [
                new PdfImage(
                    100,
                    200,
                    80,
                    40,
                    bytes ?? MinimalJpegBytes(),
                    contentType,
                    SourceCrop: crop),
            ])]);

    private static string WritePageXml(
        PdfImageSourceCrop crop, byte[]? bytes = null, string contentType = "image/jpeg")
    {
        var xpsBytes = PortableXpsWriter.WriteToBytes(DocumentWith(crop, bytes, contentType));
        using var archive = new ZipArchive(new MemoryStream(xpsBytes), ZipArchiveMode.Read);
        var page = archive.GetEntry("Documents/1/Pages/1.fpage");
        page.Should().NotBeNull();
        using var reader = new StreamReader(page!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>A PNG carrying only a valid signature and IHDR; the writer copies bytes verbatim.</summary>
    private static byte[] PngWithSize(int width, int height)
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        ];
        header.CopyTo(bytes);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }

    private static byte[] MinimalJpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");
}
