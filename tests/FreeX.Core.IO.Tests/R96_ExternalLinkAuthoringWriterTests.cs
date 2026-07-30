using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R96-io-external-link-writer-1: FreeX's formula lexer/parser fully accepts a freshly TYPED
/// bracketed external-workbook reference (<c>'[Book.xlsx]Sheet1'!A1</c>), but nothing ever
/// synthesized the supporting OOXML infrastructure Excel always writes alongside it -- the
/// <c>xl/externalLinks/externalLinkN.xml</c> part, its own <c>externalLinkPath</c> relationship, the
/// workbook-level <c>externalLink</c> relationship/<c>&lt;externalReference&gt;</c> entry, and the
/// content-type Override. A save emitted the literal formula text with none of that backing -- a
/// shape real Excel never produces on its own. Fixed by
/// <c>XlsxExternalLinkAuthoringWriter</c>, wired into both the fresh-workbook save path
/// (<c>XlsxFileAdapter.SavePostProcessing.cs</c>) and the source-package save path
/// (<c>XlsxFileAdapter.SourcePackage.cs</c>, right after <c>XlsxExternalLinkReferencePreserver</c>).
/// </summary>
public sealed class R96_ExternalLinkAuthoringWriterTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// The primary proof: a brand-new workbook (never loaded from a .xlsx -- the "!hasSourcePackage"
    /// save path) whose only external-workbook content is a freshly typed formula must, on save,
    /// carry the full backing chain a real Excel file would have: workbook.xml
    /// externalReferences/externalReference -&gt; workbook.xml.rels externalLink relationship -&gt;
    /// xl/externalLinks/externalLink1.xml (externalBook/sheetNames) -&gt; that part's own _rels
    /// externalLinkPath relationship (TargetMode=External, Target=the typed book name) -&gt; a matching
    /// [Content_Types].xml Override. Before the fix, none of this existed -- the formula's literal
    /// bracketed text was the only trace of the reference in the saved package.
    /// </summary>
    [Fact]
    public void FreshWorkbook_TypedExternalWorkbookFormula_SynthesizesFullExternalLinkBackingChain()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        // The real product entry point for "a formula was typed into a cell" at the model layer
        // this test project can reach (CellEntryParser lives in FreeX.App.Services, a layer above
        // Core.IO); every existing Core.IO formula-round-trip test uses this same seam.
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "'[Budget.xlsx]Data'!A1");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // 1. workbook.xml carries a fresh <externalReference>.
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var externalReference = workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")?
            .Elements(WorkbookNs + "externalReference")
            .SingleOrDefault();
        externalReference.Should().NotBeNull("a typed bracketed external reference must produce a workbook-level externalReference entry");
        var relId = externalReference!.Attribute(RelNs + "id")!.Value;

        // 2. workbook.xml.rels resolves that r:id to an internal externalLink part.
        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var relationship = workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(element => element.Attribute("Id")?.Value == relId);
        relationship.Should().NotBeNull();
        relationship!.Attribute("Type")!.Value.Should().Be(ExternalLinkRelationshipType);
        relationship.Attribute("TargetMode")?.Value.Should().NotBe("External", "the workbook-level relationship must target a package part, not the external file directly");

        var partPath = "xl/" + relationship.Attribute("Target")!.Value.TrimStart('/');
        archive.GetEntry(partPath).Should().NotBeNull($"{partPath} must exist as a real package part");

        // 3. The externalLink part carries externalBook/sheetNames for the referenced sheet.
        var externalLinkXml = LoadXml(archive, partPath);
        externalLinkXml.Root!.Name.Should().Be(WorkbookNs + "externalLink");
        var externalBook = externalLinkXml.Root.Element(WorkbookNs + "externalBook");
        externalBook.Should().NotBeNull();
        var sheetNameVal = externalBook!
            .Element(WorkbookNs + "sheetNames")?
            .Elements(WorkbookNs + "sheetName")
            .Select(element => element.Attribute("val")?.Value)
            .SingleOrDefault();
        sheetNameVal.Should().Be("Data");

        // 4. The externalLink part's own _rels resolves externalBook/@r:id to an EXTERNAL
        //    externalLinkPath relationship carrying the typed book name.
        var bookRelId = externalBook.Attribute(RelNs + "id")!.Value;
        var partRelsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
        var partRelsXml = LoadXml(archive, partRelsPath);
        var bookRelationship = partRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(element => element.Attribute("Id")?.Value == bookRelId);
        bookRelationship.Should().NotBeNull();
        bookRelationship!.Attribute("Type")!.Value.Should().Be(ExternalLinkPathRelationshipType);
        bookRelationship.Attribute("Target")!.Value.Should().Be("Budget.xlsx");
        bookRelationship.Attribute("TargetMode")!.Value.Should().Be("External");

        // 5. [Content_Types].xml has a matching Override.
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var contentTypeOverride = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .SingleOrDefault(element => string.Equals(
                element.Attribute("PartName")?.Value?.TrimStart('/'),
                partPath,
                System.StringComparison.OrdinalIgnoreCase));
        contentTypeOverride.Should().NotBeNull();
        contentTypeOverride!.Attribute("ContentType")!.Value.Should().Be(ExternalLinkContentType);

        // 6. The whole package graph is internally consistent by FreeX's own health validator.
        var issues = XlsxPackageHealthValidator.Validate(archive);
        issues.Where(issue => issue.Contains("external", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }

    /// <summary>
    /// No-regression sibling: a workbook LOADED from a source package that already carries a valid
    /// external link (the R95-style fixture) must not get a second, duplicate externalLinkN.xml part
    /// synthesized on top of the preserved one just because the same formula still contains the
    /// bracketed reference text. Exercises the OTHER call site (inside
    /// <c>PreserveSourcePackageParts</c>, the <c>hasSourcePackage</c> save path) and proves the
    /// "already backed" idempotency scan actually works, not just that it compiles.
    /// </summary>
    [Fact]
    public void SourceLoadedWorkbook_PreExistingExternalLink_IsNotDuplicatedOnSave()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        // Touch an unrelated cell so the save goes through the real edited-workbook save flow.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        var externalLinkPartCount = archive.Entries.Count(entry =>
            entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase));
        externalLinkPartCount.Should().Be(1, "the pre-existing external link for Linked.xlsx must be carried forward, not duplicated alongside a freshly synthesized twin");

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .Should().ContainSingle();
    }

    /// <summary>
    /// No-regression sibling: saving the SAME in-memory workbook object twice in a row (no reload in
    /// between) must not accumulate a second externalLinkN.xml part for the same freshly typed
    /// reference. This is the scenario the "already backed" scan is specifically designed to catch --
    /// the in-memory <see cref="Workbook.ExternalLinks"/> model is never populated by this writer (by
    /// design, to stay scoped to writer support only), so if the idempotency check instead looked at
    /// that model it would wrongly treat the SAME reference as still-unbacked on every subsequent save
    /// and pile up a new part each time.
    /// </summary>
    [Fact]
    public void RepeatedSaveOfSameWorkbook_DoesNotAccumulateDuplicateExternalLinkParts()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "'[Budget.xlsx]Data'!A1");

        var adapter = new XlsxFileAdapter();
        using (var first = new MemoryStream())
            adapter.Save(workbook, first);

        using var second = new MemoryStream();
        adapter.Save(workbook, second);

        second.Position = 0;
        using var archive = new ZipArchive(second, ZipArchiveMode.Read, leaveOpen: true);
        var externalLinkPartCount = archive.Entries.Count(entry =>
            entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase));
        externalLinkPartCount.Should().Be(1);

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .Should().ContainSingle();
    }

    // ── fixture construction (mirrors R95_ExternalLinkSheetNameWhitespacePreservationTests) ──

    private static MemoryStream CreateSourcePackageWithExternalFormula()
    {
        var workbook = new Workbook("ExternalLinkAuthoringSibling");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(stream);
        AddExternalFormulaCell(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        AddContentTypeOverride(archive, "/xl/externalLinks/externalLink1.xml", ExternalLinkContentType);

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
        workbookXml.Root.Add(new XElement(
            WorkbookNs + "externalReferences",
            new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLink"))));
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink"),
            new XAttribute("Type", ExternalLinkRelationshipType),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

        ReplaceXml(archive, "xl/externalLinks/externalLink1.xml", new XDocument(
            new XElement(
                WorkbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(
                    WorkbookNs + "externalBook",
                    new XAttribute(RelNs + "id", "rIdFreeXExternalBook"),
                    new XElement(
                        WorkbookNs + "sheetNames",
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Data")))))));

        ReplaceXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", ExternalLinkPathRelationshipType),
                    new XAttribute("Target", "Linked.xlsx"),
                    new XAttribute("TargetMode", "External")))));
    }

    private static void AddExternalFormulaCell(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var sheetData = worksheetXml.Root!.Element(WorkbookNs + "sheetData")!;
        sheetData.Add(new XElement(
            WorkbookNs + "row",
            new XAttribute("r", "20"),
            new XElement(
                WorkbookNs + "c",
                new XAttribute("r", "A20"),
                new XElement(WorkbookNs + "f", "'[1]Data'!A1"),
                new XElement(WorkbookNs + "v", "123"))));
        ReplaceXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var overrideElement = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(element.Attribute("PartName")?.Value, partName, System.StringComparison.OrdinalIgnoreCase));
        if (overrideElement is null)
        {
            contentTypesXml.Root.Add(new XElement(
                ContentTypeNs + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }
        else
        {
            overrideElement.SetAttributeValue("ContentType", contentType);
        }

        ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException(entryName);
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        var existing = archive.GetEntry(entryName);
        existing?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        document.Save(entryStream, SaveOptions.DisableFormatting);
    }
}
