using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSharedStringsPackageGraphTests
{
    private const string SharedStringsPath = "xl/sharedStrings.xml";
    private const string WorkbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string SharedStringsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";

    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void LoadedWorkbookFullSave_PreservesRichSharedStringsAndPrunesDuplicateWorkbookRelationships()
    {
        using var source = CreateWorkbookWithRichSharedStringAndDuplicateRelationship();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("full save edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        AssertSharedStringsPackageGraph(saved);
        AssertRichSharedStringMetadata(saved, "Rich phonetic");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 2).Should().Be(new TextValue("full save edit"));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_PreservesRichSharedStringsAndPrunesDuplicateWorkbookRelationships()
    {
        using var source = CreateWorkbookWithRichSharedStringAndDuplicateRelationship();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        AssertSharedStringsPackageGraph(saved);
        AssertRichSharedStringMetadata(saved, "Rich phonetic");

        saved.Position = 0;
        adapter.Load(saved).GetSheetAt(0).GetValue(2, 2).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void NormalizePackage_WithSharedStringsPart_RepairsMissingContentTypeAndWorkbookRelationship()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", $"""
                <Types xmlns="{ContentTypeNs}">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", $"""<workbook xmlns="{SpreadsheetNs}"/>"""),
            (WorkbookRelationshipsPath, $"""<Relationships xmlns="{PackageRelationshipNs}"/>"""),
            (SharedStringsPath, $"""
                <sst xmlns="{SpreadsheetNs}">
                  <si><t>orphaned text</t></si>
                </sst>
                """));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringPackageGraphNormalizer.NormalizePackage(archive);
        }

        AssertSharedStringsPackageGraph(package);
    }

    [Fact]
    public void NormalizePackage_WithoutSharedStringsPart_RemovesStaleContentTypeAndWorkbookRelationship()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", $"""
                <Types xmlns="{ContentTypeNs}">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/sharedStrings.xml" ContentType="{SharedStringsContentType}"/>
                </Types>
                """),
            ("xl/workbook.xml", $"""<workbook xmlns="{SpreadsheetNs}"/>"""),
            (WorkbookRelationshipsPath, $"""
                <Relationships xmlns="{PackageRelationshipNs}">
                  <Relationship Id="rIdSharedStrings" Type="{SharedStringsRelationshipType}" Target="sharedStrings.xml"/>
                </Relationships>
                """));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxSharedStringPackageGraphNormalizer.NormalizePackage(archive);
        }

        package.Position = 0;
        using var verifyArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        verifyArchive.GetEntry(SharedStringsPath).Should().BeNull();

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Any(IsSharedStringsContentTypeOverride)
            .Should()
            .BeFalse();

        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, WorkbookRelationshipsPath);
        relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Any(IsSharedStringsRelationship)
            .Should()
            .BeFalse();
    }

    private static MemoryStream CreateWorkbookWithRichSharedStringAndDuplicateRelationship()
    {
        var workbook = new Workbook("SharedStringsPackageGraph");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Rich phonetic"));

        var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddSharedStringRichTextAndPhonetics(archive);
            var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, WorkbookRelationshipsPath);
            workbookRelsXml.Root!.Add(new XElement(
                PackageRelationshipNs + "Relationship",
                new XAttribute("Id", "rIdDuplicateSharedStrings"),
                new XAttribute("Type", SharedStringsRelationshipType),
                new XAttribute("Target", "sharedStrings.xml")));
            ReplacePackageXml(archive, WorkbookRelationshipsPath, workbookRelsXml);
        }

        package.Position = 0;
        return package;
    }

    private static void AddSharedStringRichTextAndPhonetics(ZipArchive archive)
    {
        var sharedStringsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, SharedStringsPath);
        var sharedString = sharedStringsXml.Root!
            .Elements(SpreadsheetNs + "si")
            .Single(element => element.Element(SpreadsheetNs + "t")?.Value == "Rich phonetic");
        sharedString.ReplaceNodes(
            new XElement(
                SpreadsheetNs + "r",
                new XElement(
                    SpreadsheetNs + "rPr",
                    new XElement(SpreadsheetNs + "b"),
                    new XElement(SpreadsheetNs + "rFont", new XAttribute("val", "FreeXRich"))),
                new XElement(SpreadsheetNs + "t", "Rich ")),
            new XElement(
                SpreadsheetNs + "r",
                new XElement(SpreadsheetNs + "t", "phonetic")),
            new XElement(
                SpreadsheetNs + "rPh",
                new XAttribute("sb", "0"),
                new XAttribute("eb", "4"),
                new XElement(SpreadsheetNs + "t", "ri-chi")),
            new XElement(
                SpreadsheetNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "noConversion")));
        ReplacePackageXml(archive, SharedStringsPath, sharedStringsXml);
    }

    private static void AssertSharedStringsPackageGraph(Stream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        archive.GetEntry(SharedStringsPath).Should().NotBeNull();

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Where(IsSharedStringsContentTypeOverride)
            .Should()
            .ContainSingle();

        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, WorkbookRelationshipsPath);
        relationshipsXml.Root!
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(element => string.Equals(
                element.Attribute("Type")?.Value,
                SharedStringsRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .Where(IsCurrentSharedStringsRelationship)
            .Should()
            .ContainSingle();
    }

    private static void AssertRichSharedStringMetadata(Stream package, string plainText)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var sharedString = XlsxPackageTestFixtures.LoadPackageXml(archive, SharedStringsPath)
            .Root!
            .Elements(SpreadsheetNs + "si")
            .Single(element => ReadSharedStringPlainText(element) == plainText);

        sharedString.Elements(SpreadsheetNs + "r").Should().HaveCount(2);
        sharedString.Element(SpreadsheetNs + "rPh").Should().NotBeNull();
        sharedString.Element(SpreadsheetNs + "phoneticPr")!
            .Attribute("type")!
            .Value
            .Should()
            .Be("noConversion");
    }

    private static string ReadSharedStringPlainText(XElement sharedString)
    {
        var runs = sharedString.Elements(SpreadsheetNs + "r").ToList();
        return runs.Count == 0
            ? sharedString.Element(SpreadsheetNs + "t")?.Value ?? string.Empty
            : string.Concat(runs.Select(run => run.Element(SpreadsheetNs + "t")?.Value ?? string.Empty));
    }

    private static bool IsSharedStringsContentTypeOverride(XElement element) =>
        string.Equals(element.Attribute("PartName")?.Value, "/xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(element.Attribute("ContentType")?.Value, SharedStringsContentType, StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedStringsRelationship(XElement element) =>
        string.Equals(element.Attribute("Type")?.Value, SharedStringsRelationshipType, StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentSharedStringsRelationship(XElement element) =>
        element.Attribute("TargetMode") is null &&
        string.Equals(
            XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", element.Attribute("Target")?.Value ?? ""),
            SharedStringsPath,
            StringComparison.OrdinalIgnoreCase);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
