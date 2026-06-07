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
    public void WorksheetPicture_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetPictureSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetPicture_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetPictureSourceWorkbook());
        var sourceWorksheetDrawing = ReadWorksheetChildElement(source, "drawing");
        var sourceDrawing = ReadPackageRootElement(source, "xl/drawings/drawing1.xml");
        var sourceDrawingRelationships = ReadPackageRootElement(source, "xl/drawings/_rels/drawing1.xml.rels");
        var sourceImageBytes = ReadWorksheetPictureImageBytes(source);
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
        ReadWorksheetChildElement(saved, "drawing")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/drawings/drawing1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/drawings/_rels/drawing1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDrawingRelationships.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetPictureImageBytes(saved).Should().Equal(sourceImageBytes);
    }

    private static Workbook CreateWorksheetPictureSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetPicturePatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Product Photo",
            Anchor = new CellAddress(sheet.Id, 2, 3),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            AltText = "Authored picture"
        });
        return workbook;
    }

    private static byte[] ReadWorksheetPictureImageBytes(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string relationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        const string drawingPath = "xl/drawings/drawing1.xml";

        var relsXml = LoadPackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels");
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(element => element.Attribute("Type")?.Value == relationshipType);
        var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, relationship.Attribute("Target")!.Value);
        using var imageStream = archive.GetEntry(imagePath)!.Open();
        using var bytes = new MemoryStream();
        imageStream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
