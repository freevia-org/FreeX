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
        AssertExternalLinkGraph(source);
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
        AssertExternalLinkGraph(saved);
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
        AssertReloadedExternalLinkModel(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookExternalReferencesForSchemaValidity()
    {
        using var source = CreateExternalLinkSourcePackage();
        SetWorkbookExternalReferencesInvalidAttributes(source);
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
        SchemaErrors(saved).Should().BeEmpty();
        AssertExternalLinkPackage(saved);
        AssertExternalLinkGraph(saved);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var externalReferences = ReadWorkbookChildElement(saved, "externalReferences");
        externalReferences.Attribute("customExternalReferencesFlag").Should().BeNull();
        externalReferences.Element(workbookNs + "nativeExternalReferencesChild").Should().BeNull();

        var externalReference = externalReferences
            .Elements(workbookNs + "externalReference")
            .Should()
            .ContainSingle()
            .Subject;
        externalReference.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXExternalLink");
        externalReference.Attribute("customExternalReferenceFlag").Should().BeNull();
        externalReference.Elements().Should().BeEmpty();
        AssertReloadedExternalLinkModel(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidExternalLinkSidecarForSchemaValidity()
    {
        using var source = CreateExternalLinkSourcePackage();
        SetExternalLinkSidecarInvalidPayload(source);
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
        SchemaErrors(saved).Should().BeEmpty();
        // This fixture pads the cached sheetName/@val to " LinkedSheet " -- unlike the other
        // padding it sweeps up alongside (rIds, defined-name text, etc.), the leading/trailing
        // spaces here are legitimate Excel content that must survive verbatim, not get trimmed.
        AssertExternalLinkPackage(saved, expectedSheetName: " LinkedSheet ");
        AssertExternalLinkGraph(saved);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var externalLink = ReadPackageRootElement(saved, "xl/externalLinks/externalLink1.xml");
        externalLink.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Should().BeEmpty();
        externalLink.Element(workbookNs + "nativeExternalLinkChild").Should().BeNull();
        externalLink.Elements(workbookNs + "externalBook").Should().ContainSingle();

        var externalBook = externalLink.Element(workbookNs + "externalBook")!;
        externalBook.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXExternalBook");
        externalBook.Attribute("customExternalBookFlag").Should().BeNull();
        externalBook.Element(workbookNs + "nativeExternalBookChild").Should().BeNull();

        var sheetNames = externalBook.Element(workbookNs + "sheetNames")!;
        sheetNames.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).Should().BeEmpty();
        var sheetName = sheetNames.Elements(workbookNs + "sheetName").Should().ContainSingle().Subject;
        // Leading/trailing spaces in a cached sheetName/@val are legitimate Excel content (Excel
        // permits them in sheet names) and must survive normalization verbatim, because the same
        // untrimmed name is separately embedded in any dependent formula's quoted sheet qualifier
        // (e.g. '[1]Sheet 1 '!A1) -- trimming just this cached copy would desync the two
        // representations and break external-reference resolution on the next load.
        sheetName.Attribute("val")!.Value.Should().Be(" LinkedSheet ");
        sheetName.Attribute("customSheetNameFlag").Should().BeNull();
        sheetName.Elements().Should().BeEmpty();

        var definedName = externalBook
            .Element(workbookNs + "definedNames")!
            .Elements(workbookNs + "definedName")
            .Should()
            .ContainSingle()
            .Subject;
        definedName.Attribute("name")!.Value.Should().Be("ExternalName");
        definedName.Attribute("refersTo")!.Value.Should().Be("LinkedSheet!$A$1");
        definedName.Attribute("sheetId")!.Value.Should().Be("0");
        definedName.Attribute("customDefinedNameFlag").Should().BeNull();
        definedName.Elements().Should().BeEmpty();
        AssertReloadedExternalLinkModel(adapter, saved);
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

    private static void AssertExternalLinkPackage(Stream stream, string expectedSheetName = "LinkedSheet")
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

        // Leading/trailing spaces (when the fixture pads the cached name -- see
        // SetExternalLinkSidecarInvalidPayload) are legitimate Excel content and must survive
        // normalization verbatim; see NormalizeSheetNameValueAttribute for why.
        ReadPackageRootElement(stream, "xl/externalLinks/externalLink1.xml")
            .Element(workbookNs + "externalBook")!
            .Element(workbookNs + "sheetNames")!
            .Element(workbookNs + "sheetName")!
            .Attribute("val")!
            .Value
            .Should()
            .Be(expectedSheetName);

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

    private static void AssertExternalLinkGraph(Stream stream)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var externalReferenceId = ReadWorkbookChildElement(stream, "externalReferences")
            .Elements(workbookNs + "externalReference")
            .Should()
            .ContainSingle()
            .Subject
            .Attribute(relNs + "id")!
            .Value;

        var workbookRelationship = ReadPackageRootElement(stream, "xl/_rels/workbook.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == externalReferenceId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink")
            .Should()
            .ContainSingle()
            .Subject;
        workbookRelationship.Attribute("Target")!.Value.Should().Be("externalLinks/externalLink1.xml");

        var externalLinkPart = "xl/" + workbookRelationship.Attribute("Target")!.Value;
        var externalBookRelationshipId = ReadPackageRootElement(stream, externalLinkPart)
            .Element(workbookNs + "externalBook")!
            .Attribute(relNs + "id")!
            .Value;

        ReadPackageRootElement(stream, "xl/externalLinks/_rels/externalLink1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == externalBookRelationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath" &&
                relationship.Attribute("Target")?.Value == "linked-workbook.xlsx" &&
                relationship.Attribute("TargetMode")?.Value == "External")
            .Should()
            .ContainSingle();
    }

    private static void AssertReloadedExternalLinkModel(XlsxFileAdapter adapter, Stream stream)
    {
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        reloaded.GetSheetAt(0).GetCell(3, 3)!.Value.Should().Be(new NumberValue(42));
        reloaded.ExternalLinks.Should().ContainSingle(link =>
            link.PackagePart == "xl/externalLinks/externalLink1.xml" &&
            link.TargetUri == "linked-workbook.xlsx" &&
            link.TargetMode == "External");
    }

    private static void SetWorkbookExternalReferencesInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var externalReferences = workbookXml.Root!.Element(workbookNs + "externalReferences")!;
        externalReferences.SetAttributeValue("customExternalReferencesFlag", "removed");
        externalReferences.Add(new XElement(workbookNs + "nativeExternalReferencesChild"));

        var externalReference = externalReferences.Element(workbookNs + "externalReference")!;
        externalReference.SetAttributeValue(relNs + "id", " rIdFreeXExternalLink ");
        externalReference.SetAttributeValue("customExternalReferenceFlag", "removed");
        externalReference.Add(new XElement(workbookNs + "nativeExternalReferenceChild"));
        externalReferences.Add(new XElement(workbookNs + "externalReference", new XAttribute(relNs + "id", " ")));
        externalReferences.Add(new XElement(workbookNs + "externalReference", new XAttribute(relNs + "id", "rIdFreeXExternalLink")));

        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetExternalLinkSidecarInvalidPayload(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var externalLinkXml = LoadPackageXml(archive, "xl/externalLinks/externalLink1.xml");
        var externalLink = externalLinkXml.Root!;
        externalLink.SetAttributeValue("customExternalLinkFlag", "removed");
        externalLink.AddFirst(new XElement(workbookNs + "nativeExternalLinkChild"));

        var externalBook = externalLink.Element(workbookNs + "externalBook")!;
        externalBook.SetAttributeValue(relNs + "id", " rIdFreeXExternalBook ");
        externalBook.SetAttributeValue("customExternalBookFlag", "removed");
        externalBook.AddFirst(new XElement(workbookNs + "nativeExternalBookChild"));

        var sheetNames = externalBook.Element(workbookNs + "sheetNames")!;
        sheetNames.SetAttributeValue("count", "1");
        sheetNames.Add(new XElement(workbookNs + "nativeSheetNamesChild"));
        var sheetName = sheetNames.Element(workbookNs + "sheetName")!;
        sheetName.SetAttributeValue("val", " LinkedSheet ");
        sheetName.SetAttributeValue("customSheetNameFlag", "removed");
        sheetName.Add(new XElement(workbookNs + "nativeSheetNameChild"));
        sheetNames.Add(new XElement(workbookNs + "sheetName", new XAttribute("val", " ")));

        externalBook.Add(new XElement(
            workbookNs + "definedNames",
            new XAttribute("count", "1"),
            new XElement(
                workbookNs + "definedName",
                new XAttribute("name", " ExternalName "),
                new XAttribute("refersTo", " LinkedSheet!$A$1 "),
                new XAttribute("sheetId", " 0 "),
                new XAttribute("customDefinedNameFlag", "removed"),
                new XElement(workbookNs + "nativeDefinedNameChild")),
            new XElement(workbookNs + "definedName", new XAttribute("name", " ")),
            new XElement(workbookNs + "nativeDefinedNamesChild")));
        externalLink.Add(new XElement(
            workbookNs + "externalBook",
            new XAttribute(relNs + "id", "rIdDuplicateExternalBook")));

        ReplacePackageXml(archive, "xl/externalLinks/externalLink1.xml", externalLinkXml);
    }
}
