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
    }

    private static Workbook CreateWorksheetBackgroundImageSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetBackgroundImagePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "background.png");
        return workbook;
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
