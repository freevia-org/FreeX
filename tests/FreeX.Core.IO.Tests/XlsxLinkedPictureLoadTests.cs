using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R65-io-image-drawing-6-1 regression: a picture inserted via Excel's "Link to File" has an
/// <c>&lt;xdr:pic&gt;</c> whose <c>&lt;a:blip&gt;</c> carries <c>r:link</c> (not <c>r:embed</c>), backed
/// by an External-mode relationship. Before the fix, <c>XlsxWorksheetDrawingPartReader.ReadPictureParts</c>
/// only ever looked for <c>r:embed</c> and skipped the element entirely when it was absent, so the picture
/// never made it into <see cref="Sheet.Pictures"/> — invisible to the model and, on any save path that
/// regenerates the sheet's drawing objects from the model rather than copying raw source bytes, lost for
/// good. FreeX has no UI command that authors an r:link picture yet, so these tests hand-craft the XML the
/// way Excel itself would produce it, the same way <c>XlsxNonChartSchemaValidationTests</c>'s
/// <c>AddExternalWorksheetPictureReference</c> hand-crafts an external worksheet-background reference.
/// </summary>
public sealed class XlsxLinkedPictureLoadTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void LinkedPicture_WithOnlyRLink_IsMaterializedInSheetPicturesAndSurvivesRoundTrip()
    {
        const string externalTarget = "file:///C:/Images/external-photo.png";

        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("LinkedPictureLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "EmbeddedPicture",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);
        InjectLinkedPicture(initialSave, "LinkedPicture", externalTarget);

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        reloadedSheet.Pictures.Should().HaveCount(2,
            "the linked picture must be materialized alongside the embedded one, not silently dropped");
        var linkedPicture = reloadedSheet.Pictures.Should()
            .ContainSingle(picture => picture.Name == "LinkedPicture",
                "the linked picture must be identifiable by its cNvPr name like any other picture").Subject;
        linkedPicture.LinkedImageTarget.Should().Be(externalTarget,
            "the external relationship Target must be preserved verbatim as the linked/external marker");
        linkedPicture.ImageBytes.Should().BeNullOrEmpty("a linked picture has no embedded raster to load");

        var embeddedPicture = reloadedSheet.Pictures.Should()
            .ContainSingle(picture => picture.Name == "EmbeddedPicture").Subject;
        embeddedPicture.LinkedImageTarget.Should().BeNull("a normal embedded picture is not linked");
        embeddedPicture.ImageBytes.Should().Equal(MinimalPngBytes());

        // ── Round-trip: a further save + reload must still keep the linked picture, not drop it ──
        // (No model edit is made here, so this exercises the cheapest save path available for an
        // unmodified reload -- whichever one that is -- rather than requiring the source-package
        // edit-preparation path, which categorically declines any package containing an external
        // relationship in its drawing graph; that is a separate, pre-existing limitation unrelated to
        // this fix.)
        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        secondSave.Position = 0;
        var finalSheet = adapter.Load(secondSave).GetSheet("Sheet1")!;
        finalSheet.Pictures.Should().HaveCount(2, "the linked picture must still survive a subsequent save+reload");
        finalSheet.Pictures.Should()
            .ContainSingle(picture => picture.Name == "LinkedPicture" && picture.LinkedImageTarget == externalTarget,
                "the linked picture's external target must be preserved through a second round-trip");
    }

    [Fact]
    public void EmbeddedPicture_WithNoLinkedTarget_StillLoadsNormally()
    {
        // Sibling no-regression test: a plain embedded picture (the overwhelmingly common case) must be
        // unaffected by the new r:link handling -- it keeps loading via r:embed exactly as before, with
        // LinkedImageTarget staying null.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("EmbeddedPictureNoRegression");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "OnlyPicture",
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80
        });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheet("Sheet1")!;
        var picture = reloadedSheet.Pictures.Should().ContainSingle().Subject;
        picture.Name.Should().Be("OnlyPicture");
        picture.LinkedImageTarget.Should().BeNull();
        picture.ImageBytes.Should().Equal(MinimalPngBytes());
        picture.Width.Should().Be(120);
        picture.Height.Should().Be(80);
    }

    /// <summary>
    /// Hand-crafts a "Link to File" &lt;xdr:pic&gt; (blip r:link + External relationship, no r:embed and
    /// no media part) and appends it to the sheet's single drawing part, alongside whatever picture(s)
    /// <see cref="XlsxWorksheetDrawingObjectWriter"/> already wrote there.
    /// </summary>
    private static void InjectLinkedPicture(MemoryStream packageStream, string pictureName, string externalTarget)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var drawingEntry = archive.Entries.Single(entry =>
            entry.FullName.StartsWith("xl/drawings/drawing", System.StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase));
        var drawingPath = drawingEntry.FullName;
        var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);

        var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
        var relsXml = archive.GetEntry(drawingRelsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        const string linkRelId = "rIdLinkedImage1";
        relsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", linkRelId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
            new XAttribute("Target", externalTarget),
            new XAttribute("TargetMode", "External")));

        drawingXml.Root!.Add(new XElement(SpreadsheetDrawingNs + "oneCellAnchor",
            new XElement(SpreadsheetDrawingNs + "from",
                new XElement(SpreadsheetDrawingNs + "col", "6"),
                new XElement(SpreadsheetDrawingNs + "colOff", "0"),
                new XElement(SpreadsheetDrawingNs + "row", "6"),
                new XElement(SpreadsheetDrawingNs + "rowOff", "0")),
            new XElement(SpreadsheetDrawingNs + "ext",
                new XAttribute("cx", "914400"),
                new XAttribute("cy", "914400")),
            new XElement(SpreadsheetDrawingNs + "pic",
                new XElement(SpreadsheetDrawingNs + "nvPicPr",
                    new XElement(SpreadsheetDrawingNs + "cNvPr", new XAttribute("id", "99"), new XAttribute("name", pictureName)),
                    new XElement(SpreadsheetDrawingNs + "cNvPicPr")),
                new XElement(SpreadsheetDrawingNs + "blipFill",
                    new XElement(DrawingNs + "blip", new XAttribute(RelNs + "link", linkRelId)),
                    new XElement(DrawingNs + "stretch", new XElement(DrawingNs + "fillRect"))),
                new XElement(SpreadsheetDrawingNs + "spPr",
                    new XElement(DrawingNs + "prstGeom",
                        new XAttribute("prst", "rect"),
                        new XElement(DrawingNs + "avLst")))),
            new XElement(SpreadsheetDrawingNs + "clientData")));

        XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, drawingRelsPath, relsXml);
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
