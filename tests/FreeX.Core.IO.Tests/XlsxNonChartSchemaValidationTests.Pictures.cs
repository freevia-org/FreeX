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
        AssertWorksheetPictureModel(sheet);
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

        saved.Position = 0;
        AssertWorksheetPictureModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithResizedWorksheetPicture_DoesNotDiscardNewGeometry()
    {
        // F15 regression: resizing/moving a source-loaded picture must not be silently discarded by the
        // cell-patch fast-save path. Before the fix, pictures had no anchor/geometry comparison at all in the
        // patch-safe check, so any resize or reposition of an existing picture looked like "no drawing change"
        // and the stale source drawing XML (with the ORIGINAL geometry) was kept on save.
        using var source = Save(CreateWorksheetPictureSourceWorkbook());

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        var newWidth = picture.Width + 200;
        var newHeight = picture.Height + 90;
        var newOffsetX = picture.AnchorOffsetX + 25;
        var newOffsetY = picture.AnchorOffsetY + 10;
        picture.Width = newWidth;
        picture.Height = newHeight;
        picture.AnchorOffsetX = newOffsetX;
        picture.AnchorOffsetY = newOffsetY;
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // The geometry change makes the source drawing part unsafe to keep as-is, so the whole package must
        // fall back to a full save (never a source patch that would silently retain the old drawing XML).
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        var reloadedPicture = adapter.Load(saved).GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        reloadedPicture.Width.Should().BeApproximately(newWidth, 1.0);
        reloadedPicture.Height.Should().BeApproximately(newHeight, 1.0);
        reloadedPicture.AnchorOffsetX.Should().BeApproximately(newOffsetX, 1.0);
        reloadedPicture.AnchorOffsetY.Should().BeApproximately(newOffsetY, 1.0);
    }

    [Fact]
    public void LoadedWorkbookFullSave_RebindsWorksheetPictureRelationshipWhenAuthoredDrawingUsesSourceId()
    {
        var workbook = new Workbook("WorksheetPictureRelationshipCollision");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        using var source = Save(workbook);
        AddExternalWorksheetPictureReference(source, "rId1");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Pictures.Add(new PictureModel
        {
            Name = "Authored picture",
            Anchor = new CellAddress(loadedSheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            AltText = "Authored picture"
        });

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        SchemaErrors(saved).Should().BeEmpty();

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var drawingRelId = worksheetXml.Root!
            .Element(worksheetNs + "drawing")!
            .Attribute(relNs + "id")!
            .Value;
        drawingRelId.Should().Be("rId1");

        var pictureRelId = worksheetXml.Root!
            .Element(worksheetNs + "picture")!
            .Attribute(relNs + "id")!
            .Value;
        pictureRelId.Should().NotBe("rId1");

        var worksheetRelationships = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        var relationshipsById = worksheetRelationships.Root!
            .Elements(packageRelNs + "Relationship")
            .ToDictionary(element => element.Attribute("Id")!.Value, StringComparer.OrdinalIgnoreCase);

        relationshipsById[drawingRelId].Attribute("Type")!.Value.Should().Be("http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
        relationshipsById[pictureRelId].Attribute("Type")!.Value.Should().Be("http://schemas.openxmlformats.org/officeDocument/2006/relationships/image");
        relationshipsById[pictureRelId].Attribute("Target")!.Value.Should().Be("https://example.invalid/background.png");
        relationshipsById[pictureRelId].Attribute("TargetMode")!.Value.Should().Be("External");

        archive.Dispose();
        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        var reloadedPicture = reloadedSheet.Pictures.Should().ContainSingle().Subject;
        reloadedPicture.Name.Should().Be("Authored picture");
        reloadedPicture.Anchor.Should().Be(new CellAddress(reloadedSheet.Id, 2, 2));
        reloadedPicture.Kind.Should().Be(PictureKind.Image);
        reloadedPicture.ImageBytes.Should().Equal(MinimalPngBytes());
        reloadedPicture.ContentType.Should().Be("image/png");
        reloadedPicture.Width.Should().Be(96);
        reloadedPicture.Height.Should().Be(64);
        reloadedPicture.AltText.Should().Be("Authored picture");
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

    private static void AssertWorksheetPictureModel(Sheet sheet)
    {
        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Name.Should().Be("Product Photo");
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 2, 3));
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ImageBytes.Should().Equal(MinimalPngBytes());
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(120);
        picture.Height.Should().Be(80);
        picture.AltText.Should().Be("Authored picture");
    }

    private static void AddExternalWorksheetPictureReference(MemoryStream packageStream, string relationshipId)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        worksheetRelsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", relationshipId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
            new XAttribute("Target", "https://example.invalid/background.png"),
            new XAttribute("TargetMode", "External")));
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        worksheetXml.Root!.Add(new XElement(
            worksheetNs + "picture",
            new XAttribute(relNs + "id", relationshipId)));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        packageStream.Position = 0;
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
