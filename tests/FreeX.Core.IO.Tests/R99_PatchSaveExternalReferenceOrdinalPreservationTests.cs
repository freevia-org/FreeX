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
/// R99-io-external-references-patch-normalizer-1: <c>XlsxWorkbookExternalReferencesNormalizer</c>,
/// wired into FreeX's everyday cell-PATCH save path via
/// <c>XlsxFileAdapter.SourcePackageSnapshot.NormalizePatchWorkbookExternalReferences</c> (called
/// unconditionally, regardless of whether the edit touches external links), used to REMOVE any
/// &lt;externalReference&gt; whose r:id was blank, missing, or duplicated. Because Excel's '[n]'
/// bracket-index formula syntax addresses external references by their fixed 1-based position in
/// workbook.xml's &lt;externalReference&gt; list, dropping an earlier slot shifted every later
/// externalReference down by one -- so a formula like '[3]Sheet1'!A1 that correctly addressed the
/// source's third external workbook silently addressed a different one after an ordinary cell-patch
/// save. This is the patch-save-path sibling of
/// <see cref="R96_ExternalLinkReferencePreserverOrdinalTests"/> (which covers the SAME bug class on
/// the full ClosedXML-rebuild save path) and
/// <see cref="R96_ExternalLinkOrdinalPositionAfterUnresolvedReferenceTests"/> (the read path) --
/// those tests explicitly force <c>workbook.IsStructureProtected = true</c> to exercise the
/// full-rewrite path, so none of them exercised the cell-patch fast path this class covers.
/// <para>
/// Fixed by reserving the slot: mint a fresh, guaranteed-unused r:id and leave the element
/// deliberately unbacked by any Relationship, instead of removing it.
/// </para>
/// </summary>
public sealed class R99_PatchSaveExternalReferenceOrdinalPreservationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// Middle entry unresolvable: workbook.xml order on disk is [1] BookA / [2] broken / [3] BookC.
    /// A formula addresses '[3]' (BookC). An ORDINARY cell edit -- no structure protection flip --
    /// must qualify for the cheap cell-PATCH save path and still keep the saved package's ordinal 3
    /// pointing at BookC, not silently renumber it down to slot 2.
    /// </summary>
    [Fact]
    public void PatchSave_ReservesOrdinalSlotForUnresolvedMiddleReference_SoLaterReferenceKeepsItsBracketIndex()
    {
        using var source = CreateWorkbookWithBrokenMiddleExternalReference();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // A plain cell edit alone -- no IsStructureProtected flip -- so this must go through FreeX's
        // cheap cell-PATCH save path, which copies xl/workbook.xml through the normalizer under test.
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

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

            externalReferences.Should().HaveCount(3, "the broken middle externalReference must reserve an empty placeholder slot instead of being dropped by the patch-save path");

            var slot3RelId = externalReferences[2].Attribute(RelNs + "id")!.Value;
            var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var slot3Relationship = workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .SingleOrDefault(element => element.Attribute("Id")?.Value == slot3RelId);
            slot3Relationship.Should().NotBeNull("slot 3 must still resolve to a real relationship after the patch-save");
            slot3Relationship!.Attribute("Type")!.Value.Should().Be(ExternalLinkRelationshipType);

            var partPath = "xl/" + slot3Relationship.Attribute("Target")!.Value.TrimStart('/');
            var externalLinkXml = LoadXml(archive, partPath);
            var sheetDataValue = externalLinkXml.Root!
                .Element(WorkbookNs + "externalBook")!
                .Element(WorkbookNs + "sheetDataSet")!
                .Element(WorkbookNs + "sheetData")!
                .Element(WorkbookNs + "row")!
                .Element(WorkbookNs + "cell")!
                .Element(WorkbookNs + "v")!
                .Value;
            sheetDataValue.Should().Be("222", "slot 3 must still be BookC (cached 222), not the value shifted up from BookA/whatever would have become the new slot 2");

            // Slot 2 (the placeholder) must NOT resolve to any real relationship/part, and its minted
            // r:id must not collide with any real relationship already present in the rels part.
            var slot2RelId = externalReferences[1].Attribute(RelNs + "id")!.Value;
            workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Any(element => element.Attribute("Id")?.Value == slot2RelId)
                .Should().BeFalse("the unresolved placeholder must stay unbacked, exactly like the dangling reference already in the source");
        }

        // Real product consumer: reload the SAVED package and recalc through FormulaEvaluator, proving
        // '[3]Sheet1'!A1 still resolves to BookC (222) end-to-end after the round trip.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);
        reloadedSheet.GetValue(20, 1).Should().Be(new NumberValue(222));
    }

    /// <summary>
    /// No-regression sibling: when every externalReference resolves cleanly, an ordinary cell-patch
    /// save must keep exactly the same slot count/order -- the placeholder logic must never fire for
    /// entries that resolved fine, and the patch-save path must still be taken (proving the fixture
    /// itself doesn't accidentally force the full-rewrite path).
    /// </summary>
    [Fact]
    public void PatchSave_DoesNotInsertPlaceholdersWhenAllExternalReferencesResolve()
    {
        using var source = CreateWorkbookWithAllExternalReferencesResolving();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!
            .Element(WorkbookNs + "externalReferences")!
            .Elements(WorkbookNs + "externalReference")
            .Should().HaveCount(2);

        var issues = XlsxPackageHealthValidator.Validate(archive);
        issues.Where(issue => issue.Contains("external", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }

    // ── fixture construction (mirrors R96_ExternalLinkReferencePreserverOrdinalTests) ──

    private static MemoryStream CreateWorkbookWithBrokenMiddleExternalReference()
    {
        var workbook = new Workbook("ExternalLinkPatchOrdinal");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        // 3 total ordinal slots: [1] A (resolved) / [2] broken / [3] B (resolved).
        AddExternalLinkPackage(
            stream,
            brokenIndices: [1],
            externalLinkPaths:
            [
                "xl/externalLinks/externalLinkA.xml",
                "xl/externalLinks/externalLinkB.xml"
            ]);
        // 0-based slot 2 (B) is Excel's 1-based '[3]'.
        AddExternalFormulaCell(stream, bracketIndex: 3, cachedValue: 222);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateWorkbookWithAllExternalReferencesResolving()
    {
        var workbook = new Workbook("ExternalLinkPatchOrdinalControl");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(
            stream,
            brokenIndices: [],
            externalLinkPaths: ["xl/externalLinks/externalLinkB.xml", "xl/externalLinks/externalLinkC.xml"]);
        stream.Position = 0;
        return stream;
    }

    /// <param name="brokenIndices">
    /// 0-based positions within the final &lt;externalReferences&gt; list that get a dangling r:id
    /// with no matching Relationship element. The remaining positions are filled, in order, from
    /// <paramref name="externalLinkPaths"/>.
    /// </param>
    private static void AddExternalLinkPackage(MemoryStream stream, int[] brokenIndices, string[] externalLinkPaths)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var path in externalLinkPaths)
        {
            AddContentTypeOverride(archive, "/" + path, ExternalLinkContentType);
        }

        var relationshipTargets = externalLinkPaths
            .Select(path => path.StartsWith("xl/", System.StringComparison.Ordinal) ? path["xl/".Length..] : path)
            .ToArray();

        var totalSlots = externalLinkPaths.Length + brokenIndices.Length;
        var externalReferences = new XElement(WorkbookNs + "externalReferences");
        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);

        var resolvedIndex = 0;
        var brokenSet = brokenIndices.ToHashSet();
        var cachedValues = new[] { 111, 222, 333, 444, 555 };
        for (var slot = 0; slot < totalSlots; slot++)
        {
            if (brokenSet.Contains(slot))
            {
                // Blank r:id (present but empty) -- NOT merely a dangling/unresolved-but-nonblank id
                // like "rIdMissing1" -- is the exact condition XlsxWorkbookExternalReferencesNormalizer
                // targets: NormalizeRelationshipId trims an empty value and removes the attribute,
                // leaving relationshipId null, which is what triggered the buggy unconditional removal.
                externalReferences.Add(new XElement(
                    WorkbookNs + "externalReference",
                    new XAttribute(RelNs + "id", string.Empty)));
                continue;
            }

            var relId = $"rIdResolved{resolvedIndex}";
            externalReferences.Add(new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", relId)));
            workbookRelationshipsXml.Root!.Add(new XElement(
                PackageRelNs + "Relationship",
                new XAttribute("Id", relId),
                new XAttribute("Type", ExternalLinkRelationshipType),
                new XAttribute("Target", relationshipTargets[resolvedIndex])));
            AddExternalLinkPart(archive, externalLinkPaths[resolvedIndex], $"rIdBook{resolvedIndex}", $"Book{resolvedIndex}.xlsx", cachedValues[resolvedIndex]);
            resolvedIndex++;
        }

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
        InsertExternalReferencesInOrder(workbookXml.Root, externalReferences);
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);
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
