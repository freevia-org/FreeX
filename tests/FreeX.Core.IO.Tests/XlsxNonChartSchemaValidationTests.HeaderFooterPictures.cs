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
        AssertReloadedPageHeaderPictures(saved, expectedPictureCount: 1);
    }

    [Fact]
    public void HeaderFooterPictures_ReloadsSavedWorkbookWithPictureModel()
    {
        using var saved = Save(CreateHeaderFooterPictureSourceWorkbook());
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var sheet = reloaded.GetSheetAt(0);

        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("&[Picture]", "Center", "Right"));
        sheet.PageHeaderPictures.Left.Should().NotBeNull();
        var picture = sheet.PageHeaderPictures.Left!;
        picture.ImageBytes.Should().Equal(MinimalPngBytes());
        picture.ContentType.Should().Be("image/png");
        picture.FileName.Should().Be("header-logo.png");
        picture.Width.Should().Be(96);
        picture.Height.Should().Be(32);
        sheet.PageHeaderPictures.Center.Should().BeNull();
        sheet.PageHeaderPictures.Right.Should().BeNull();
    }

    [Fact]
    public void HeaderFooterPictures_WithRepeatedFileNames_WritesDistinctMediaRelationshipTargets()
    {
        var workbook = CreateHeaderFooterPictureSourceWorkbook();
        var sheet = workbook.GetSheetAt(0);
        var duplicateName = "logo.png";
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", duplicateName, 96, 32),
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", duplicateName, 80, 28),
            null);

        using var saved = Save(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var vmlPath = ReadHeaderFooterVmlPath(saved);
        var imageTargets = ReadHeaderFooterImageRelationshipTargets(saved, vmlPath);
        imageTargets.Should().HaveCount(2);
        imageTargets.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(2);
        AssertReloadedPageHeaderPictures(saved, expectedPictureCount: 2);
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

        var reloadedSheet = AssertReloadedPageHeaderPictures(saved, expectedPictureCount: 1);
        reloadedSheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42));
        reloadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("&[Picture]", "", ""));
        reloadedSheet.PageHeaderPictures.Left.Should().NotBeNull();
        reloadedSheet.PageHeaderPictures.Left!.ImageBytes.Should().Equal(sourceImageBytes);
        reloadedSheet.PageHeaderPictures.Left.ContentType.Should().Be("image/png");
        reloadedSheet.PageHeaderPictures.Center.Should().BeNull();
        reloadedSheet.PageHeaderPictures.Right.Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithStaleHeaderFooterImageRelationship_PrunesStaleRelationship()
    {
        using var source = CreateExcelAuthoredHeaderFooterPictureSourcePackage();
        AddStaleHeaderFooterImageRelationship(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        var vmlPath = ReadHeaderFooterVmlPath(saved);
        var relationships = ReadPackageRootElement(saved, XlsxPackagePath.GetRelationshipPartPath(vmlPath));
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relationships
            .Elements(packageRelNs + "Relationship")
            .Select(element => element.Attribute("Id")?.Value)
            .Should()
            .Equal("rIdImage1");
        var reloadedSheet = AssertReloadedPageHeaderPictures(saved, expectedPictureCount: 1);
        reloadedSheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void HeaderFooterPicturePackageGraphNormalizer_WithMissingContentTypes_RestoresContentTypes()
    {
        using var source = CreateExcelAuthoredHeaderFooterPictureSourcePackage();
        RemoveHeaderFooterPictureContentTypes(source);
        source.Position = 0;

        using (var archive = new ZipArchive(source, ZipArchiveMode.Update, leaveOpen: true))
            XlsxHeaderFooterPicturePackageGraphNormalizer.Normalize(archive, "xl/drawings/vmlDrawing1.vml").Should().BeTrue();

        SchemaErrors(source).Should().BeEmpty();
        HasEffectiveContentType(source, "xl/drawings/vmlDrawing1.vml", "application/vnd.openxmlformats-officedocument.vmlDrawing")
            .Should()
            .BeTrue();
        HasEffectiveContentType(source, "xl/media/headerFooterImage1.png", "image/png")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidHeaderFooterLegacyDrawingMarkerForSchemaValidity()
    {
        using var source = CreateExcelAuthoredHeaderFooterPictureSourcePackage();
        SetHeaderFooterLegacyDrawingMarkerInvalidMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertHeaderFooterLegacyDrawingMarkerSanitized(saved);
        AssertHeaderFooterPicturePackage(saved);
        var reloadedSheet = AssertReloadedPageHeaderPictures(saved, expectedPictureCount: 1);
        reloadedSheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42));
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

    private static void AddStaleHeaderFooterImageRelationship(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsPath = "xl/drawings/_rels/vmlDrawing1.vml.rels";
        var relationshipsXml = LoadPackageXml(archive, relationshipsPath);
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdStaleImage"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
            new XAttribute("Target", "../media/staleHeaderFooterImage.png")));
        ReplacePackageXml(archive, relationshipsPath, relationshipsXml);

        archive.GetEntry("xl/media/staleHeaderFooterImage.png")?.Delete();
        var imageEntry = archive.CreateEntry("xl/media/staleHeaderFooterImage.png");
        using (var imageStream = imageEntry.Open())
            imageStream.Write(MinimalPngBytes());

        AddPackageContentTypeOverride(archive, "/xl/media/staleHeaderFooterImage.png", "image/png");
    }

    private static void RemoveHeaderFooterPictureContentTypes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/xl/drawings/vmlDrawing1.vml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Attribute("PartName")?.Value, "/xl/media/headerFooterImage1.png", StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root
            .Elements(contentTypeNs + "Default")
            .Where(element => string.Equals(element.Attribute("Extension")?.Value, "png", StringComparison.OrdinalIgnoreCase))
            .Remove();
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void SetHeaderFooterLegacyDrawingMarkerInvalidMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;
        var legacyDrawing = root.Element(worksheetNs + "legacyDrawingHF")!;
        legacyDrawing.SetAttributeValue("customLegacyDrawingFlag", "removed");
        legacyDrawing.Add(new XElement(worksheetNs + "nativeLegacyDrawingChild"));
        root.Add(new XElement(
            worksheetNs + "legacyDrawingHF",
            new XAttribute(relNs + "id", "rIdHeaderFooterDrawing1"),
            new XAttribute("customDuplicateMarkerFlag", "removed")));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
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

    private static Sheet AssertReloadedPageHeaderPictures(Stream stream, int expectedPictureCount)
    {
        stream.Position = 0;
        var sheet = new XlsxFileAdapter().Load(stream).GetSheetAt(0);
        var pictures = new[]
            {
                sheet.PageHeaderPictures.Left,
                sheet.PageHeaderPictures.Center,
                sheet.PageHeaderPictures.Right
            }
            .OfType<WorksheetHeaderFooterPicture>()
            .ToList();

        pictures.Should().HaveCount(expectedPictureCount);
        pictures.Should().AllSatisfy(picture =>
        {
            picture.ImageBytes.Should().Equal(MinimalPngBytes());
            picture.ContentType.Should().Be("image/png");
        });
        return sheet;
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

    private static void AssertHeaderFooterLegacyDrawingMarkerSanitized(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var worksheet = ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml");
        var legacyDrawing = worksheet.Elements(worksheetNs + "legacyDrawingHF")
            .Should()
            .ContainSingle()
            .Subject;
        legacyDrawing.Attribute(relNs + "id")!.Value.Should().Be("rIdHeaderFooterDrawing1");
        legacyDrawing.Attribute("customLegacyDrawingFlag").Should().BeNull();
        legacyDrawing.Attribute("customDuplicateMarkerFlag").Should().BeNull();
        legacyDrawing.Elements().Should().BeEmpty();
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

    private static List<string> ReadHeaderFooterImageRelationshipTargets(Stream stream, string vmlPath)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string imageRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

        var vmlRelationships = ReadPackageRootElement(stream, XlsxPackagePath.GetRelationshipPartPath(vmlPath));
        return vmlRelationships
            .Elements(packageRelNs + "Relationship")
            .Where(element => element.Attribute("Type")?.Value == imageRelationshipType)
            .Select(element => XlsxPackagePath.ResolveRelationshipTarget(vmlPath, element.Attribute("Target")!.Value))
            .ToList();
    }

    private static bool HasEffectiveContentType(Stream stream, string partPath, string contentType)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        var normalizedPartName = $"/{partPath.TrimStart('/')}";
        var overrideElement = contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(element.Attribute("PartName")?.Value, normalizedPartName, StringComparison.OrdinalIgnoreCase));
        if (overrideElement is not null)
            return string.Equals(overrideElement.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase);

        var extension = Path.GetExtension(partPath).TrimStart('.');
        return contentTypesXml.Root!
            .Elements(contentTypeNs + "Default")
            .Any(element =>
                string.Equals(element.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
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
