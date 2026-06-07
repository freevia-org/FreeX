using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxVolatileDependenciesPackageGraphTests
{
    private const string VolatileDependenciesPath = "xl/volatileDependencies.xml";
    private const string VolatileDependenciesContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.volatileDependencies+xml";
    private const string VolatileDependenciesRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/volatileDependencies";

    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void LoadedWorkbookFullSave_PreservesVolatileDependenciesPackageGraphAlongsideModelEdits()
    {
        using var source = CreateWorkbookWithVolatileDependenciesPackageGraph();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        AssertVolatileDependenciesPackageGraph(saved);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(1, 1).Should().Be(new TextValue("edited"));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_PreservesVolatileDependenciesPackageGraphAlongsideCellEdits()
    {
        using var source = CreateWorkbookWithVolatileDependenciesPackageGraph();
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
        AssertVolatileDependenciesPackageGraph(saved);

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 1).Should().Be(new NumberValue(42));
    }

    private static MemoryStream CreateWorkbookWithVolatileDependenciesPackageGraph()
    {
        var workbook = new Workbook("VolatileDependenciesPackageGraph");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteTextEntry(archive, VolatileDependenciesPath, $"""
                <volTypes xmlns="{SpreadsheetNs}">
                  <volType type="realTimeData">
                    <main first="1"/>
                  </volType>
                </volTypes>
                """);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            EnsureContentTypeOverride(contentTypesXml, VolatileDependenciesPath, VolatileDependenciesContentType);
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            EnsureRelationship(workbookRelsXml, "rIdVolatileDependencies", VolatileDependenciesRelationshipType, "volatileDependencies.xml");
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertVolatileDependenciesPackageGraph(Stream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var volatileDependencies = archive.GetEntry(VolatileDependenciesPath);
        volatileDependencies.Should().NotBeNull("Excel-authored volatile dependency metadata must survive FreeX saves");
        XlsxPackageTestFixtures.LoadPackageXml(archive, VolatileDependenciesPath)
            .Root!
            .Name
            .Should()
            .Be(SpreadsheetNs + "volTypes");

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        AssertContentTypeOverride(contentTypesXml, VolatileDependenciesPath, VolatileDependenciesContentType);

        var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        AssertRelationship(workbookRelsXml, VolatileDependenciesRelationshipType, "volatileDependencies.xml");
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
