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
/// R96-io-external-link-ordinal-after-unresolved-skip: <c>XlsxExternalLinkMetadataReader</c> used to
/// silently <c>continue</c> (drop) any <c>&lt;externalReference&gt;</c> whose <c>r:id</c> did not
/// resolve via <c>workbook.xml.rels</c> instead of reserving its slot. Because the '[n]' bracket-index
/// formula syntax (<see cref="FreeX.Core.Formula.ExternalSheetReferenceResolver"/>'s
/// <c>TryFindExternalLink</c>) addresses <c>Workbook.ExternalLinks</c> purely by its FILTERED list
/// position, any skipped/dropped entry shifted every later external link down by one slot relative to
/// the ordinal Excel itself encoded into the formula's '[n]' reference -- so <c>'[2]Sheet1'!A1</c>,
/// meant to address the workbook's SECOND externalReference, would silently resolve against whatever
/// became <c>ExternalLinks[1]</c> after the skip (the THIRD externalReference) instead of failing
/// loudly or resolving to the correct external workbook.
/// <para>
/// Fixed by having the reader append an empty placeholder <see cref="ExternalLinkModel"/> for any
/// externalReference whose relationship can't be resolved, so later entries keep their original
/// 1-based ordinal position.
/// </para>
/// </summary>
public sealed class R96_ExternalLinkOrdinalPositionAfterUnresolvedReferenceTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void Load_ReservesOrdinalSlotsSoLaterExternalReferencesKeepTheirBracketIndex()
    {
        // workbook.xml order: [1] broken (dangling r:id, no matching relationship) / [2] BookB / [3] BookC.
        using var package = CreateWorkbookWithBrokenFirstExternalReference();

        var links = XlsxExternalLinkMetadataReader.Load(package);

        links.Should().HaveCount(3, "the broken first externalReference must reserve an empty placeholder slot");
        links[0].SheetNames.Should().BeEmpty("slot 1 is the unresolved placeholder");
        links[1].SheetNames.Should().ContainSingle().Which.Should().Be("Sheet1");
        links[1].CachedSheetData.Single().Values[(1u, 1u)].Should().Be(new NumberValue(111), "slot 2 must be BookB, not BookC");
        links[2].CachedSheetData.Single().Values[(1u, 1u)].Should().Be(new NumberValue(222), "slot 3 must be BookC");
    }

    [Fact]
    public void FormulaEvaluation_BracketTwoResolvesToSecondExternalReferenceNotThird_ThroughRealLoadAndRecalc()
    {
        // Real product entry point: load the package via XlsxFileAdapter (which drives
        // XlsxExternalLinkMetadataReader internally) and recalculate through FormulaEvaluator/
        // RecalcEngine, exactly as opening the file and pressing F9 would.
        using var package = CreateWorkbookWithBrokenFirstExternalReference();

        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheetAt(0);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        // '[2]Sheet1'!A1 must read BookB's cached 111, not BookC's cached 222 -- proof the fix
        // reaches the actual formula-evaluation consumer, not just the reader's own model list.
        sheet.GetValue(20, 1).Should().Be(new NumberValue(111));
    }

    /// <summary>
    /// Sibling no-regression: when every externalReference resolves cleanly (nothing to skip), the
    /// list must stay exactly the same length/order as before -- the placeholder logic must never
    /// insert extra slots for entries that resolved fine.
    /// </summary>
    [Fact]
    public void Load_DoesNotInsertPlaceholdersWhenAllExternalReferencesResolve()
    {
        using var package = CreateWorkbookWithAllExternalReferencesResolving();

        var links = XlsxExternalLinkMetadataReader.Load(package);

        links.Should().HaveCount(2);
        links[0].CachedSheetData.Single().Values[(1u, 1u)].Should().Be(new NumberValue(111));
        links[1].CachedSheetData.Single().Values[(1u, 1u)].Should().Be(new NumberValue(222));
    }

    // ── fixture construction ────────────────────────────────────────────────────────────────

    private static MemoryStream CreateWorkbookWithBrokenFirstExternalReference()
    {
        var workbook = new Workbook("ExternalLinkOrdinal");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(
            stream,
            includeBrokenFirstReference: true,
            externalLinkPaths: ["xl/externalLinks/externalLinkB.xml", "xl/externalLinks/externalLinkC.xml"]);
        AddExternalFormulaCell(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateWorkbookWithAllExternalReferencesResolving()
    {
        var workbook = new Workbook("ExternalLinkOrdinalControl");
        var sheet = workbook.AddSheet("Local");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));

        var stream = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddExternalLinkPackage(
            stream,
            includeBrokenFirstReference: false,
            externalLinkPaths: ["xl/externalLinks/externalLinkB.xml", "xl/externalLinks/externalLinkC.xml"]);
        stream.Position = 0;
        return stream;
    }

    private static void AddExternalLinkPackage(MemoryStream stream, bool includeBrokenFirstReference, string[] externalLinkPaths)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var path in externalLinkPaths)
        {
            AddContentTypeOverride(
                archive,
                "/" + path,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");
        }

        // Relationship Target attributes are relative to "xl/" (workbook.xml's own directory), so
        // strip the "xl/" prefix the archive-entry paths (and content-type overrides) need.
        var relationshipTargets = externalLinkPaths
            .Select(path => path.StartsWith("xl/", System.StringComparison.Ordinal) ? path["xl/".Length..] : path)
            .ToArray();

        var externalReferences = new XElement(WorkbookNs + "externalReferences");
        if (includeBrokenFirstReference)
        {
            // Ordinal 1: dangling r:id with no matching Relationship element in workbook.xml.rels.
            externalReferences.Add(new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdMissing")));
        }

        externalReferences.Add(new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdB")));
        externalReferences.Add(new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdC")));

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
        InsertExternalReferencesInOrder(workbookXml.Root, externalReferences);
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdB"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", relationshipTargets[0])));
        workbookRelationshipsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdC"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", relationshipTargets[1])));
        ReplaceXml(archive, workbookRelationshipsPath, workbookRelationshipsXml);

        AddExternalLinkPart(archive, externalLinkPaths[0], "rIdBookB", "BookB.xlsx", cachedValue: 111);
        AddExternalLinkPart(archive, externalLinkPaths[1], "rIdBookC", "BookC.xlsx", cachedValue: 222);
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

    /// <summary>
    /// Injects a worksheet formula addressing the SECOND externalReference by its on-disk ordinal
    /// ('[2]', BookB) -- the exact shape that silently mis-resolved to BookC before this fix.
    /// </summary>
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
                new XElement(WorkbookNs + "f", "'[2]Sheet1'!A1"),
                new XElement(WorkbookNs + "v", "111"))));
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
