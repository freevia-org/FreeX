using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R104-io-external-link-formula-ordinal-1: <see cref="XlsxExternalLinkAuthoringWriter"/> correctly
/// synthesizes the externalLink backing infrastructure for a freshly typed external-workbook formula
/// (<see cref="R96_ExternalLinkAuthoringWriterTests"/>), but it left the ORIGIN cell's own persisted
/// <c>&lt;f&gt;</c> text untouched -- still the literal quoted-filename form
/// (<c>'[Budget.xlsx]Data'!A1</c>) ClosedXML wrote verbatim, rather than the 1-based numeric-ordinal
/// form (<c>'[1]Data'!A1</c>) real Excel actually persists on disk (translating to/from the friendly
/// filename form only for formula-bar display). Left unrewritten, the saved package is internally
/// self-contradictory: workbook.xml declares external reference #1 for Budget.xlsx, but the formula
/// that supposedly drove that synthesis still spells the filename out literally -- a shape Excel's own
/// save path never produces. Fixed in <c>XlsxExternalLinkAuthoringWriter.Save</c> by rewriting every
/// matching worksheet <c>&lt;f&gt;</c> in place once the final ordinal for each referenced book is
/// known.
/// </summary>
public sealed class R104_ExternalLinkFormulaOrdinalRewriteTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// THE PRIMARY PROOF (fails before the fix, passes after): a brand-new workbook whose only
    /// external-workbook content is a freshly typed formula must, on save, carry that formula's
    /// persisted <c>&lt;f&gt;</c> text in the numeric-ordinal form real Excel writes -- NOT the literal
    /// filename form the formula was typed with. Before the fix the saved <c>&lt;f&gt;</c> was still
    /// <c>'[Budget.xlsx]Data'!A1</c> verbatim (matching the finding's own reproduction).
    /// </summary>
    [Fact]
    public void FreshWorkbook_TypedExternalWorkbookFormula_RewritesFormulaToNumericOrdinalForm()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "'[Budget.xlsx]Data'!A1");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        var formulaText = GetCellFormula(archive, "xl/worksheets/sheet1.xml", "A1");

        formulaText.Should().Be(
            "'[1]Data'!A1",
            "real Excel never persists the filename inside a formula's <f> text -- only the 1-based " +
            "externalReference ordinal that workbook.xml's <externalReference>/<externalReferences> " +
            "entry (synthesized alongside it) actually occupies");

        // Sanity: the ordinal really does resolve, via the same rels chain a reader would follow, to
        // the book this formula was originally typed against.
        var bookByOrdinal = ResolveBookNamesByOrdinal(archive);
        bookByOrdinal[1].Should().Be("Budget.xlsx");
    }

    /// <summary>
    /// No-regression sibling covering a second, distinct call path this same fix must get right: TWO
    /// separate freshly typed formulas against TWO DIFFERENT external books must each be rewritten to
    /// their OWN correct ordinal (1 and 2, matching workbook.xml's <externalReference> insertion order)
    /// -- not both collapsed to the same ordinal, and not swapped.
    /// </summary>
    [Fact]
    public void FreshWorkbook_TwoDistinctExternalBooks_EachFormulaRewrittenToItsOwnOrdinal()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "'[Budget.xlsx]Data'!A1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "'[Actuals.xlsx]Data'!A1");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        var bookByOrdinal = ResolveBookNamesByOrdinal(archive);
        bookByOrdinal.Should().HaveCount(2);

        var formulaA1 = GetCellFormula(archive, "xl/worksheets/sheet1.xml", "A1");
        var formulaA2 = GetCellFormula(archive, "xl/worksheets/sheet1.xml", "A2");

        var ordinalA1 = ExtractOrdinal(formulaA1);
        var ordinalA2 = ExtractOrdinal(formulaA2);

        ordinalA1.Should().NotBe(ordinalA2, "each distinct external book must occupy its own ordinal slot");
        bookByOrdinal[ordinalA1].Should().Be("Budget.xlsx");
        bookByOrdinal[ordinalA2].Should().Be("Actuals.xlsx");

        formulaA1.Should().NotContain("Budget.xlsx");
        formulaA2.Should().NotContain("Actuals.xlsx");
    }

    /// <summary>
    /// No-regression sibling for the OTHER call site (the hasSourcePackage / <c>PreserveSourcePackageParts</c>
    /// path): a workbook loaded from a source package that ALREADY has a valid external link (ordinal 1,
    /// pointing at "Linked.xlsx") must, when a brand-new cell is typed with the filename form referencing
    /// that SAME already-linked book, have that new formula rewritten to the EXISTING ordinal ('[1]') --
    /// not left in filename form (the original bug) and not given a spuriously duplicated second
    /// externalLink part/ordinal. This is the edge case the "already backed" early-return used to skip
    /// entirely (no NEW book meant no rewrite ran at all).
    /// </summary>
    [Fact]
    public void SourceLoadedWorkbook_NewFormulaReferencingAlreadyBackedBook_IsRewrittenToExistingOrdinal()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        // A brand-new cell, typed fresh in this editing session, against the SAME book the source
        // package already links (as ordinal 1).
        sheet.SetFormula(new CellAddress(sheet.Id, 6, 6), "'[Linked.xlsx]Data'!B2");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // Idempotency preserved: still exactly one externalLink part/reference, not a duplicate.
        var externalLinkPartCount = archive.Entries.Count(entry =>
            entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase));
        externalLinkPartCount.Should().Be(1);

        var newCellFormula = GetCellFormula(archive, "xl/worksheets/sheet1.xml", "F6");
        newCellFormula.Should().Be(
            "'[1]Data'!B2",
            "the freshly typed formula references a book ALREADY backed by externalReference #1, so it " +
            "must be rewritten to that existing ordinal rather than left in filename form");

        // The pre-existing formula cell (already in numeric-ordinal form from the source package) must
        // be left completely untouched.
        var preExistingFormula = GetCellFormula(archive, "xl/worksheets/sheet1.xml", "A20");
        preExistingFormula.Should().Be("'[1]Data'!A1");
    }

    // ── helpers ──

    private static string GetCellFormula(ZipArchive archive, string worksheetPath, string cellReference)
    {
        var worksheetXml = LoadXml(archive, worksheetPath);
        var cell = worksheetXml.Root!
            .Descendants(WorkbookNs + "c")
            .Single(c => c.Attribute("r")?.Value == cellReference);
        return cell.Element(WorkbookNs + "f")!.Value;
    }

    private static int ExtractOrdinal(string formulaText)
    {
        var openBracket = formulaText.IndexOf('[');
        var closeBracket = formulaText.IndexOf(']');
        openBracket.Should().BeGreaterThanOrEqualTo(0);
        closeBracket.Should().BeGreaterThan(openBracket);
        return int.Parse(formulaText[(openBracket + 1)..closeBracket]);
    }

    /// <summary>
    /// Mirrors the resolution chain a real reader (XlsxExternalLinkMetadataReader) follows: walks
    /// workbook.xml's <externalReferences>/<externalReference> list in order (1-based position IS the
    /// ordinal), resolves each r:id through workbook.xml.rels to its externalLinkN.xml part, then
    /// through that part's own _rels to the externalLinkPath relationship's Target (the book name).
    /// </summary>
    private static Dictionary<int, string> ResolveBookNamesByOrdinal(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");

        var result = new Dictionary<int, string>();
        var ordinal = 0;
        foreach (var externalReference in workbookXml.Root!
                     .Element(WorkbookNs + "externalReferences")!
                     .Elements(WorkbookNs + "externalReference"))
        {
            ordinal++;
            var relId = externalReference.Attribute(RelNs + "id")!.Value;
            var relationship = workbookRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Single(r => r.Attribute("Id")?.Value == relId);
            var partPath = "xl/" + relationship.Attribute("Target")!.Value.TrimStart('/');
            var partRelsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
            var partRelsXml = LoadXml(archive, partRelsPath);
            var bookRelationship = partRelsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Single(r => r.Attribute("Type")?.Value == ExternalLinkPathRelationshipType);
            result[ordinal] = bookRelationship.Attribute("Target")!.Value;
        }

        return result;
    }

    // ── fixture construction (mirrors R96_ExternalLinkAuthoringWriterTests) ──

    private static MemoryStream CreateSourcePackageWithExternalFormula()
    {
        var workbook = new Workbook("ExternalLinkFormulaOrdinalSibling");
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
