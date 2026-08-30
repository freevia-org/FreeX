using System.Linq;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R173: <see cref="HtmlFileAdapter"/>'s data-URI export and <see cref="MhtmlFileAdapter"/>'s Content-Type
/// header must label EMF/WMF pictures with their real media type (matching <c>Free.Shared.Opc.OpcMediaTypes</c>
/// / <c>Ooxml.ImageContentTypeForExtension</c>, the same table <c>OdtFileAdapter</c> and <c>DocxWriter</c> use),
/// not the historical <c>image/png</c> fallback -- bytes mislabelled as PNG fail to decode in any consumer.
/// A PNG/JPEG regression case guards that already-correct formats are unaffected.
/// </summary>
public class R173_HtmlEmfWmfContentTypeTests
{
    private static TextDocument DocumentWithImage(InlineImage image)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        document.Blocks.Add(paragraph);
        return document;
    }

    [Theory]
    [InlineData(ImageFormat.Emf, "image/x-emf")]
    [InlineData(ImageFormat.Wmf, "image/x-wmf")]
    public void Html_DataUriExport_UsesRealContentTypeForMetafiles(ImageFormat format, string expectedMime)
    {
        var image = new InlineImage([0x01, 0x00, 0x00, 0x00, 0xAA, 0xBB], 10, 10, format);
        var document = DocumentWithImage(image);

        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain($"data:{expectedMime};base64,");
        html.Should().NotContain("data:image/png;base64,");
    }

    [Theory]
    [InlineData(ImageFormat.Emf, "image/x-emf")]
    [InlineData(ImageFormat.Wmf, "image/x-wmf")]
    public void Mhtml_Export_UsesRealContentTypeHeaderForMetafiles(ImageFormat format, string expectedMime)
    {
        var image = new InlineImage([0x01, 0x00, 0x00, 0x00, 0xAA, 0xBB], 10, 10, format);
        var document = DocumentWithImage(image);

        using var stream = new MemoryStream();
        new MhtmlFileAdapter().Save(document, stream);
        var mhtml = Encoding.UTF8.GetString(stream.ToArray());

        mhtml.Should().Contain($"Content-Type: {expectedMime}");
        mhtml.Should().NotContain("Content-Type: image/png");
    }

    [Theory]
    [InlineData(ImageFormat.Emf)]
    [InlineData(ImageFormat.Wmf)]
    public void Html_DataUriRoundTrip_PreservesMetafileFormatTag(ImageFormat format)
    {
        // The reader-side counterpart of the writer fix: ImageFormatFromMime must recognise the
        // image/x-emf / image/x-wmf content type the writer now emits, or a FreeW-authored HTML file
        // round-trips an EMF/WMF picture back in as a (wrongly tagged) ImageFormat.Png.
        var bytes = new byte[] { 0x01, 0x00, 0x00, 0x00, 0xAA, 0xBB };
        var document = DocumentWithImage(new InlineImage(bytes, 10, 10, format));

        using var stream = new MemoryStream();
        var adapter = new HtmlFileAdapter();
        adapter.Save(document, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);

        var loadedImage = loaded.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Select(r => r.Image).FirstOrDefault(i => i is not null);
        loadedImage.Should().NotBeNull();
        loadedImage!.Format.Should().Be(format);
        loadedImage.Bytes.Should().Equal(bytes);
    }

    [Theory]
    [InlineData(ImageFormat.Png, "image/png")]
    [InlineData(ImageFormat.Jpeg, "image/jpeg")]
    [InlineData(ImageFormat.Gif, "image/gif")]
    [InlineData(ImageFormat.Bmp, "image/bmp")]
    [InlineData(ImageFormat.Tiff, "image/tiff")]
    public void Html_DataUriExport_StillUsesCorrectContentTypeForNonMetafileFormats(ImageFormat format, string expectedMime)
    {
        var image = new InlineImage([0x00, 0x01, 0x02, 0x03], 10, 10, format);
        var document = DocumentWithImage(image);

        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(document, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        html.Should().Contain($"data:{expectedMime};base64,");
    }
}
