using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void ExternalLinksPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateExternalLinkSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
        AssertExternalLinkPackage(source);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithExternalLinks_ProducesSchemaValidWorkbook()
    {
        using var source = CreateExternalLinkSourcePackage();
        var sourceExternalReferences = ReadWorkbookChildElement(source, "externalReferences");
        var sourceWorkbookRelationships = ReadPackageRootElement(source, "xl/_rels/workbook.xml.rels");
        var sourceExternalLink = ReadPackageRootElement(source, "xl/externalLinks/externalLink1.xml");
        var sourceExternalLinkRelationships = ReadPackageRootElement(source, "xl/externalLinks/_rels/externalLink1.xml.rels");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.ExternalLinks.Should().ContainSingle(link =>
            link.PackagePart == "xl/externalLinks/externalLink1.xml" &&
            link.TargetUri == "linked-workbook.xlsx" &&
            link.TargetMode == "External");
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
        AssertExternalLinkPackage(saved);
        ReadWorkbookChildElement(saved, "externalReferences")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExternalReferences.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/_rels/workbook.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/externalLinks/externalLink1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExternalLink.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/externalLinks/_rels/externalLink1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExternalLinkRelationships.ToString(SaveOptions.DisableFormatting));
    }

    private static MemoryStream CreateExternalLinkSourcePackage()
    {
        var workbook = new Workbook("ExternalLinkPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("external link"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddExternalLinkPackage(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        AddPackageContentTypeOverride(
            archive,
            "/xl/externalLinks/externalLink1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(workbookNs + "externalReferences").Remove();
        InsertExternalReferencesInOrder(workbookXml.Root, workbookNs, new XElement(
            workbookNs + "externalReferences",
            new XElement(workbookNs + "externalReference", new XAttribute(relNs + "id", "rIdFreeXExternalLink"))));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadPackageXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXExternalLink" ||
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink" ||
                relationship.Attribute("Target")?.Value == "externalLinks/externalLink1.xml")
            .Remove();
        workbookRelationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        ReplacePackageXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

        ReplacePackageXml(archive, "xl/externalLinks/externalLink1.xml", new XDocument(
            new XElement(
                workbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", relNs),
                new XElement(
                    workbookNs + "externalBook",
                    new XAttribute(relNs + "id", "rIdFreeXExternalBook"),
                    new XElement(workbookNs + "sheetNames",
                        new XElement(workbookNs + "sheetName", new XAttribute("val", "LinkedSheet")))))));
        ReplacePackageXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                packageRelNs + "Relationships",
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("Target", "linked-workbook.xlsx"),
                    new XAttribute("TargetMode", "External")))));
    }

    private static void InsertExternalReferencesInOrder(
        XElement workbookRoot,
        XNamespace workbookNs,
        XElement externalReferences)
    {
        string[] laterWorkbookElements =
        [
            "definedNames",
            "calcPr",
            "oleSize",
            "customWorkbookViews",
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects",
            "extLst"
        ];

        var insertionPoint = workbookRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            workbookRoot.Add(externalReferences);
        else
            insertionPoint.AddBeforeSelf(externalReferences);
    }

    private static void AssertExternalLinkPackage(Stream stream)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        ReadWorkbookChildElement(stream, "externalReferences")
            .Elements(workbookNs + "externalReference")
            .Single()
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be("rIdFreeXExternalLink");

        ReadPackageRootElement(stream, "xl/_rels/workbook.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXExternalLink" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink" &&
                relationship.Attribute("Target")?.Value == "externalLinks/externalLink1.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "xl/externalLinks/externalLink1.xml")
            .Element(workbookNs + "externalBook")!
            .Element(workbookNs + "sheetNames")!
            .Element(workbookNs + "sheetName")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("LinkedSheet");

        ReadPackageRootElement(stream, "xl/externalLinks/_rels/externalLink1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXExternalBook" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath" &&
                relationship.Attribute("Target")?.Value == "linked-workbook.xlsx" &&
                relationship.Attribute("TargetMode")?.Value == "External")
            .Should()
            .ContainSingle();
    }
}
