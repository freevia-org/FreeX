using System;
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
/// R102-io-external-link-authoring-mint-collision: when a source workbook being re-saved already
/// carries a dangling/unresolvable &lt;externalReference&gt; -- the exact case
/// <see cref="XlsxExternalLinkReferencePreserver"/> reserves an UNBACKED placeholder r:id for (no
/// Relationship element added, by design, to mirror the source's own broken reference; see
/// <see cref="R101_ExternalLinkReferencePreserverDuplicateRelIdTests"/>) -- AND the same save also
/// introduces a freshly TYPED bracketed external-workbook formula (e.g.
/// <c>='[Budget.xlsx]Sheet1'!A1</c>), <see cref="XlsxExternalLinkAuthoringWriter"/> independently
/// minted its own relationship id by scanning ONLY the Relationship elements already present in
/// xl/_rels/workbook.xml.rels (via <c>OpcRelationships.NextRelationshipId</c>) -- which is exactly
/// the set the preserver's placeholder was deliberately left OUT of. Since nothing else touches
/// workbook.xml.rels in between, that scan deterministically re-minted the SAME "next" id the
/// placeholder had just claimed, producing two sibling &lt;externalReference&gt; elements sharing one
/// r:id. The end-of-save generic container-schema normalizer (<c>XlsxWorkbookContainerElementSchemas</c>
/// externalReferences schema -&gt; <c>XlsxWorkbookContainerElementSchema</c>'s dedup-by-r:id loop) then
/// silently deleted the second occurrence outright -- collapsing three ordinal '[n]' slots down to
/// two and leaving the surviving (first) slot's r:id backed by a REAL Relationship it was never
/// supposed to have, cross-contaminating what should have stayed a dangling reference with the
/// freshly authored book's data.
/// <para>
/// Fixed by threading a reserved-id pool -- every r:id already used by an existing
/// &lt;externalReference&gt; element in workbook.xml, backed or not -- into
/// <c>OpcRelationships.NextRelationshipId</c>/<c>EnsureRelationshipForPackagePart</c> so a freshly
/// minted id can never collide with one of them.
/// </para>
/// </summary>
public sealed class R102_ExternalLinkAuthoringMintCollisionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// The primary proof: source has one resolvable external reference (slot 1, Book0.xlsx) and one
    /// dangling/unresolvable reference (slot 2). The same save types a brand-new bracketed reference
    /// to a book the source never had at all ('[Budget.xlsx]Data'!A1). All three ordinal slots must
    /// survive with three DISTINCT r:id values -- none silently collapsed by a relationship-id
    /// collision between the preserver's placeholder and the authoring writer's fresh mint.
    /// </summary>
    [Fact]
    public void Save_FreshExternalFormulaAndDanglingSourceReference_PreservesAllThreeOrdinalSlots()
    {
        using var source = CreateSourceWithRealAndDanglingExternalReference();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // Freshly TYPE a bracketed reference to a workbook the source package never carried at all --
        // the exact "typed directly into a cell" shape XlsxExternalLinkAuthoringWriter synthesizes
        // backing infrastructure for.
        sheet.SetFormula(new CellAddress(sheet.Id, 25, 1), "'[Budget.xlsx]Data'!A1");
        // A plain cell edit alone qualifies for the cheap cell-PATCH save path, which never calls
        // XlsxExternalLinkReferencePreserver at all. Flip structure-protection to force the full
        // ClosedXML-rebuild save path -- the only path that actually runs the preserver -- exactly
        // like the R101 sibling tests do.
        workbook.IsStructureProtected = true;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var externalReferences = workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .ToList();

        externalReferences.Should().HaveCount(
            3,
            "the pre-existing real slot, the dangling placeholder slot, and the freshly authored " +
            "Budget.xlsx slot must all survive as distinct ordinal entries -- a relationship-id " +
            "collision between the preserver's placeholder and the authoring writer's fresh mint must " +
            "not let the end-of-save normalizer silently delete one");

        var relIds = externalReferences
            .Select(element => element.Attribute(RelNs + "id")!.Value)
            .ToList();
        relIds.Should().OnlyHaveUniqueItems(
            "two <externalReference> elements must never share an r:id -- Excel addresses them by " +
            "fixed ordinal position, and a collision is exactly what lets the schema normalizer " +
            "silently collapse the list");

        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var backedRelationships = workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => element,
                StringComparer.OrdinalIgnoreCase);

        // Exactly one of the three ordinal slots -- the dangling one carried over from the source --
        // must remain unbacked by any Relationship, exactly mirroring the source's own broken
        // reference. The other two (the pre-existing real link and the freshly authored one) must
        // each resolve to their OWN distinct externalLink part.
        var unbackedCount = relIds.Count(id => !backedRelationships.ContainsKey(id));
        unbackedCount.Should().Be(1, "only the carried-forward dangling reference should stay unbacked");

        var backedTargets = relIds
            .Where(id => backedRelationships.ContainsKey(id))
            .Select(id => backedRelationships[id].Attribute("Target")!.Value)
            .ToList();
        backedTargets.Should().OnlyHaveUniqueItems(
            "the pre-existing real link and the freshly authored Budget.xlsx link must resolve to two " +
            "DIFFERENT externalLink parts, never the same one (which would mean one was silently " +
            "dropped and the other's relationship id reused for the dangling slot)");

        // Real product consumer: reload the SAVED package and recalc through FormulaEvaluator, proving
        // the pre-existing reference (ordinal slot 1) still resolves to its own correct target after
        // the round trip and was not cross-contaminated by the collision.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);
        reloadedSheet.GetValue(20, 1).Should().Be(new NumberValue(111));
    }

    /// <summary>
    /// No-regression sibling: the SAME dangling-source-reference shape, but with NO freshly typed
    /// external formula added in this save. The preserver's own placeholder-reservation behavior
    /// (R96/R101) must be completely unaffected by threading the new reserved-id pool through
    /// <c>EnsureRelationshipForPackagePart</c> -- the real slot must still resolve correctly and the
    /// dangling slot must still be reserved (not collapsed), exactly as before this fix.
    /// </summary>
    [Fact]
    public void Save_DanglingSourceReferenceAlone_NoFreshFormula_StillPreservesBothOrdinalSlots()
    {
        using var source = CreateSourceWithRealAndDanglingExternalReference();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));
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

            externalReferences.Should().HaveCount(2, "no fresh formula was authored in this save, so only the two source ordinal slots should exist");

            var relIds = externalReferences.Select(e => e.Attribute(RelNs + "id")!.Value).ToList();
            relIds.Should().OnlyHaveUniqueItems();

            var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var backedIds = workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Select(e => e.Attribute("Id")?.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            backedIds.Should().Contain(relIds[0], "the pre-existing real reference must stay backed");
            backedIds.Should().NotContain(relIds[1], "the dangling reference must stay unbacked");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);
        reloadedSheet.GetValue(20, 1).Should().Be(new NumberValue(111));
    }

    /// <summary>
    /// Source workbook.xml carries two &lt;externalReference&gt; elements: slot 1 resolves to a real
    /// externalLink part (Book0.xlsx, cached A1 == 111), slot 2's r:id ("rIdMissing0") resolves to
    /// nothing in xl/_rels/workbook.xml.rels at all (a genuinely dangling/broken reference). A formula
    /// at row 20 addresses slot 1 by its ordinal position ('[1]Data'!A1) so the reload+recalc
    /// assertion proves it still resolves correctly after the save.
    /// </summary>
    private static MemoryStream CreateSourceWithRealAndDanglingExternalReference()
    {
        var workbook = new Workbook("ExternalLinkAuthoringMintCollision");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            const string partPath = "xl/externalLinks/externalLink1.xml";
            AddContentTypeOverride(archive, "/" + partPath, ExternalLinkContentType);

            var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);
            workbookRelationshipsXml.Root!.Add(new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdBook0"),
                new XAttribute("Type", ExternalLinkRelationshipType),
                new XAttribute("Target", "externalLinks/externalLink1.xml")));
            ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

            AddExternalLinkPart(archive, partPath, "rIdPath0", "Book0.xlsx", 111);

            var externalReferences = new XElement(
                WorkbookNs + "externalReferences",
                new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdBook0")),
                new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdMissing0")));

            var workbookXml = LoadXml(archive, "xl/workbook.xml");
            workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
            InsertExternalReferencesInOrder(workbookXml.Root!, externalReferences);
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        AddExternalFormulaCell(stream, bracketIndex: 1, cachedValue: 111);
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
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Ledger"))),
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

        var relsPath = partPath.Replace("externalLinks/", "externalLinks/_rels/", StringComparison.Ordinal) + ".rels";
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
                new XElement(WorkbookNs + "f", $"'[{bracketIndex}]Ledger'!A1"),
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
                laterWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
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
                string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
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
