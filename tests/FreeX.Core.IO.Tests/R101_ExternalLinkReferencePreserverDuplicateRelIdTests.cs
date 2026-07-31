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
/// R101-io-external-link-preserver-duplicate-rid: <c>XlsxExternalLinkReferencePreserver.Preserve</c>
/// reserves an ordinal slot for an UNRESOLVABLE &lt;externalReference&gt; (r96,
/// <see cref="R96_ExternalLinkReferencePreserverOrdinalTests"/>), but two SOURCE
/// &lt;externalReference&gt; elements carrying the IDENTICAL r:id still collapsed to a single
/// SAVED entry -- a post-hoc ".Any(...targetRelId)" dedup silently skipped re-adding an element for
/// an already-seen targetRelId, so the second occurrence of a repeated r:id vanished from the saved
/// workbook.xml entirely, shifting every later ordinal '[n]' slot down by one.
/// <para>
/// ECMA-376 requires r:id to be present on CT_ExternalReference (18.13 externalReference) but does
/// NOT require it to be unique across sibling elements or to resolve to a relationship --
/// <see cref="XlsxExternalLinkMetadataReader"/> already treats a repeated r:id on the READ side as
/// its own (blank-placeholder) ordinal slot rather than re-resolving it; the SAVE side must agree.
/// </para>
/// <para>
/// Fixed by tracking consumed sourceRelIds during the save-side walk and diverting a repeat into the
/// same placeholder-reservation branch already used for a blank/unresolvable r:id (so it reserves its
/// own unbacked ordinal slot instead of disappearing), and by removing the now-redundant (and
/// behavior-narrowing) post-hoc target-relId dedup so two DIFFERENT sourceRelIds that happen to
/// resolve to the same target part each still get their own &lt;externalReference&gt; element.
/// </para>
/// </summary>
public sealed class R101_ExternalLinkReferencePreserverDuplicateRelIdTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// FIRST pair duplicated: slots [0]=BookA [1]=BookA (identical r:id) [2]=BookB. A formula
    /// addresses '[3]' (BookB). Before the fix, the duplicate collapsed to one saved slot -- only 2
    /// externalReference elements survived -- so '[3]' pointed past the end of the (shrunk) list.
    /// </summary>
    [Fact]
    public void Save_PreservesOrdinalSlot_WhenFirstPairSharesIdenticalRelId()
    {
        RunDuplicateScenario(
            slots: [(false, 0), (false, 0), (false, 1)],
            expectedSlotCount: 3,
            laterBracketIndex: 3,
            laterExpectedValue: 222,
            duplicatePlaceholderSlot: 1);
    }

    /// <summary>
    /// MIDDLE pair duplicated: slots [0]=BookA [1]=BookB [2]=BookB (identical r:id) [3]=BookC. A
    /// formula addresses '[4]' (BookC), the ordinal after the duplicate pair.
    /// </summary>
    [Fact]
    public void Save_PreservesOrdinalSlot_WhenMiddlePairSharesIdenticalRelId()
    {
        RunDuplicateScenario(
            slots: [(false, 0), (false, 1), (false, 1), (false, 2)],
            expectedSlotCount: 4,
            laterBracketIndex: 4,
            laterExpectedValue: 333,
            duplicatePlaceholderSlot: 2);
    }

    /// <summary>
    /// LAST pair duplicated: slots [0]=BookA [1]=BookB [2]=BookB (identical r:id). No slot follows
    /// the duplicate, so this proves the earlier ordinal ('[1]' -> BookA) still resolves and the
    /// total slot count is preserved (3), not collapsed to 2.
    /// </summary>
    [Fact]
    public void Save_PreservesOrdinalSlot_WhenLastPairSharesIdenticalRelId()
    {
        RunDuplicateScenario(
            slots: [(false, 0), (false, 1), (false, 1)],
            expectedSlotCount: 3,
            laterBracketIndex: 1,
            laterExpectedValue: 111,
            duplicatePlaceholderSlot: 2);
    }

    /// <summary>
    /// A genuinely broken/unresolvable r:id (r96) and an identical-r:id duplicate pair (r101) in the
    /// SAME save: slots [0]=BookA [1]=broken [2]=BookA (identical r:id to slot 0) [3]=BookB. Both
    /// kinds of "doesn't cleanly resolve" must coexist without interfering with each other's ordinal
    /// reservation.
    /// </summary>
    [Fact]
    public void Save_PreservesOrdinalSlots_WhenBrokenReferenceAndDuplicateRelIdCoexist()
    {
        RunDuplicateScenario(
            slots: [(false, 0), (true, -1), (false, 0), (false, 1)],
            expectedSlotCount: 4,
            laterBracketIndex: 4,
            laterExpectedValue: 222,
            duplicatePlaceholderSlot: 2,
            brokenPlaceholderSlot: 1);
    }

    /// <summary>
    /// No-regression baseline: every source r:id distinct, every reference resolves cleanly. Slot
    /// count and later-reference resolution must be unaffected by the r101 fix.
    /// </summary>
    [Fact]
    public void Save_AllDistinctRelIds_NoRegressionInOrdinalOrResolution()
    {
        RunDuplicateScenario(
            slots: [(false, 0), (false, 1), (false, 2)],
            expectedSlotCount: 3,
            laterBracketIndex: 3,
            laterExpectedValue: 333,
            duplicatePlaceholderSlot: null);
    }

    private static void RunDuplicateScenario(
        (bool broken, int bookIndex)[] slots,
        int expectedSlotCount,
        int laterBracketIndex,
        int laterExpectedValue,
        int? duplicatePlaceholderSlot,
        int? brokenPlaceholderSlot = null)
    {
        using var source = CreateWorkbookWithSlots(slots, laterBracketIndex, laterExpectedValue);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));
        // A plain cell edit alone qualifies for the cheap cell-PATCH save path, which copies
        // xl/workbook.xml byte-for-byte and never calls XlsxExternalLinkReferencePreserver at all.
        // Flip structure-protection to force the full ClosedXML-rebuild save path -- the only path
        // that actually runs the Preserver -- exactly like the r96 sibling tests do.
        workbook.IsStructureProtected = true;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = LoadXml(archive, "xl/workbook.xml");
            var externalReferences = workbookXml.Root!
                .Element(WorkbookNs + "externalReferences")!
                .Elements(WorkbookNs + "externalReference")
                .ToList();

            externalReferences.Should().HaveCount(
                expectedSlotCount,
                "a duplicated/broken r:id must reserve its own ordinal slot instead of collapsing the list");

            var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var backedIds = workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Select(e => e.Attribute("Id")?.Value)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            if (duplicatePlaceholderSlot is int dupSlot)
            {
                var dupRelId = externalReferences[dupSlot].Attribute(RelNs + "id")!.Value;
                backedIds.Should().NotContain(
                    dupRelId,
                    "the second occurrence of a repeated r:id must stay unbacked, mirroring the read side's blank-placeholder treatment of a duplicate");
            }

            if (brokenPlaceholderSlot is int brokenSlot)
            {
                var brokenRelId = externalReferences[brokenSlot].Attribute(RelNs + "id")!.Value;
                backedIds.Should().NotContain(brokenRelId, "a genuinely unresolvable r:id must stay unbacked");
            }
        }

        // Real product consumer: reload the SAVED package and recalc through FormulaEvaluator, proving
        // the later reference still resolves to its correct target after the round trip.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);
        reloadedSheet.GetValue(20, 1).Should().Be(new NumberValue(laterExpectedValue));
    }

    /// <param name="slots">
    /// Ordered list of ordinal slots. <c>broken == true</c> means a dangling, guaranteed-unresolvable
    /// r:id (its own unique dangling id per slot). <c>broken == false</c> means a real reference whose
    /// r:id is derived from <c>bookIndex</c> -- two slots sharing the same bookIndex get the IDENTICAL
    /// r:id (the r101 duplicate case) and point at the same externalLink part/cached value.
    /// </param>
    private static MemoryStream CreateWorkbookWithSlots(
        (bool broken, int bookIndex)[] slots,
        int laterBracketIndex,
        int laterExpectedValue)
    {
        var workbook = new Workbook("ExternalLinkPreserverDuplicateRelId");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var cachedValues = new[] { 111, 222, 333, 444, 555 };
            var distinctBookIndices = slots.Where(s => !s.broken).Select(s => s.bookIndex).Distinct().OrderBy(i => i).ToArray();

            foreach (var bookIndex in distinctBookIndices)
            {
                var partPath = $"xl/externalLinks/externalLink{bookIndex}.xml";
                AddContentTypeOverride(archive, "/" + partPath, ExternalLinkContentType);
            }

            var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);

            foreach (var bookIndex in distinctBookIndices)
            {
                var relId = $"rIdBook{bookIndex}";
                var partPath = $"xl/externalLinks/externalLink{bookIndex}.xml";
                workbookRelationshipsXml.Root!.Add(new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", relId),
                    new XAttribute("Type", ExternalLinkRelationshipType),
                    new XAttribute("Target", $"externalLinks/externalLink{bookIndex}.xml")));
                AddExternalLinkPart(archive, partPath, $"rIdPath{bookIndex}", $"Book{bookIndex}.xlsx", cachedValues[bookIndex]);
            }

            ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

            var externalReferences = new XElement(WorkbookNs + "externalReferences");
            var brokenCounter = 0;
            foreach (var (broken, bookIndex) in slots)
            {
                if (broken)
                {
                    externalReferences.Add(new XElement(
                        WorkbookNs + "externalReference",
                        new XAttribute(RelNs + "id", $"rIdMissing{brokenCounter++}")));
                    continue;
                }

                externalReferences.Add(new XElement(
                    WorkbookNs + "externalReference",
                    new XAttribute(RelNs + "id", $"rIdBook{bookIndex}")));
            }

            var workbookXml = LoadXml(archive, "xl/workbook.xml");
            workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
            InsertExternalReferencesInOrder(workbookXml.Root!, externalReferences);
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        AddExternalFormulaCell(stream, bracketIndex: laterBracketIndex, cachedValue: laterExpectedValue);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPart(ZipArchive archive, string partPath, string bookRelId, string targetFileName, int cachedValue)
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
