using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorksheetBackgroundImage_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetBackgroundImageSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetBackgroundImage_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetBackgroundImageSourceWorkbook());
        var sourcePicture = ReadWorksheetChildElement(source, "picture");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        var sourceImageBytes = ReadWorksheetBackgroundImageBytes(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertWorksheetBackgroundImageModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "picture")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePicture.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetBackgroundImageBytes(saved).Should().Equal(sourceImageBytes);

        saved.Position = 0;
        AssertWorksheetBackgroundImageModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetBackgroundPictureMarkerForSchemaValidity()
    {
        using var source = Save(CreateWorksheetBackgroundImageSourceWorkbook());
        SetWorksheetBackgroundPictureMarkerInvalidMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertWorksheetBackgroundImageModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetBackgroundPictureMarkerSanitized(saved);
        ReadWorksheetBackgroundImageBytes(saved).Should().Equal(MinimalPngBytes());

        saved.Position = 0;
        AssertWorksheetBackgroundImageModel(adapter.Load(saved).GetSheetAt(0));
    }

    private static Workbook CreateWorksheetBackgroundImageSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetBackgroundImagePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "background.png");
        return workbook;
    }

    private static void SetWorksheetBackgroundPictureMarkerInvalidMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;
        var picture = root.Element(worksheetNs + "picture")!;
        picture.SetAttributeValue("customPictureFlag", "removed");
        picture.Add(new XElement(worksheetNs + "nativePictureChild"));
        root.Add(new XElement(
            worksheetNs + "picture",
            new XAttribute(relNs + "id", picture.Attribute(relNs + "id")!.Value),
            new XAttribute("customDuplicatePictureFlag", "removed")));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetBackgroundPictureMarkerSanitized(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var worksheet = ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml");
        var picture = worksheet.Elements(worksheetNs + "picture")
            .Should()
            .ContainSingle()
            .Subject;
        picture.Attribute(relNs + "id").Should().NotBeNull();
        picture.Attribute("customPictureFlag").Should().BeNull();
        picture.Attribute("customDuplicatePictureFlag").Should().BeNull();
        picture.Elements().Should().BeEmpty();
    }

    private static void AssertWorksheetBackgroundImageModel(Sheet sheet)
    {
        sheet.BackgroundImage.Should().NotBeNull();
        sheet.BackgroundImage!.ImageBytes.Should().Equal(MinimalPngBytes());
        sheet.BackgroundImage.ContentType.Should().Be("image/png");
        sheet.BackgroundImage.FileName.Should().Be("background.png");
    }

    private static byte[] ReadWorksheetBackgroundImageBytes(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        const string worksheetPath = "xl/worksheets/sheet1.xml";

        var relsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType);
        var imagePath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value);
        using var imageStream = archive.GetEntry(imagePath)!.Open();
        using var bytes = new MemoryStream();
        imageStream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
