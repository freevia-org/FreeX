using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round 172 (freep-media F1 follow-up, FreeX half). Insert Picture used to infer the content type
/// in the WPF shell through <c>DrawingInputParser.GetImageContentType</c>, a hardcoded switch that
/// defaulted EVERY unrecognised extension to <c>image/png</c>. Both shells' named picture filters
/// also offer an "All files" entry, so a <c>.wmf</c>/<c>.emf</c>/<c>.heic</c> could be chosen and its
/// bytes were then written into the saved .xlsx as <c>xl/media/freexPictureN.png</c> declared
/// <c>image/png</c> -- self-consistent but wrong, and undecodable by Excel or by FreeX itself.
///
/// These tests assert the SAVED PACKAGE, not just the lookup's return value, because the mislabel is
/// only observable there: <see cref="XlsxWorksheetDrawingObjectWriter"/> names the media part from the
/// stored content type (via <see cref="OpcMediaTypes.GetImageExtension"/>) AND declares the same
/// content type as that extension's <c>[Content_Types].xml</c> Default, so a lookup-only assertion
/// cannot distinguish "right" from "consistently wrong".
///
/// Design decision recorded here: wmf/emf are NOT added to FreeX picture insertion. FreeX has no
/// metafile decoder in either toolkit (<c>WpfBitmapImageLoader</c> is WIC-only, the Avalonia loader is
/// Skia-only), and neither shell's named filter lists them. The correct behaviour is the rejected-file
/// UX the Avalonia shell already had -- which the WPF shell now shares -- rather than silently
/// mislabelling. FreeW, whose model/writer/renderer DO carry metafiles end to end, extends instead;
/// see the FreeW half of this round.
/// </summary>
public sealed class R172_InsertedPictureSavedMediaContentTypeTests
{
    private static readonly XNamespace ContentTypesNs = OpcMediaTypes.ContentTypesNamespace;

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Theory]
    [InlineData("photo.png", "image/png", "png")]
    [InlineData("photo.jpg", "image/jpeg", "jpg")]
    [InlineData("photo.jpeg", "image/jpeg", "jpg")]
    [InlineData("photo.gif", "image/gif", "gif")]
    [InlineData("photo.bmp", "image/bmp", "bmp")]
    [InlineData("photo.webp", "image/webp", "webp")]
    [InlineData("scan.tif", "image/tiff", "tiff")]
    [InlineData("scan.tiff", "image/tiff", "tiff")]
    public void SupportedPicture_SavesMediaPartWhoseExtensionAndDeclaredContentTypeAgree(
        string fileName,
        string expectedContentType,
        string expectedMediaExtension)
    {
        var contentType = InsertPictureCommandFactory.ContentTypeForPath(fileName);
        contentType.Should().Be(expectedContentType);

        var (mediaPath, declaredContentType) = SaveWithPictureAndReadMediaFacts(contentType!);

        Path.GetExtension(mediaPath).TrimStart('.').Should().Be(
            expectedMediaExtension,
            "the media part must be named for the format actually chosen, not for the png default");
        declaredContentType.Should().Be(
            expectedContentType,
            "[Content_Types].xml must declare the media extension with the same content type the "
            + "picture was inserted with -- the r157-remediation pairing rule");
    }

    // The r157-remediation trap: teaching one side of the pair about a format without the other
    // produces a package that is worse than the original bug (a part named .png declared image/webp).
    // Both directions of the extension<->content-type pair are asserted here for every format the
    // picker accepts, so neither side can be extended alone.
    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.gif")]
    [InlineData("photo.bmp")]
    [InlineData("photo.webp")]
    [InlineData("scan.tiff")]
    public void SupportedPicture_ContentTypeAndExtensionMappersRoundTrip(string fileName)
    {
        var contentType = InsertPictureCommandFactory.ContentTypeForPath(fileName)!;
        var extension = OpcMediaTypes.GetImageExtension(contentType);

        OpcMediaTypes.GetImageContentType($"xl/media/image1.{extension}").Should().Be(contentType);
    }

    // wmf/emf reach the picker only through the dialogs' "All files" entry. The shells must reject
    // them (null == "unsupported"), which is what stops a mislabelled media part being written at all.
    [Theory]
    [InlineData("drawing.wmf")]
    [InlineData("drawing.WMF")]
    [InlineData("drawing.emf")]
    [InlineData("drawing.EMF")]
    [InlineData("logo.svg")]
    [InlineData("photo.heic")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void UnsupportedPicture_IsRejectedRatherThanDefaultedToPng(string fileName)
    {
        InsertPictureCommandFactory.ContentTypeForPath(fileName).Should().BeNull(
            "an unsupported picture format must be refused by the shared picker policy; the removed "
            + "DrawingInputParser.GetImageContentType instead returned image/png, which the writer "
            + "turned into a .png media part holding non-png bytes");
        InsertPictureCommandFactory.IsSupportedImagePath(fileName).Should().BeFalse();
    }

    // The header/footer picture route (HeaderFooterDialog.Pictures.cs in WPF,
    // MainWindow.PageLayout.cs in Avalonia) shares the picker policy but used to swallow its "null =
    // unsupported" answer with `?? "image/png"`, and its writer
    // (XlsxHeaderFooterPicturePackageWriter) names the media part from the same content type -- so it
    // carried an identical mislabel through a different pair of call sites.
    [Theory]
    // The header/footer media file name keeps the picture's own file name, so an agreeing-but-
    // differently-spelled extension ("logo.tif" for image/tiff) is preserved -- and it is that
    // extension, not the content-type-derived "tiff", that [Content_Types].xml must declare.
    [InlineData("logo.png", "image/png", "png")]
    [InlineData("logo.webp", "image/webp", "webp")]
    [InlineData("logo.tif", "image/tiff", "tif")]
    [InlineData("logo.tiff", "image/tiff", "tiff")]
    [InlineData("logo.jpeg", "image/jpeg", "jpeg")]
    public void SupportedHeaderFooterPicture_SavesMediaPartWhoseExtensionMatchesItsContentType(
        string fileName,
        string expectedContentType,
        string expectedMediaExtension)
    {
        var contentType = InsertPictureCommandFactory.ContentTypeForPath(fileName);
        contentType.Should().Be(expectedContentType);

        var workbook = new Workbook("R172HeaderPicture");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), contentType!, fileName, 120, 48),
            null,
            null);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var mediaPath = archive.Entries
            .Select(entry => entry.FullName)
            .Should().ContainSingle(name => name.StartsWith("xl/media/", StringComparison.Ordinal))
            .Subject;

        Path.GetExtension(mediaPath).TrimStart('.').Should().Be(expectedMediaExtension);
        DeclaredDefaultContentType(archive, expectedMediaExtension).Should().Be(expectedContentType);
    }

    // A file name whose extension does not mean this content type at all must not be carried into the
    // package: the part would be named from the file name and typed from the content type, leaving an
    // .bin part that no Default covers. The name is re-derived from the content type instead.
    [Fact]
    public void HeaderFooterPictureNamedWithADisagreeingExtension_IsRenamedFromItsContentType()
    {
        var workbook = new Workbook("R172HeaderPictureMismatch");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", "logo.bin", 120, 48),
            null,
            null);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var mediaPath = archive.Entries
            .Select(entry => entry.FullName)
            .Should().ContainSingle(name => name.StartsWith("xl/media/", StringComparison.Ordinal))
            .Subject;

        mediaPath.Should().EndWith("logo.png");
        DeclaredDefaultContentType(archive, "png").Should().Be("image/png");
    }

    private static (string MediaPath, string DeclaredContentType) SaveWithPictureAndReadMediaFacts(
        string contentType)
    {
        var workbook = new Workbook("R172Picture");
        var sheet = workbook.AddSheet("Sheet1");
        var command = InsertPictureCommandFactory.Build(
            sheet.Id,
            new CellAddress(sheet.Id, 1, 1),
            MinimalPngBytes(),
            contentType,
            width: 96,
            height: 64);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var mediaPath = archive.Entries
            .Select(entry => entry.FullName)
            .Should().ContainSingle(name => name.StartsWith("xl/media/freexPicture", StringComparison.Ordinal))
            .Subject;

        var extension = Path.GetExtension(mediaPath).TrimStart('.');
        return (mediaPath, DeclaredDefaultContentType(archive, extension));
    }

    private static string DeclaredDefaultContentType(ZipArchive archive, string extension)
    {
        using var contentTypesStream = archive.GetEntry(OpcMediaTypes.ContentTypesPath)!.Open();
        return XDocument.Load(contentTypesStream).Root!
            .Elements(ContentTypesNs + "Default")
            .Single(element => string.Equals(
                (string?)element.Attribute("Extension"),
                extension,
                StringComparison.OrdinalIgnoreCase))
            .Attribute("ContentType")!.Value;
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
