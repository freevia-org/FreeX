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
/// R95-io-external-link-sheetname-whitespace: <c>XlsxExternalLinkSchemaNormalizer</c> used to route
/// <c>externalBook/sheetNames/sheetName/@val</c> through the generic trim-everything attribute
/// helper on EVERY save (both the full-regenerate and incremental patch-save paths). Excel permits
/// (and itself writes) leading/trailing spaces in a cached external sheet name, and the SAME
/// untrimmed name is separately embedded, quoted, in any worksheet formula's sheet qualifier (e.g.
/// <c>'[1]Sheet 1 '!A1</c>) -- a completely different package part the normalizer never touches. A
/// save that trimmed the cached copy but left the formula's quoted copy untouched desynced the two
/// representations, so <see cref="ExternalLinkModel.TryFindSheetIndex"/>'s exact
/// (whitespace-sensitive) string match failed on the next load and a previously-resolving external
/// reference silently became #REF!.
/// <para>
/// Fixed by preserving <c>sheetName/@val</c> verbatim (only removing it when null/blank) instead of
/// trimming it, so it stays byte-for-byte consistent with the untouched formula qualifier text.
/// </para>
/// </summary>
public sealed class R95_ExternalLinkSheetNameWhitespacePreservationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    // Excel permits (and writes) leading/trailing spaces in a cached external sheet name.
    private const string ExternalSheetNameWithSpaces = "Data ";

    [Fact]
    public void PatchSave_PreservesExternalLinkSheetNameWhitespace_SoExternalFormulaStillResolvesOnReload()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        // Touch an unrelated cell so the save goes through the real edited-workbook save flow
        // (matching how a user's own edit would trigger this normalizer as an unrelated side effect).
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        // The cached sheetName/@val must survive the save byte-for-byte, including its trailing space.
        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var externalLinkXml = LoadXml(archive, "xl/externalLinks/externalLink1.xml");
            var sheetNameVal = externalLinkXml.Root!
                .Element(WorkbookNs + "externalBook")!
                .Element(WorkbookNs + "sheetNames")!
                .Element(WorkbookNs + "sheetName")!
                .Attribute("val")!
                .Value;
            sheetNameVal.Should().Be(ExternalSheetNameWithSpaces);
        }

        // And the reloaded, recalculated formula must still resolve against the cached external
        // value instead of falling through to #REF!.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(20, 1).Should().Be(
            new NumberValue(123),
            "the cached sheetNames/@val must stay consistent with the untouched quoted sheet " +
            "qualifier embedded in the worksheet formula, or the external reference falls through " +
            "to #REF! purely as a side effect of an unrelated save");
    }

    /// <summary>
    /// Sibling no-regression: a genuinely blank/whitespace-only cached sheet name (never valid Excel
    /// content -- Excel forbids a blank sheet name outright) must still be dropped by the
    /// normalizer, exactly as before this fix. Only the "has real, possibly space-padded content"
    /// case changed from trim-in-place to preserve-verbatim.
    /// </summary>
    [Fact]
    public void PatchSave_StillDropsWhitespaceOnlyCachedSheetName()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        AddSecondBlankSheetNameEntry(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var externalLinkXml = LoadXml(archive, "xl/externalLinks/externalLink1.xml");
        var sheetNameElements = externalLinkXml.Root!
            .Element(WorkbookNs + "externalBook")!
            .Element(WorkbookNs + "sheetNames")!
            .Elements(WorkbookNs + "sheetName")
            .ToList();

        sheetNameElements.Should().ContainSingle();
        sheetNameElements[0].Attribute("val")!.Value.Should().Be(ExternalSheetNameWithSpaces);
    }

    // ── fixture construction ────────────────────────────────────────────────────────────────

    private static MemoryStream CreateSourcePackageWithExternalFormula()
    {
        var workbook = new Workbook("ExternalLinkSheetNameWhitespace");
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

        AddContentTypeOverride(
            archive,
            "/xl/externalLinks/externalLink1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");

        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Elements(WorkbookNs + "externalReferences").Remove();
        InsertExternalReferencesInOrder(workbookXml.Root, new XElement(
            WorkbookNs + "externalReferences",
            new XElement(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", "rIdFreeXExternalLink"))));
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);

        var workbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelationshipsXml = LoadXml(archive, workbookRelationshipsPath);
        workbookRelationshipsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
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
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", ExternalSheetNameWithSpaces))),
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
                                    new XElement(WorkbookNs + "v", "123")))))))));

        ReplaceXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("Target", "linked-workbook.xlsx"),
                    new XAttribute("TargetMode", "External")))));
    }

    /// <summary>
    /// Injects a worksheet formula cell whose quoted sheet qualifier text contains the SAME
    /// (untrimmed) sheet name as the externalBook/sheetNames/sheetName/@val entry above -- exactly
    /// the on-disk shape Excel itself produces, and the shape this normalizer must never desync.
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
                new XElement(WorkbookNs + "f", $"'[1]{ExternalSheetNameWithSpaces}'!A1"),
                new XElement(WorkbookNs + "v", "123"))));
        ReplaceXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AddSecondBlankSheetNameEntry(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        var externalLinkXml = LoadXml(archive, "xl/externalLinks/externalLink1.xml");
        externalLinkXml.Root!
            .Element(WorkbookNs + "externalBook")!
            .Element(WorkbookNs + "sheetNames")!
            .Add(new XElement(WorkbookNs + "sheetName", new XAttribute("val", "   ")));
        ReplaceXml(archive, "xl/externalLinks/externalLink1.xml", externalLinkXml);
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
