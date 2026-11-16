using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Pdf.Skia;
using SkiaSharp;

namespace Free.Shared.Pdf.Tests;

public sealed class SkiaXpsWriterTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";

    [Fact]
    public void WriteToBytes_LeavesAnEffectRasterBetweenNativeFillAndVisibleGlyphs()
    {
        const string text = "Budget café";
        var red = new PdfColor(220, 30, 20);
        var rotated = new PdfRotationGroup(28, 40, 90, [
            new PdfText(28, 40, 10, PdfFontFace.Regular, PdfColor.Black, text, "Arial"),
        ], FlipH: true);
        var page = new PdfContentPage(72, 72, [
            new PdfFillRect(2, 2, 30, 24, red),
            new PdfEffectGroup(PdfEffectKind.Shadow, 12, 30, 50, 24,
                new PdfEffectParameters(PdfColor.Black, 0.7, 2, 1, -1), [rotated]),
            new PdfClipGroup(12, 30, 50, 24, [rotated]),
            new PdfOpacityGroup(0, [
                new PdfText(28, 40, 10, PdfFontFace.Regular, PdfColor.Black, "Invisible", "Arial"),
            ]),
        ]);
        var diagnostics = new List<string>();

        var bytes = SkiaXpsWriter.WriteToBytes(new PdfContentDocument([page]), diagnostics);
        diagnostics.Should().BeEmpty();
        using var package = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var pagePart = package.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        var fixedPage = XDocument.Load(pagePart);
        var imageBrush = fixedPage.Descendants(Xps + "ImageBrush").Should().ContainSingle().Which;
        var imagePath = imageBrush.Attribute("ImageSource")!.Value.TrimStart('/');
        using var artwork = SKBitmap.Decode(ReadPart(package, imagePath))
            ?? throw new InvalidOperationException("The XPS artwork could not be decoded.");
        artwork.Width.Should().BeLessThan(144);
        artwork.Height.Should().BeLessThan(144);
        artwork.Pixels.Should().Contain(pixel => pixel.Alpha > 0);
        artwork.Pixels.Should().Contain(pixel => pixel.Alpha == 0);
        fixedPage.Descendants(Xps + "Path").Should().Contain(path =>
            (string?)path.Attribute("Fill") == "#DC1E14");

        var glyph = fixedPage.Descendants(Xps + "Glyphs").Should().ContainSingle().Which;
        glyph.Attribute("UnicodeString")!.Value.Should().Be(text);
        glyph.Attribute("OriginX")!.Value.Should().Be("28");
        glyph.Attribute("OriginY")!.Value.Should().Be("32");
        glyph.Attribute("Fill")!.Value.Should().Be("#000000");
        var rotation = glyph.Parent!;
        var clip = rotation.Parent!;
        rotation.Attribute("RenderTransform").Should().NotBeNull();
        clip.Attribute("Clip").Should().NotBeNull();
        clip.ElementsBeforeSelf().Should().Contain(imageBrush.Parent!.Parent!);

        var fontUri = glyph.Attribute("FontUri")!.Value.TrimStart('/').Split('#');
        var collectionIndex = fontUri.Length == 1 ? 0 : int.Parse(fontUri[1], CultureInfo.InvariantCulture);
        var fontPath = fontUri[0];
        var fontBytes = ReadPart(package, fontPath);
        var key = Convert.FromHexString(Path.GetFileNameWithoutExtension(fontPath).Replace("-", ""));
        for (var index = 0; index < 32; index++)
            fontBytes[index] ^= key[15 - index % 16];
        using var fontData = SKData.CreateCopy(fontBytes);
        using var typeface = SKTypeface.FromData(fontData, collectionIndex);
        typeface.Should().NotBeNull();
        var glyphIds = glyph.Attribute("Indices")!.Value.Split(';')
            .Select(entry => ushort.Parse(entry.Split(',')[0], CultureInfo.InvariantCulture));
        glyphIds.Should().Equal(text.Select(character => typeface!.GetGlyph(character)));
    }

    [Fact]
    public void WriteToBytes_KeepsOriginalImageAndNativeDashedBorder()
    {
        using var source = new SKBitmap(new SKImageInfo(8, 6));
        source.Erase(SKColors.DarkCyan);
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        var sourcePng = encoded.ToArray();
        var dash = new PdfDashPattern([6, 2], Phase: 1);
        var page = new PdfContentPage(72, 72, [
            new PdfImage(4, 8, 20, 12, sourcePng, "image/png"),
            new PdfStrokeRect(4, 8, 20, 12, PdfColor.Black, 2, dash),
            new PdfText(4, 42, 12, PdfFontFace.Bold, PdfColor.Black, "Quarterly report", "Arial"),
        ]);

        using var package = new ZipArchive(new MemoryStream(SkiaXpsWriter.WriteToBytes(new PdfContentDocument([page]))));
        using var pagePart = package.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        var fixedPage = XDocument.Load(pagePart);
        var imageBrush = fixedPage.Descendants(Xps + "ImageBrush").Should().ContainSingle().Which;
        ReadPart(package, imageBrush.Attribute("ImageSource")!.Value.TrimStart('/')).Should().Equal(sourcePng);
        var dashedPath = fixedPage.Descendants(Xps + "Path").Should()
            .ContainSingle(path => path.Attribute("StrokeDashArray") != null).Which;
        dashedPath.Attribute("StrokeDashArray")!.Value.Should().Be("3 1");
        dashedPath.Attribute("StrokeDashOffset")!.Value.Should().Be("0.5");
        fixedPage.Descendants(Xps + "Glyphs").Should().ContainSingle().Which
            .Attribute("Fill")!.Value.Should().Be("#000000");
    }

    [Fact]
    public void WriteToBytes_RasterizesDashPatternsTheDirectWriterCannotRepresent()
    {
        var page = new PdfContentPage(72, 72,
            [new PdfStrokeRect(4, 8, 20, 12, PdfColor.Black, 2, new PdfDashPattern([6, 3], 1e13))]);
        using var package = new ZipArchive(new MemoryStream(SkiaXpsWriter.WriteToBytes(new PdfContentDocument([page]))));
        using var pagePart = package.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        var fixedPage = XDocument.Load(pagePart);
        fixedPage.Descendants(Xps + "ImageBrush").Should().NotBeEmpty();
        fixedPage.Descendants(Xps + "Path").Should().NotContain(path => path.Attribute("StrokeDashArray") != null);
    }

    [Fact]
    public void WriteToBytes_UsesNativeBrushForGradientOnlyPath()
    {
        var gradient = new PdfLinearGradient(0, 0, 40, 0, [new(0, PdfColor.Black), new(1, new PdfColor(255, 0, 0))]);
        var contour = new PdfPathContour(new(0, 0), [PdfPathSegment.LineTo(new(40, 0)), PdfPathSegment.LineTo(new(40, 40))], true);
        var page = new PdfContentPage(72, 72, [new PdfPathLinearGradient([contour], gradient, null, gradient, null, 2)]);
        using var package = new ZipArchive(new MemoryStream(SkiaXpsWriter.WriteToBytes(new PdfContentDocument([page]))));
        using var pagePart = package.GetEntry("Documents/1/Pages/1.fpage")!.Open();
        var fixedPage = XDocument.Load(pagePart);
        fixedPage.Descendants(Xps + "LinearGradientBrush").Should().HaveCount(2);
        fixedPage.Descendants(Xps + "ImageBrush").Should().BeEmpty();
    }

    [Theory]
    [InlineData(0x0000, "odttf", false)]
    [InlineData(0x0004, "odttf", true)]
    [InlineData(0x0008, "odttf", false)]
    [InlineData(0x0100, "odttf", false)]
    [InlineData(0x0002, null, false)]
    [InlineData(0x0200, null, false)]
    public void FontResource_RespectsEmbeddingPermissions(int flags, string? extension, bool restricted)
    {
        var bytes = new byte[64];
        WriteFontDirectory(bytes, 0, (ushort)flags);

        SkiaXpsFontResources.GetEmbeddableExtension(bytes, 0, out var isRestricted)
            .Should().Be(extension);
        isRestricted.Should().Be(restricted);
    }

    [Fact]
    public void FontResource_ValidatesAllPackagedCollectionFaces()
    {
        var bytes = new byte[148];
        "ttcf"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 20);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 84);
        WriteFontDirectory(bytes, 20, 0x0004);
        WriteFontDirectory(bytes, 84, 0x0008);

        SkiaXpsFontResources.GetEmbeddableExtension(bytes, 1, out var isRestricted)
            .Should().Be("odttc");
        isRestricted.Should().BeTrue();

        WriteFontDirectory(bytes, 20, 0x0002);

        SkiaXpsFontResources.GetEmbeddableExtension(bytes, 1, out _).Should().BeNull();
    }

    private static void WriteFontDirectory(byte[] bytes, int offset, ushort flags)
    {
        var face = bytes.AsSpan(offset);
        BinaryPrimitives.WriteUInt32BigEndian(face, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(face[4..], 2);
        WriteFontTable(face[12..], "OS/2"u8, (uint)(offset + 44), 10);
        WriteFontTable(face[28..], "glyf"u8, (uint)(offset + 54), 1);
        BinaryPrimitives.WriteUInt16BigEndian(face[52..], flags);
    }

    private static void WriteFontTable(Span<byte> entry, ReadOnlySpan<byte> tag, uint offset, uint length)
    {
        tag.CopyTo(entry);
        BinaryPrimitives.WriteUInt32BigEndian(entry[8..], offset);
        BinaryPrimitives.WriteUInt32BigEndian(entry[12..], length);
    }

    private static byte[] ReadPart(ZipArchive package, string path)
    {
        using var entry = package.GetEntry(path)!.Open();
        using var output = new MemoryStream();
        entry.CopyTo(output);
        return output.ToArray();
    }
}
