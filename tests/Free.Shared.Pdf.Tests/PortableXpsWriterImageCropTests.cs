using System.IO.Compression;
using FluentAssertions;
using Free.Shared.Pdf;
using Free.Shared.Xps;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// XPS expresses an <c>a:srcRect</c> crop as an ImageBrush <c>Viewbox</c> over the source bounding
/// box; negative insets outset instead, which shrinks the destination rectangle. Both halves come
/// from the same plan the PDF adapters use.
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

        xml.Should().Contain("Viewbox=\"0.25,0.125,0.5,0.5\"");
        xml.Should().Contain("Viewport=\"100,552,80,40\"", "a positive crop still fills the frame");
        xml.Should().Contain("Data=\"M 100,552 L 180,552 L 180,592 L 100,592 Z\"");
    }

    [Fact]
    public void WriteToBytes_NegativeCropLetterboxesTheDestinationAndKeepsTheWholeViewbox()
    {
        // l=-25000 outsets: the frame spans source fractions [-0.25, 1], so the whole bitmap covers
        // the right 80% of the 80pt-wide frame (64pt starting 16pt in) and the left 20% stays empty.
        var xml = WritePageXml(new PdfImageSourceCrop(-0.25, 0, 0, 0));

        xml.Should().Contain("Viewbox=\"0,0,1,1\"", "an outset crops nothing off the bitmap");
        xml.Should().Contain("Viewport=\"116,552,64,40\"");
        xml.Should().Contain(
            "Data=\"M 116,552 L 180,552 L 180,592 L 116,592 Z\"",
            "the painted geometry shrinks so the padding is left unmarked");
    }

    [Fact]
    public void WriteToBytes_UncroppedImageIsUnchanged()
    {
        var xml = WritePageXml(default);

        xml.Should().Contain("Viewbox=\"0,0,1,1\"");
        xml.Should().Contain("Viewport=\"100,552,80,40\"");
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

    private static PdfContentDocument DocumentWith(PdfImageSourceCrop crop) =>
        new([
            new PdfContentPage(PageWidth, PageHeight, [
                new PdfImage(
                    100,
                    200,
                    80,
                    40,
                    MinimalJpegBytes(),
                    "image/jpeg",
                    SourceCrop: crop),
            ])]);

    private static string WritePageXml(PdfImageSourceCrop crop)
    {
        var bytes = PortableXpsWriter.WriteToBytes(DocumentWith(crop));
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
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
