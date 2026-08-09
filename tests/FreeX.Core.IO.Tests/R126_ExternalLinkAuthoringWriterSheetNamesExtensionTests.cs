using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R126-io-external-link-sheetnames-extend-1: a workbook loaded from a source package that already
/// has an external link to a book (e.g. "Budget.xlsx") whose cached <c>externalBook/sheetNames</c>
/// only lists the sheet(s) referenced at last refresh (e.g. "Q1") must, when the user types a NEW
/// formula against a DIFFERENT sheet of that SAME book (e.g. "Q2"), have that sheet appended to the
/// existing <c>externalLinkN.xml</c> part's <c>sheetNames</c> catalog on save -- not silently dropped.
/// Before this fix, <c>XlsxExternalLinkAuthoringWriter.Save</c> keyed "already backed" purely by book
/// name and skipped any book already backed by an existing part, so the new sheet had nowhere to go:
/// <c>ExternalLinkModel.TryFindSheetIndex</c> could never find it on the next load, and the reference
/// would never resolve, forever, surviving save/reload.
/// <para>
/// Sheet names here deliberately avoid the "Sheet&lt;n&gt;"/default-name shape ("Q1"/"Q2" instead of
/// "Sheet1"/"Sheet2") -- ClosedXML's own A1 formula parser (<c>XLCell.FormulaA1</c>, the same setter
/// <c>XlsxFileAdapter.Save.cs</c> uses for every formula cell) cannot tokenize a bracketed external
/// reference whose sheet-name segment matches that shape at all (fails with
/// "Unable to determine token..." even against a brand-new workbook with zero other external-link
/// infrastructure involved) -- a separate, pre-existing ClosedXML limitation confirmed unrelated to
/// this writer, and out of scope for a Core.IO-only fix.
/// </para>
/// </summary>
public sealed class R126_ExternalLinkAuthoringWriterSheetNamesExtensionTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    /// <summary>
    /// The primary proof, exercised through the real product entry points (Load -&gt; edit -&gt; Save
    /// -&gt; Load again): a brand-new formula against a sheet the already-backed link never cached must
    /// get its sheet name appended to the EXISTING externalLinkN.xml part (not a duplicate part), and
    /// must resolve as a known external sheet on the very next load.
    /// </summary>
    [Fact]
    public void NewFormula_AgainstUncachedSheetOfAlreadyBackedBook_ExtendsExistingPartSheetNames()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        // User types a NEW formula against "Q2" of the SAME already-linked book -- a sheet the
        // pre-existing external link never cached (only "Q1" was cached at last refresh).
        var sheet = workbook.GetSheetAt(0);
        sheet.SetFormula(new CellAddress(sheet.Id, 9, 9), "'[Budget.xlsx]Q2'!A1");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            // Not duplicated: still exactly one externalLinkN.xml part for this one book.
            var externalLinkPartCount = archive.Entries.Count(entry =>
                entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase));
            externalLinkPartCount.Should().Be(1, "the pre-existing external link for Budget.xlsx must be extended in place, not duplicated");

            // The EXISTING part's sheetNames now lists both the originally cached sheet AND the
            // newly referenced one -- and "Q1" keeps index 0 (any cached sheetDataSet for it must
            // not get silently repointed at a different sheet).
            var partXml = LoadXml(archive, "xl/externalLinks/externalLink1.xml");
            var sheetNameVals = partXml.Root!
                .Element(WorkbookNs + "externalBook")!
                .Element(WorkbookNs + "sheetNames")!
                .Elements(WorkbookNs + "sheetName")
                .Select(element => element.Attribute("val")!.Value)
                .ToList();
            sheetNameVals.Should().Equal("Q1", "Q2");

            // The freshly typed formula got rewritten to the numeric-ordinal form against the correct
            // (only) external reference, same as R104's ordinal-rewrite behavior for any other book.
            var worksheetXml = LoadXml(archive, "xl/worksheets/sheet1.xml");
            var formulaTexts = worksheetXml.Root!
                .Element(WorkbookNs + "sheetData")!
                .Elements(WorkbookNs + "row")
                .Elements(WorkbookNs + "c")
                .Elements(WorkbookNs + "f")
                .Select(f => f.Value)
                .ToList();
            formulaTexts.Should().Contain(text => text.Contains("Q2", System.StringComparison.Ordinal));
        }

        // Full round trip through the real reader: on the NEXT load, the external link model itself
        // must now know about "Q2" -- this is the actual observable fix, proven through the same
        // XlsxExternalLinkMetadataReader path production code uses, not a hand-inspected XML fragment.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.ExternalLinks.Should().ContainSingle();
        var link = reloaded.ExternalLinks[0];
        link.SheetNames.Should().Equal("Q1", "Q2");
        link.TryFindSheetIndex("Q2").Should().Be(1);
        link.TryFindSheetIndex("Q1").Should().Be(0, "the pre-existing sheet must keep its original index");
    }

    /// <summary>
    /// No-regression sibling: a new formula against a sheet the already-backed book ALREADY cached
    /// must not grow or duplicate that sheet's entry in the existing part's sheetNames.
    /// </summary>
    [Fact]
    public void NewFormula_AgainstAlreadyCachedSheetOfAlreadyBackedBook_DoesNotDuplicateSheetNames()
    {
        using var source = CreateSourcePackageWithExternalFormula();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should().BeTrue(blockReason);

        // A SECOND new formula against the SAME already-cached "Q1" -- nothing new to append.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetFormula(new CellAddress(sheet.Id, 9, 9), "'[Budget.xlsx]Q1'!B2");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var externalLinkPartCount = archive.Entries.Count(entry =>
            entry.FullName.StartsWith("xl/externalLinks/externalLink", System.StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.Contains("_rels", System.StringComparison.OrdinalIgnoreCase));
        externalLinkPartCount.Should().Be(1);

        var partXml = LoadXml(archive, "xl/externalLinks/externalLink1.xml");
        var sheetNameVals = partXml.Root!
            .Element(WorkbookNs + "externalBook")!
            .Element(WorkbookNs + "sheetNames")!
            .Elements(WorkbookNs + "sheetName")
            .Select(element => element.Attribute("val")!.Value)
            .ToList();
        sheetNameVals.Should().Equal("Q1");
    }

    // ---- fixture plumbing (mirrors R96_ExternalLinkAuthoringWriterTests' own private helpers) ----

    private static MemoryStream CreateSourcePackageWithExternalFormula()
    {
        var workbook = new Workbook("ExternalLinkSheetNamesExtensionSibling");
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
                        new XElement(WorkbookNs + "sheetName", new XAttribute("val", "Q1")))))));

        ReplaceXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXExternalBook"),
                    new XAttribute("Type", ExternalLinkPathRelationshipType),
                    new XAttribute("Target", "Budget.xlsx"),
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
                new XElement(WorkbookNs + "f", "'[1]Q1'!A1"),
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
