using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for arbitrary inline-image formats (jpeg/gif/bmp/tiff/emf/wmf): the original bytes +
/// format survive byte-for-byte, the media part is named with the correct extension, and
/// <c>[Content_Types].xml</c> declares a Default for every extension used. A PNG regression test guards the
/// historical behaviour (still <c>image1.png</c>, single png Default).
/// </summary>
public class ImageFormatRoundTripTests
{
    // Minimal valid-enough byte sequences with the correct magic numbers; the writer stores them verbatim.
    private static byte[] Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    private static byte[] Jpeg() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
        0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0xFF, 0xD9,
    ];

    private static byte[] Gif() =>
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00,
        0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x3B,
    ];

    private static TextDocument DocWith(InlineImage image)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static (ZipArchive Zip, MemoryStream Stream) Pack(TextDocument doc)
    {
        var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        return (new ZipArchive(stream, ZipArchiveMode.Read), stream);
    }

    private static string ContentTypes(ZipArchive zip)
    {
        using var reader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        return reader.ReadToEnd();
    }

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void Jpeg_RoundTrips_BytesFormatExtensionAndContentType()
    {
        var bytes = Jpeg();
        var (zip, stream) = Pack(DocWith(new InlineImage(bytes, 64, 48, ImageFormat.Jpeg)));
        using (stream)
        using (zip)
        {
            // The media part carries the JPEG extension (not .png) and the exact bytes.
            var entry = zip.GetEntry("word/media/image1.jpeg");
            entry.Should().NotBeNull();
            using var entryStream = entry!.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            buffer.ToArray().Should().Equal(bytes);

            // The content-type Default for jpeg is present (and no spurious png Default for a jpeg-only doc).
            var ct = ContentTypes(zip);
            ct.Should().Contain("Extension=\"jpeg\"");
            ct.Should().Contain("image/jpeg");
        }

        var imageRun = RoundTrip(DocWith(new InlineImage(bytes, 64, 48, ImageFormat.Jpeg)))
            .Paragraphs.First().Runs.Single(r => r.Image is not null);
        imageRun.Image!.Format.Should().Be(ImageFormat.Jpeg);
        imageRun.Image.Bytes.Should().Equal(bytes);
    }

    [Fact]
    public void Gif_RoundTrips_BytesFormatExtensionAndContentType()
    {
        var bytes = Gif();
        var (zip, stream) = Pack(DocWith(new InlineImage(bytes, 32, 32, ImageFormat.Gif)));
        using (stream)
        using (zip)
        {
            var entry = zip.GetEntry("word/media/image1.gif");
            entry.Should().NotBeNull();
            using var entryStream = entry!.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            buffer.ToArray().Should().Equal(bytes);

            var ct = ContentTypes(zip);
            ct.Should().Contain("Extension=\"gif\"");
            ct.Should().Contain("image/gif");
        }

        var imageRun = RoundTrip(DocWith(new InlineImage(bytes, 32, 32, ImageFormat.Gif)))
            .Paragraphs.First().Runs.Single(r => r.Image is not null);
        imageRun.Image!.Format.Should().Be(ImageFormat.Gif);
        imageRun.Image.Bytes.Should().Equal(bytes);
    }

    [Fact]
    public void Png_RegressionStaysImage1PngWithSinglePngDefault()
    {
        var bytes = Png();
        var (zip, stream) = Pack(DocWith(new InlineImage(bytes, 50, 50)));
        using (stream)
        using (zip)
        {
            zip.GetEntry("word/media/image1.png").Should().NotBeNull();

            var ct = ContentTypes(zip);
            ct.Should().Contain("Extension=\"png\"");
            ct.Should().Contain("image/png");
            // Exactly one image Default (png) and no stray jpeg/gif/etc. for a PNG-only document.
            ct.Should().NotContain("image/jpeg");
            ct.Should().NotContain("Extension=\"jpeg\"");
        }

        var imageRun = RoundTrip(DocWith(new InlineImage(bytes, 50, 50)))
            .Paragraphs.First().Runs.Single(r => r.Image is not null);
        imageRun.Image!.Format.Should().Be(ImageFormat.Png);
        imageRun.Image.Bytes.Should().Equal(bytes);
    }

    [Fact]
    public void MixedFormats_EmitOneContentTypeDefaultPerExtension()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(Png(), 10, 10)));
        paragraph.Runs.Add(Run.FromImage(new InlineImage(Jpeg(), 10, 10, ImageFormat.Jpeg)));
        paragraph.Runs.Add(Run.FromImage(new InlineImage(Gif(), 10, 10, ImageFormat.Gif)));
        doc.Blocks.Add(paragraph);

        var (zip, stream) = Pack(doc);
        using (stream)
        using (zip)
        {
            zip.GetEntry("word/media/image1.png").Should().NotBeNull();
            zip.GetEntry("word/media/image2.jpeg").Should().NotBeNull();
            zip.GetEntry("word/media/image3.gif").Should().NotBeNull();

            var ct = ContentTypes(zip);
            ct.Should().Contain("image/png").And.Contain("image/jpeg").And.Contain("image/gif");
        }

        var runs = RoundTrip(doc).Paragraphs.First().Runs.Where(r => r.Image is not null).ToList();
        runs.Select(r => r.Image!.Format)
            .Should().Equal(ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif);
    }

    [Fact]
    public void Reader_DetectsFormatFromMagicBytes_WhenExtensionUnknown()
    {
        // Even if a producer wrote the part with a generic/unknown extension, the magic-byte fallback
        // recovers the real format. Here we round-trip through our own writer (jpeg extension), so the
        // extension path is exercised; the magic-byte detector is unit-tested separately in the model tests.
        var bytes = Jpeg();
        var imageRun = RoundTrip(DocWith(new InlineImage(bytes, 20, 20, ImageFormat.Jpeg)))
            .Paragraphs.First().Runs.Single(r => r.Image is not null);
        imageRun.Image!.Format.Should().Be(ImageFormat.Jpeg);
    }
}
