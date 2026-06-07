using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void HeaderFooterPictures_ProducesSchemaValidWorkbook()
    {
        using var saved = Save(CreateHeaderFooterPictureSourceWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        AssertHeaderFooterPicturePackage(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithHeaderFooterPictures_ProducesSchemaValidWorkbook()
    {
        using var source = CreateExcelAuthoredHeaderFooterPictureSourcePackage();
        var sourceLegacyDrawing = ReadWorksheetChildElement(source, "legacyDrawingHF");
        var sourceVmlPath = ReadHeaderFooterVmlPath(source);
        var sourceVmlDrawing = ReadPackageRootElement(source, sourceVmlPath);
        var sourceVmlRelationships = ReadPackageRootElement(source, XlsxPackagePath.GetRelationshipPartPath(sourceVmlPath));
        var sourceImageBytes = ReadHeaderFooterImageBytes(source, sourceVmlPath);
        SchemaErrors(source).Should().BeEmpty();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertHeaderFooterPicturePackage(saved);
        ReadWorksheetChildElement(saved, "legacyDrawingHF")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceLegacyDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, sourceVmlPath)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceVmlDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, XlsxPackagePath.GetRelationshipPartPath(sourceVmlPath))
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceVmlRelationships.ToString(SaveOptions.DisableFormatting));
        ReadHeaderFooterImageBytes(saved, sourceVmlPath).Should().Equal(sourceImageBytes);
    }

    private static Workbook CreateHeaderFooterPictureSourceWorkbook()
    {
        var workbook = new Workbook("HeaderFooterPicturePatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("header/footer picture"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "Center", "Right");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", "header-logo.png", 96, 32),
            null,
            null);
        return workbook;
    }

    private static MemoryStream CreateExcelAuthoredHeaderFooterPictureSourcePackage()
    {
        var workbook = new Workbook("ExcelHeaderFooterPicturePatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("header/footer picture"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");

        var stream = Save(workbook);
        AddExcelHeaderFooterLegacyDrawingPackage(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddExcelHeaderFooterLegacyDrawingPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        archive.GetEntry("xl/drawings/vmlDrawing1.vml")?.Delete();
        var vmlEntry = archive.CreateEntry("xl/drawings/vmlDrawing1.vml");
        using (var writer = new StreamWriter(vmlEntry.Open(), Encoding.UTF8))
        {
            writer.Write("""
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="LH" type="#_x0000_t75">
                    <v:imagedata o:relid="rIdImage1" o:title="Header"/>
                  </v:shape>
                </xml>
                """);
        }

        archive.GetEntry("xl/media/headerFooterImage1.png")?.Delete();
        var imageEntry = archive.CreateEntry("xl/media/headerFooterImage1.png");
        using (var imageStream = imageEntry.Open())
            imageStream.Write(MinimalPngBytes());

        ReplacePackageXml(archive, "xl/drawings/_rels/vmlDrawing1.vml.rels", new XDocument(
            new XElement(
                packageRelNs + "Relationships",
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdImage1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", "../media/headerFooterImage1.png")))));

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        worksheetRelsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdHeaderFooterDrawing1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
            new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        InsertLegacyDrawingHeaderFooterInOrder(worksheetXml.Root!, worksheetNs, new XElement(
            worksheetNs + "legacyDrawingHF",
            new XAttribute(relNs + "id", "rIdHeaderFooterDrawing1")));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        AddPackageContentTypeOverride(
            archive,
            "/xl/drawings/vmlDrawing1.vml",
            "application/vnd.openxmlformats-officedocument.vmlDrawing");
        AddPackageContentTypeOverride(
            archive,
            "/xl/media/headerFooterImage1.png",
            "image/png");
    }

    private static void InsertLegacyDrawingHeaderFooterInOrder(
        XElement worksheetRoot,
        XNamespace worksheetNs,
        XElement legacyDrawingHeaderFooter)
    {
        string[] laterWorksheetElements =
        [
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == worksheetNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(legacyDrawingHeaderFooter);
        else
            insertionPoint.AddBeforeSelf(legacyDrawingHeaderFooter);
    }

    private static void AssertHeaderFooterPicturePackage(Stream stream)
    {
        var vmlPath = ReadHeaderFooterVmlPath(stream);
        var vmlRelationshipsPath = XlsxPackagePath.GetRelationshipPartPath(vmlPath);
        ReadPackageRootElement(stream, vmlPath).Elements().Should().NotBeEmpty();
        ReadPackageRootElement(stream, vmlRelationshipsPath).Elements().Should().NotBeEmpty();
        ReadHeaderFooterImageBytes(stream, vmlPath).Should().Equal(MinimalPngBytes());
    }

    private static string ReadHeaderFooterVmlPath(Stream stream)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string worksheetPath = "xl/worksheets/sheet1.xml";
        const string vmlRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

        var legacyDrawing = ReadWorksheetChildElement(stream, "legacyDrawingHF");
        var relationshipId = legacyDrawing.Attribute(relNs + "id")!.Value;
        var worksheetRelationships = ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels");
        var relationship = worksheetRelationships
            .Elements(packageRelNs + "Relationship")
            .Single(element =>
                element.Attribute("Id")?.Value == relationshipId &&
                element.Attribute("Type")?.Value == vmlRelationshipType);
        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
    }

    private static byte[] ReadHeaderFooterImageBytes(Stream stream, string vmlPath)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string imageRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

        var vmlRelationships = ReadPackageRootElement(stream, XlsxPackagePath.GetRelationshipPartPath(vmlPath));
        var imageRelationship = vmlRelationships
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == imageRelationshipType);
        var imagePath = XlsxPackagePath.ResolveRelationshipTarget(vmlPath, imageRelationship.Attribute("Target")!.Value);
        return ReadPackageEntryBytes(stream, imagePath);
    }

    private static byte[] ReadPackageEntryBytes(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var entryStream = archive.GetEntry(entryName)!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
