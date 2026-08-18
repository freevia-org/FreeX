using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R143-io-external-link-authoring-path-collision-1: two DIFFERENT external workbooks that happen
/// to share only their filename (e.g. "C:\Data\2024\Budget.xlsx" and "C:\Data\2025\Budget.xlsx")
/// must not collapse into a single ordinal/part. Before the fix,
/// <c>XlsxExternalLinkAuthoringWriter.NormalizeBookKey</c> stripped every book reference down to its
/// bare filename before grouping/keying (<c>CollectAlreadyBackedBookParts</c>,
/// <c>BuildBookKeyOrdinals</c>, <c>CollectDistinctReferences</c> all key off it), so a second
/// pre-existing book sharing a filename with the first silently lost its own dictionary slot
/// (<c>Dictionary&lt;string,...&gt;.TryAdd</c> is a no-op on a repeat key) the moment BOTH books were
/// present in the same workbook -- and a freshly typed bare-filename reference that should have
/// become its OWN third external link instead got treated as "already backed" by whichever of the
/// two collided books happened to win the key race, silently reusing that (wrong, or at least
/// arbitrary) book's ordinal.
/// <para>
/// Exercises the real end-to-end <see cref="XlsxFileAdapter"/> Load → edit → Save pipeline (the
/// production call site for this bug is
/// <c>XlsxFileAdapter.SourcePackage.cs:101</c>, <c>XlsxExternalLinkAuthoringWriter.Save(generatedArchive, workbook)</c>,
/// invoked from <c>PreserveSourcePackageParts</c>) rather than calling the writer directly, so the
/// full real chain -- <see cref="XlsxExternalLinkReferencePreserver"/> carrying the two pre-existing
/// links forward, then this writer's "already backed" scan and formula-ordinal rewrite running on top
/// -- is what is actually under test.
/// </para>
/// </summary>
public sealed class R143_ExternalLinkAuthoringWriterPathCollisionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// The primary proof. A source package already carries TWO real, distinct external links whose
    /// book paths share the identical filename ("Budget.xlsx") in two different directories -- exactly
    /// the shape a real Excel-authored workbook has for closed-workbook links. The user then types a
    /// brand-new, bare-filename external reference ('[Budget.xlsx]Data2'!A1) and saves. That new
    /// reference must become its OWN third external-link part (Target == "Budget.xlsx" verbatim, as
    /// typed) -- it must NOT silently collapse onto either pre-existing full-path book's ordinal, and
    /// neither pre-existing book may be dropped or merged with the other.
    /// </summary>
    [Fact]
    public void PreExistingBooksSharingAFilenameInDifferentDirectories_StayDistinct_AndNewBareFilenameRefGetsItsOwnSlot()
    {
        using var source = CreateSourcePackageWithTwoSameNameBooks();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // A brand-new, bare-filename bracket reference -- the shape XlsxExternalLinkAuthoringWriter's
        // own doc comment scopes itself to, and the one shape that both FreeX's lexer/parser AND
        // ClosedXML's own formula grammar accept for a fresh cell entry.
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 1), "'[Budget.xlsx]Data2'!A1");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var externalReferences = workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .ToList();
        externalReferences.Should().HaveCount(3,
            "the two pre-existing same-filename books must each keep their own ordinal slot, and the freshly typed reference must get a third");

        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var backedTargets = externalReferences.Select(externalReference => ResolveBookTarget(archive, workbookRelsXml, externalReference)).ToList();

        // Both original full paths must survive, untouched and undropped.
        backedTargets.Should().Contain(@"C:\Data\2024\Budget.xlsx");
        backedTargets.Should().Contain(@"C:\Data\2025\Budget.xlsx");
        // The freshly typed reference must be backed by its OWN part, carrying exactly what was
        // typed -- not silently merged into (and thereby masquerading as) either existing full path.
        backedTargets.Should().Contain("Budget.xlsx");
        backedTargets.Should().OnlyHaveUniqueItems();

        // The new formula's <f> text must have been rewritten to the ordinal of ITS OWN part (the
        // one whose Target is the bare "Budget.xlsx" it was typed with), not silently repointed at
        // either pre-existing full-path book's ordinal.
        var ownOrdinal = externalReferences.IndexOf(externalReferences.Single(externalReference =>
            ResolveBookTarget(archive, workbookRelsXml, externalReference) == "Budget.xlsx")) + 1;
        var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
        var newFormulaText = worksheetXml.Root!
            .Element(WorkbookNs + "sheetData")!
            .Elements(WorkbookNs + "row")
            .Elements(WorkbookNs + "c")
            .Single(cell => cell.Attribute("r")?.Value == "A5")
            .Element(WorkbookNs + "f")!.Value;
        newFormulaText.Should().Be($"'[{ownOrdinal}]Data2'!A1",
            "the freshly typed bare-filename reference must resolve to its own new part's ordinal, not an unrelated pre-existing book's");
    }

    /// <summary>
    /// No-regression sibling: TWO pre-existing external links to genuinely DIFFERENT books (different
    /// filenames, so no collision key involved either way) must still both survive a save untouched,
    /// each keeping its own distinct ordinal and Target -- proves the fix didn't break the ordinary,
    /// no-collision multi-external-link case.
    /// </summary>
    [Fact]
    public void PreExistingBooksWithDifferentFilenames_BothSurviveSaveUnchanged()
    {
        using var source = CreateSourcePackageWithTwoDifferentlyNamedBooks();
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

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var externalReferences = workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .ToList();
        externalReferences.Should().HaveCount(2);

        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var backedTargets = externalReferences.Select(externalReference => ResolveBookTarget(archive, workbookRelsXml, externalReference)).ToList();
        backedTargets.Should().BeEquivalentTo(new[] { @"C:\Data\2024\Budget.xlsx", @"C:\Data\2025\Ledger.xlsx" });
    }

    private static string ResolveBookTarget(ZipArchive archive, XDocument workbookRelsXml, XElement externalReference)
    {
        var relId = externalReference.Attribute(RelNs + "id")!.Value;
        var relationship = workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(element => element.Attribute("Id")?.Value == relId);
        var partPath = "xl/" + relationship.Attribute("Target")!.Value.TrimStart('/');

        var partXml = LoadXml(archive, partPath);
        var bookRelId = partXml.Root!.Element(WorkbookNs + "externalBook")!.Attribute(RelNs + "id")!.Value;
        var partRelsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
        var partRelsXml = LoadXml(archive, partRelsPath);
        var bookRelationship = partRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(element => element.Attribute("Id")?.Value == bookRelId);
        bookRelationship.Attribute("Type")!.Value.Should().Be(ExternalLinkPathRelationshipType);
        return bookRelationship.Attribute("Target")!.Value;
    }

    // ── fixture construction (mirrors R96_ExternalLinkAuthoringWriterTests) ──

    private static MemoryStream CreateSourcePackageWithTwoSameNameBooks() =>
        CreateSourcePackageWithTwoBooks(@"C:\Data\2024\Budget.xlsx", @"C:\Data\2025\Budget.xlsx");

    private static MemoryStream CreateSourcePackageWithTwoDifferentlyNamedBooks() =>
        CreateSourcePackageWithTwoBooks(@"C:\Data\2024\Budget.xlsx", @"C:\Data\2025\Ledger.xlsx");

    private static MemoryStream CreateSourcePackageWithTwoBooks(string bookPathA, string bookPathB)
    {
        var workbook = new Workbook("ExternalLinkPathCollisionSibling");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(stream, bookPathA, bookPathB);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPackage(MemoryStream stream, string bookPathA, string bookPathB)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        AddContentTypeOverride(archive, "/xl/externalLinks/externalLink1.xml", ExternalLinkContentType);
        AddContentTypeOverride(archive, "/xl/externalLinks/externalLink2.xml", ExternalLinkContentType);

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
        workbookXml.Root.Add(new XElement(
            WorkbookNs + "externalReferences",
            new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLinkA")),
            new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLinkB"))));
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Add(
            new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXExternalLinkA"),
                new XAttribute("Type", ExternalLinkRelationshipType),
                new XAttribute("Target", "externalLinks/externalLink1.xml")),
            new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXExternalLinkB"),
                new XAttribute("Type", ExternalLinkRelationshipType),
                new XAttribute("Target", "externalLinks/externalLink2.xml")));
        ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

        AddExternalLinkPart(archive, "xl/externalLinks/externalLink1.xml", "rIdFreeXExternalBookA", bookPathA);
        AddExternalLinkPart(archive, "xl/externalLinks/externalLink2.xml", "rIdFreeXExternalBookB", bookPathB);
    }

    private static void AddExternalLinkPart(ZipArchive archive, string partPath, string bookRelId, string bookPath)
    {
        ReplaceXml(archive, partPath, new XDocument(
            new XElement(
                WorkbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(
                    WorkbookNs + "externalBook",
                    new XAttribute(RelNs + "id", bookRelId),
                    new XElement(
                        WorkbookNs + "sheetNames",
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Sheet1")))))));

        ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(partPath), new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", bookRelId),
                    new XAttribute("Type", ExternalLinkPathRelationshipType),
                    new XAttribute("Target", bookPath),
                    new XAttribute("TargetMode", "External")))));
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
