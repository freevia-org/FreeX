using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round 172 (freep-media F1 follow-up, FreeW half). FreeW's Insert Picture filter listed neither
/// <c>*.wmf</c> nor <c>*.emf</c>, so metafiles were reachable only through the picker's "All files"
/// entry -- even though FreeW carries them end to end. These tests pin the SAVED PACKAGE rather than
/// the lookup return value, because the failure mode this round is about is only observable there: the
/// media part's name and its <c>[Content_Types].xml</c> Default come from two different mappers
/// (<see cref="InlineImage.ExtensionFor"/> for the name,
/// <c>OoxmlWordprocessing.ImageContentTypeForExtension</c> -&gt; <c>OpcMediaTypes</c> for the type), and
/// the r157-remediation comment in <c>OpcMediaTypes</c> records what happens when one is taught a
/// format the other does not know: a part called <c>image1.png</c> declared <c>image/x-emf</c>, which is
/// worse than the self-consistent mislabel it replaced.
///
/// Both members of that pair are asserted together for every format, and the picker's advertised
/// extension is the input, so the picker list, the part name and the declared content type cannot
/// drift apart.
/// </summary>
public class R172_MetafilePictureSavedPackageTests
{
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Theory]
    [InlineData(".wmf", "wmf", "image/x-wmf")]
    [InlineData(".WMF", "wmf", "image/x-wmf")]
    [InlineData(".emf", "emf", "image/x-emf")]
    [InlineData(".EMF", "emf", "image/x-emf")]
    [InlineData(".png", "png", "image/png")]
    [InlineData(".jpg", "jpeg", "image/jpeg")]
    [InlineData(".tiff", "tiff", "image/tiff")]
    public void PickedPictureExtension_ProducesAgreeingMediaPartNameAndDeclaredContentType(
        string pickedExtension,
        string expectedPartExtension,
        string expectedContentType)
    {
        // The same inference the Insert Picture workflow performs
        // (FreeWPictureImportPlanner.ResolvePreservedFormat).
        var format = InlineImage.FormatForExtension(pickedExtension);
        format.Should().NotBeNull(
            "the picker advertises this extension, so the preserved-format lookup must recognise it");

        using var stream = SaveDocumentWithImage(format!.Value);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var mediaPath = zip.Entries
            .Select(entry => entry.FullName)
            .Should().ContainSingle(name => name.StartsWith("word/media/", StringComparison.Ordinal))
            .Subject;
        Path.GetExtension(mediaPath).TrimStart('.').Should().Be(
            expectedPartExtension,
            "the media part must be named for the picture's real format, not for the png default");

        DeclaredDefaultContentType(zip, expectedPartExtension).Should().Be(
            expectedContentType,
            "[Content_Types].xml must declare that extension with its own content type -- naming the "
            + "part from one mapper and typing it from another that disagrees is the r157 trap");
    }

    [Theory]
    [InlineData(ImageFormat.Wmf)]
    [InlineData(ImageFormat.Emf)]
    public void MetafileBytes_SurviveTheRoundTripVerbatimAndKeepTheirFormat(ImageFormat format)
    {
        var bytes = format == ImageFormat.Wmf ? MinimalWmf() : MinimalEmf();

        using var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(new InlineImage(bytes, 120, 90, format)), stream);
        stream.Position = 0;

        var read = DocxReader.Read(stream)
            .Paragraphs.First()
            .Runs.Single(run => run.Image is not null)
            .Image!;

        read.Format.Should().Be(format, "the media part's extension carries the format back on read");
        read.Bytes.Should().Equal(
            bytes,
            "a metafile is preserved, never transcoded -- rasterizing it would discard the vector "
            + "original that Word round-trips");
    }

    private static MemoryStream SaveDocumentWithImage(ImageFormat format)
    {
        var bytes = format switch
        {
            ImageFormat.Wmf => MinimalWmf(),
            ImageFormat.Emf => MinimalEmf(),
            _ => MinimalPng(),
        };

        var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(new InlineImage(bytes, 120, 90, format)), stream);
        stream.Position = 0;
        return stream;
    }

    private static string DeclaredDefaultContentType(ZipArchive zip, string extension)
    {
        using var entry = zip.GetEntry("[Content_Types].xml")!.Open();
        return XDocument.Load(entry).Root!
            .Elements(Ct + "Default")
            .Single(element => string.Equals(
                (string?)element.Attribute("Extension"),
                extension,
                StringComparison.OrdinalIgnoreCase))
            .Attribute("ContentType")!.Value;
    }

    private static TextDocument DocumentWith(InlineImage image)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>A placeable-metafile header (D7 CD C6 9A), enough for signature detection.</summary>
    private static byte[] MinimalWmf()
    {
        var bytes = new byte[64];
        bytes[0] = 0xD7;
        bytes[1] = 0xCD;
        bytes[2] = 0xC6;
        bytes[3] = 0x9A;
        return bytes;
    }

    /// <summary>An EMR_HEADER record type followed by the " EMF" signature at offset 40.</summary>
    private static byte[] MinimalEmf()
    {
        var bytes = new byte[64];
        bytes[0] = 0x01;
        bytes[40] = 0x20;
        bytes[41] = 0x45;
        bytes[42] = 0x4D;
        bytes[43] = 0x46;
        return bytes;
    }
}
