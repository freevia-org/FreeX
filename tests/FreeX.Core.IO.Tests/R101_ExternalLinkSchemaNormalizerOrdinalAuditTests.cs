using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R101-io-external-link-schema-normalizer-audit: <c>XlsxExternalLinkSchemaNormalizer</c> was never
/// audited for the same blank/duplicate-collapse hazard already fixed three times over for its
/// SIBLING classes operating on <c>xl/workbook.xml</c>'s ordinal &lt;externalReference&gt; list (the
/// read side, the full-save side, and -- this same round -- the patch-save side in
/// <see cref="XlsxExternalLinkReferencePreserver"/>).
/// <para>
/// AUDIT RESULT: NOT_A_BUG. <c>XlsxExternalLinkSchemaNormalizer.NormalizePackage</c> only rewrites the
/// INTERNAL content of each already-existing <c>xl/externalLinks/externalLinkN.xml</c> PART (removing
/// unknown attributes/children, deduplicating a malformed second payload element within the SAME part,
/// normalizing child order, trimming/dropping a blank cached name) via
/// <c>XlsxPackageXmlEditor.ReplaceXml</c>, which replaces an existing zip entry's bytes in place. It
/// never calls <c>ZipArchiveEntry.Delete()</c>, never adds/removes a zip entry, and never touches
/// <c>xl/workbook.xml</c>, <c>xl/workbook.xml.rels</c>, or any externalLink part's own
/// <c>_rels/externalLinkN.xml.rels</c> file. Because the workbook-level ordinal list and every
/// externalLink-to-part relationship are established earlier in the save pipeline (by
/// <see cref="XlsxExternalLinkReferencePreserver"/> and <see cref="XlsxExternalLinkAuthoringWriter"/>,
/// both of which run and finish before this normalizer in
/// <c>XlsxFileAdapter.SourcePackage.cs</c>/<c>SourcePackageSnapshot.cs</c>) and this normalizer cannot
/// remove or rename a part, it cannot desync the ordinal list. This test locks that invariant in as a
/// guard: it feeds the normalizer a genuinely malformed externalLink part (duplicate payload element,
/// unknown attribute/child, blank cached sheet name) through the REAL full-rebuild save path and
/// asserts the part count, the workbook.xml ordinal slot count, the backing relationship, and a
/// formula's bracket-index resolution are all unaffected.
/// </para>
/// </summary>
public sealed class R101_ExternalLinkSchemaNormalizerOrdinalAuditTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    [Fact]
    public void Save_NormalizingMalformedExternalLinkPart_NeverAltersPartCountOrOrdinalList()
    {
        using var source = CreateWorkbookWithMalformedExternalLinkParts();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));
        // Forces the full ClosedXML-rebuild save path (the cell-patch fast path copies parts
        // byte-for-byte and never runs the normalizer's content-mutation logic at all).
        workbook.IsStructureProtected = true;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var externalLinkParts = archive.Entries
                .Where(e => e.FullName.StartsWith("xl/externalLinks/", System.StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
                            !e.FullName.Contains("_rels/", System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            externalLinkParts.Should().HaveCount(2, "the normalizer must never add or remove an externalLink part");

            var workbookXml = LoadXml(archive, "xl/workbook.xml");
            var externalReferences = workbookXml.Root!
                .Element(WorkbookNs + "externalReferences")!
                .Elements(WorkbookNs + "externalReference")
                .ToList();
            externalReferences.Should().HaveCount(2, "content normalization inside a part must never change the workbook-level ordinal slot count");

            // Confirm the normalizer actually DID mutate the malformed part's content (proving the
            // test fixture exercises real normalization, not a no-op), while still leaving the part
            // resolvable from workbook.xml.rels via its original r:id.
            var slot1RelId = externalReferences[1].Attribute(RelNs + "id")!.Value;
            var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var slot1Relationship = workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .SingleOrDefault(e => e.Attribute("Id")?.Value == slot1RelId);
            slot1Relationship.Should().NotBeNull("the malformed part's own workbook-level relationship must survive content normalization");

            var partPath = "xl/" + slot1Relationship!.Attribute("Target")!.Value.TrimStart('/');
            var normalizedPartXml = LoadXml(archive, partPath);
            normalizedPartXml.Root!.Attribute("bogusAttribute").Should().BeNull("unknown attributes must be stripped by normalization");
            normalizedPartXml.Root!.Elements(WorkbookNs + "externalBook").Should().HaveCount(1, "a malformed duplicate externalBook payload must be deduplicated to one");
        }

        // Real product consumer: reload the SAVED package and recalc through FormulaEvaluator, proving
        // the bracket-index formula still resolves to the correct external workbook after the
        // normalizer ran over its malformed sibling part.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);
        reloadedSheet.GetValue(20, 1).Should().Be(new NumberValue(222));
    }

    private static MemoryStream CreateWorkbookWithMalformedExternalLinkParts()
    {
        var workbook = new Workbook("ExternalLinkSchemaNormalizerAudit");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddContentTypeOverride(archive, "/xl/externalLinks/externalLink0.xml", ExternalLinkContentType);
            AddContentTypeOverride(archive, "/xl/externalLinks/externalLink1.xml", ExternalLinkContentType);

            var workbookRelationshipsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationshipsXml.Root!.Add(
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdBook0"),
                    new XAttribute("Type", ExternalLinkRelationshipType),
                    new XAttribute("Target", "externalLinks/externalLink0.xml")),
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdBook1"),
                    new XAttribute("Type", ExternalLinkRelationshipType),
                    new XAttribute("Target", "externalLinks/externalLink1.xml")));
            ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationshipsXml);

            // Part 0: clean, resolves to 111.
            AddCleanExternalLinkPart(archive, "xl/externalLinks/externalLink0.xml", "rIdPath0", "Book0.xlsx", 111);

            // Part 1: deliberately malformed -- unknown root attribute, a duplicate second
            // <externalBook> payload element, an unrecognized stray child element, and a blank
            // cached sheet-name entry -- everything NormalizeExternalLinkRoot/NormalizeExternalBookElement
            // is documented to clean up. Resolves to 222 (the FIRST, kept externalBook).
            AddMalformedExternalLinkPart(archive, "xl/externalLinks/externalLink1.xml", "rIdPath1", "Book1.xlsx", 222);

            var externalReferences = new XElement(
                WorkbookNs + "externalReferences",
                new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdBook0")),
                new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdBook1")));

            var workbookXml = LoadXml(archive, "xl/workbook.xml");
            workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
            InsertExternalReferencesInOrder(workbookXml.Root!, externalReferences);
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        AddExternalFormulaCell(stream, bracketIndex: 2, cachedValue: 222);
        stream.Position = 0;
        return stream;
    }

    private static void AddCleanExternalLinkPart(ZipArchive archive, string partPath, string bookRelId, string targetFileName, int cachedValue)
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
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Sheet1"))),
                    new XElement(
                        WorkbookNs + "sheetDataSet",
                        new XElement(
                            WorkbookNs + "sheetData",
                            new XAttribute("sheetId", "0"),
                            new XElement(
                                WorkbookNs + "row",
                                new XAttribute("r", "1"),
                                new XElement(
                                    WorkbookNs + "cell",
                                    new XAttribute("r", "A1"),
                                    new XElement(WorkbookNs + "v", cachedValue.ToString(System.Globalization.CultureInfo.InvariantCulture))))))))));

        WriteExternalLinkPartRels(archive, partPath, bookRelId, targetFileName);
    }

    private static void AddMalformedExternalLinkPart(ZipArchive archive, string partPath, string bookRelId, string targetFileName, int cachedValue)
    {
        ReplaceXml(archive, partPath, new XDocument(
            new XElement(
                WorkbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XAttribute("bogusAttribute", "shouldBeStripped"),
                // First externalBook: the one that must survive (has a resolvable r:id, valid cached data).
                new XElement(
                    WorkbookNs + "externalBook",
                    new XAttribute(RelNs + "id", bookRelId),
                    new XElement(
                        WorkbookNs + "sheetNames",
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Sheet1")),
                        // Blank cached sheet name -- must be dropped by NormalizeSheetNamesElement.
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "   "))),
                    new XElement(
                        WorkbookNs + "sheetDataSet",
                        new XElement(
                            WorkbookNs + "sheetData",
                            new XAttribute("sheetId", "0"),
                            new XElement(
                                WorkbookNs + "row",
                                new XAttribute("r", "1"),
                                new XElement(
                                    WorkbookNs + "cell",
                                    new XAttribute("r", "A1"),
                                    new XElement(WorkbookNs + "v", cachedValue.ToString(System.Globalization.CultureInfo.InvariantCulture))))))),
                // Second, malformed duplicate payload element -- must be removed entirely (only one
                // payload element is schema-valid per externalLink).
                new XElement(
                    WorkbookNs + "externalBook",
                    new XAttribute(RelNs + "id", "rIdShouldBeRemoved")),
                // Unrecognized stray child -- must be removed.
                new XElement(WorkbookNs + "unknownJunkElement", "junk"))));

        WriteExternalLinkPartRels(archive, partPath, bookRelId, targetFileName);
    }

    private static void WriteExternalLinkPartRels(ZipArchive archive, string partPath, string bookRelId, string targetFileName)
    {
        var relsPath = partPath.Replace("externalLinks/", "externalLinks/_rels/", System.StringComparison.Ordinal) + ".rels";
        ReplaceXml(archive, relsPath, new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", bookRelId),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("Target", targetFileName),
                    new XAttribute("TargetMode", "External")))));
    }

    private static void AddExternalFormulaCell(MemoryStream stream, int bracketIndex, int cachedValue)
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
                new XElement(WorkbookNs + "f", $"'[{bracketIndex}]Sheet1'!A1"),
                new XElement(WorkbookNs + "v", cachedValue.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        ReplaceXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void InsertExternalReferencesInOrder(XElement workbookRoot, XElement externalReferences)
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
                element.Name.Namespace == WorkbookNs &&
                laterWorkbookElements.Contains(element.Name.LocalName, System.StringComparer.Ordinal));
        if (insertionPoint is null)
            workbookRoot.Add(externalReferences);
        else
            insertionPoint.AddBeforeSelf(externalReferences);
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var overrideElement = contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("PartName")?.Value, partName, System.StringComparison.OrdinalIgnoreCase));
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
