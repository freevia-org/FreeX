using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED3 finding P30: a worksheet background image saved
/// under the user's raw filename must never collide with a media part name the source package
/// already uses for an authored picture. <see cref="XlsxWorksheetBackgroundReaderWriter.Save"/>
/// (called from <c>ApplyPackagePostProcessing</c>) writes background media BEFORE
/// PreserveSourcePackageParts copies the source package's own xl/media/* entries into the
/// generated archive; without reserving those source names up front, a background image named
/// e.g. "image1.png" would claim the same package path as the source's
/// xl/media/image1.png picture, and CopyUnknownPackageParts would then skip copying the source's
/// picture media because the name is already taken — silently replacing the drawing's picture
/// with the background image.
/// </summary>
public class FreeXCleanupMED3Tests
{
    [Fact]
    public void LoadedWorkbookPatchSave_BackgroundImageNamedLikeSourcePictureMedia_DoesNotShadowSourcePicture()
    {
        var workbook = new Workbook("BackgroundVsPictureMediaTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Has picture"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;

        var originalPictureBytes = MinimalPngBytes();
        AddMinimalPicturePackage(source, originalPictureBytes);

        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        var loadedSheet = loaded.GetSheetAt(0);
        // The user picks a background file that happens to share the exact package filename
        // ("image1.png") that the source package already uses for the authored picture's media.
        var backgroundBytes = DistinctPngBytes();
        loadedSheet.BackgroundImage = new WorksheetBackgroundImage(backgroundBytes, "image/png", "image1.png");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);

        // The original drawing's picture media must still hold the ORIGINAL picture bytes, not
        // be overwritten/shadowed by the background image bytes.
        var pictureMediaEntry = archive.GetEntry("xl/media/image1.png");
        pictureMediaEntry.Should().NotBeNull("the source picture's media part must survive the save");
        using (var pictureStream = pictureMediaEntry!.Open())
        using (var ms = new MemoryStream())
        {
            pictureStream.CopyTo(ms);
            ms.ToArray().Should().Equal(originalPictureBytes,
                "the drawing's picture must not be shadowed by the background image sharing its filename");
        }

        // The background image must have been written under some OTHER, distinct media path
        // (not xl/media/image1.png, which is reserved by the source picture).
        var worksheetRelsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!;
        var relsXml = LoadPackageXml(worksheetRelsEntry);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string imageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        var imageTargets = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Type")?.Value == imageRelType)
            .Select(e => e.Attribute("Target")!.Value)
            .Select(target => XlsxPackagePath.ResolveRelationshipTarget("xl/worksheets/sheet1.xml", target))
            .ToList();

        imageTargets.Should().NotContain("xl/media/image1.png",
            "the background must not reuse the source picture's reserved media path");

        var backgroundPath = imageTargets.Should().ContainSingle().Subject;
        var backgroundEntry = archive.GetEntry(backgroundPath);
        backgroundEntry.Should().NotBeNull();
        using var backgroundStream = backgroundEntry!.Open();
        using var backgroundMs = new MemoryStream();
        backgroundStream.CopyTo(backgroundMs);
        backgroundMs.ToArray().Should().Equal(backgroundBytes);
    }

    private static void AddMinimalPicturePackage(MemoryStream packageStream, byte[] imageBytes)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Default")
                .Where(element => string.Equals(element.Attribute("Extension")?.Value, "png", StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Default",
                new XAttribute("Extension", "png"),
                new XAttribute("ContentType", "image/png")));
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdFreeXPictureDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXPictureDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    new XElement(spreadsheetDrawingNs + "oneCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "2"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "1"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "ext",
                            new XAttribute("cx", "1143000"),
                            new XAttribute("cy", "762000")),
                        new XElement(spreadsheetDrawingNs + "pic",
                            new XElement(spreadsheetDrawingNs + "nvPicPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "2"),
                                    new XAttribute("name", "Picture 1"),
                                    new XAttribute("title", "Native picture title"),
                                    new XAttribute("descr", "Native picture")),
                                new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                            new XElement(spreadsheetDrawingNs + "blipFill",
                                new XElement(drawingNs + "blip", new XAttribute(relNs + "embed", "rIdFreeXPictureImage")),
                                new XElement(drawingNs + "stretch", new XElement(drawingNs + "fillRect"))),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "xfrm"),
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(drawingNs + "avLst")))),
                        new XElement(spreadsheetDrawingNs + "clientData"))));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var drawingRelsXml = new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreeXPictureImage"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target", "../media/image1.png"))));
            ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);

            archive.GetEntry("xl/media/image1.png")?.Delete();
            var imageEntry = archive.CreateEntry("xl/media/image1.png");
            using var imageStream = imageEntry.Open();
            imageStream.Write(imageBytes);
        }

        packageStream.Position = 0;
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

    // A different 1x1 PNG (distinct IDAT payload) so byte-comparisons can tell the picture and
    // background images apart after save.
    private static byte[] DistinctPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x64, 0x60, 0x00, 0x00,
        0x00, 0x06, 0x00, 0x03, 0x36, 0x37, 0x7C, 0xA8,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }
}
