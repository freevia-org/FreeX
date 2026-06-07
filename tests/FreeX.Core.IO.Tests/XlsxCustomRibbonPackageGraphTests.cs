using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxCustomRibbonPackageGraphTests
{
    private const string CustomUiPath = "customUI/customUI.xml";
    private const string CustomUi14Path = "customUI/customUI14.xml";
    private const string CustomUiContentType = "application/xml";
    private const string CustomUiRelationshipType = "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility";
    private const string CustomUi14RelationshipType = "http://schemas.microsoft.com/office/2007/relationships/ui/extensibility";

    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void LoadedWorkbookFullSave_PreservesCustomRibbonPackageGraphAlongsideModelEdits()
    {
        using var source = CreateWorkbookWithCustomRibbonPackageGraph();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        AssertCustomRibbonPackageGraph(saved);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(1, 1).Should().Be(new TextValue("edited"));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_PreservesCustomRibbonPackageGraphAlongsideCellEdits()
    {
        using var source = CreateWorkbookWithCustomRibbonPackageGraph();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        AssertCustomRibbonPackageGraph(saved);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 1).Should().Be(new NumberValue(42));
    }

    private static MemoryStream CreateWorkbookWithCustomRibbonPackageGraph()
    {
        var workbook = new Workbook("CustomRibbonPackageGraph");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteTextEntry(archive, CustomUiPath,
                """<customUI xmlns="http://schemas.microsoft.com/office/2006/01/customui"><ribbon><tabs><tab id="FreeXTab" label="FreeX" /></tabs></ribbon></customUI>""");
            WriteTextEntry(archive, CustomUi14Path,
                """<customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui"><ribbon><tabs><tab id="FreeXTab14" label="FreeX 14" /></tabs></ribbon></customUI>""");

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            EnsureContentTypeOverride(contentTypesXml, CustomUiPath, CustomUiContentType);
            EnsureContentTypeOverride(contentTypesXml, CustomUi14Path, CustomUiContentType);
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var rootRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels");
            EnsureRelationship(rootRelsXml, "rIdCustomUi", CustomUiRelationshipType, CustomUiPath);
            EnsureRelationship(rootRelsXml, "rIdCustomUi14", CustomUi14RelationshipType, CustomUi14Path);
            ReplacePackageXml(archive, "_rels/.rels", rootRelsXml);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertCustomRibbonPackageGraph(Stream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        archive.GetEntry(CustomUiPath).Should().NotBeNull("custom ribbon UI XML must survive FreeX saves");
        archive.GetEntry(CustomUi14Path).Should().NotBeNull("Office 2010+ custom ribbon UI XML must survive FreeX saves");

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        AssertContentTypeOverride(contentTypesXml, CustomUiPath, CustomUiContentType);
        AssertContentTypeOverride(contentTypesXml, CustomUi14Path, CustomUiContentType);

        var rootRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels");
        AssertRelationship(rootRelsXml, CustomUiRelationshipType, CustomUiPath);
        AssertRelationship(rootRelsXml, CustomUi14RelationshipType, CustomUi14Path);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void EnsureContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        var root = contentTypesXml.Root!;
        var existing = root
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("PartName"), normalizedPartName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
            return;
        }

        root.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));
    }

    private static void EnsureRelationship(XDocument relationshipsXml, string id, string relationshipType, string target)
    {
        relationshipsXml.Root!.Add(new XElement(
            PackageRelationshipNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", target)));
    }

    private static void AssertContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                string.Equals((string?)element.Attribute("PartName"), normalizedPartName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("ContentType"), contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertRelationship(XDocument relationshipsXml, string relationshipType, string target)
    {
        relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                string.Equals((string?)element.Attribute("Type"), relationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)element.Attribute("Target"), target, StringComparison.OrdinalIgnoreCase) &&
                element.Attribute("TargetMode") == null);
    }
}
