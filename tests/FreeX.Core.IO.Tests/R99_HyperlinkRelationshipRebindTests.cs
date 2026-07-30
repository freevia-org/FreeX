using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the reemitted-stripped-range-hyperlink r:id rebind gap in
/// XlsxWorksheetMetadataPreserver.CellMetadata.cs's MergeWorksheetHyperlinkMetadata: a whole-column/row
/// (or oversized bounded-range) EXTERNAL hyperlink is stripped from the ClosedXML-input copy at load time
/// (XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs) and re-emitted verbatim on a full
/// (non-patch) save. When the reemitted element's original r:id happens to collide with an id
/// XlsxPackageMetadataMerger.MergeRelationshipParts already assigned to some OTHER, unrelated
/// relationship in ClosedXML's freshly regenerated worksheet .rels, the merger renumbers the incoming
/// (stripped hyperlink's) relationship to a fresh id -- but the reemitted &lt;hyperlink&gt; element in the
/// worksheet body was never updated to follow, so it keeps pointing at the id now owned by the OTHER
/// relationship (the wrong URL) instead of its own.
/// </summary>
public sealed class R99_HyperlinkRelationshipRebindTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string StrippedTargetUrl = "https://example.com/stripped-target";
    private const string NormalTargetUrl = "https://example.com/normal-target";

    // Builds a fully valid single-sheet .xlsx package (via a real adapter save, so every required
    // package part is already correct) and then swaps in hand-authored worksheet XML AND a matching
    // worksheet .rels for xl/worksheets/sheet1.xml, mirroring the technique
    // Backlog_cellmetadata_Tests.CreateSourcePackage uses (extended with a .rels part, which that helper
    // does not need since its fixtures only use internal "location" hyperlinks with no r:id).
    private static MemoryStream CreateSourcePackage(string worksheetXml, string worksheetRelsXml)
    {
        var workbook = new Workbook("R99-HyperlinkRebind");
        workbook.AddSheet("Sheet1");

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var existingEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            existingEntry.Should().NotBeNull("a freshly saved single-sheet workbook must contain xl/worksheets/sheet1.xml");
            existingEntry!.Delete();

            var replacementEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(replacementEntry.Open()))
                writer.Write(worksheetXml);

            var relsEntry = archive.CreateEntry("xl/worksheets/_rels/sheet1.xml.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(worksheetRelsXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static string ResolveHyperlinkTargetUrl(XDocument worksheetXml, XDocument relsXml, string reference)
    {
        var hyperlink = worksheetXml.Root!
            .Element(WorkbookNs + "hyperlinks")?
            .Elements(WorkbookNs + "hyperlink")
            .FirstOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.Ordinal));

        hyperlink.Should().NotBeNull($"the reemitted hyperlink for ref=\"{reference}\" must survive the full save");

        var relId = hyperlink!.Attribute(RelNs + "id")?.Value;
        relId.Should().NotBeNullOrWhiteSpace($"the reemitted external hyperlink for ref=\"{reference}\" must carry an r:id");

        var relationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relId, StringComparison.Ordinal));

        relationship.Should().NotBeNull(
            $"r:id=\"{relId}\" on the ref=\"{reference}\" hyperlink must resolve to an actual relationship in the " +
            "saved worksheet's .rels part");

        return relationship!.Attribute("Target")?.Value ?? string.Empty;
    }

    [Fact]
    public void FullSave_RebindsReemittedExternalHyperlinkRelationshipId_WhenIdCollidesWithRegeneratedRelationship()
    {
        // The stripped whole-column hyperlink's SOURCE r:id ("rId5") is deliberately chosen to match the
        // id ClosedXML's own regeneration deterministically assigns to the surviving normal hyperlink's
        // relationship in this fixture shape (workbook.xml.rels + this sheet's own prior relationships
        // consume rId1-rId4, so the first worksheet-local relationship ClosedXML writes on save lands on
        // rId5) -- forcing XlsxPackageMetadataMerger.MergeRelationshipParts to hit the id-collision
        // renumbering branch (XlsxPackageMetadataMerger.cs lines ~249-251) for the copied-over stripped
        // relationship. The source's own two relationships use distinct ids (rId5 vs rId7) so the source
        // .rels itself stays valid; the collision only manifests against the TARGET package's
        // independently-numbered regenerated relationships.
        using var source = CreateSourcePackage(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
                <row r="2"><c r="B2" t="str"><v>link</v></c></row>
              </sheetData>
              <hyperlinks>
                <hyperlink ref="C:C" r:id="rId5" display="stripped"/>
                <hyperlink ref="B2" r:id="rId7" display="normal"/>
              </hyperlinks>
            </worksheet>
            """,
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{StrippedTargetUrl}" TargetMode="External"/>
              <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{NormalTargetUrl}" TargetMode="External"/>
            </Relationships>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);

        // Editing a brand-new cell (outside the loaded sheetData) forces a full (non-patch) rewrite --
        // the same technique the existing backlog hyperlink-reemission tests use.
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("r99-hyperlink-rebind-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        var relsEntry = savedArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        worksheetEntry.Should().NotBeNull();
        relsEntry.Should().NotBeNull("the saved worksheet must still carry its own relationships part");

        using var worksheetStream = worksheetEntry!.Open();
        var savedWorksheetXml = XDocument.Load(worksheetStream);
        using var relsStream = relsEntry!.Open();
        var savedRelsXml = XDocument.Load(relsStream);

        // Sanity check that this fixture actually manufactured the collision it claims to: the source's
        // rId5 (stripped hyperlink's relationship) must no longer be the id ClosedXML's own regenerated
        // relationship for ref="B2" holds -- i.e. IDs were renumbered, not passed through untouched.
        var strippedRelationshipId = savedWorksheetXml.Root!
            .Element(WorkbookNs + "hyperlinks")!
            .Elements(WorkbookNs + "hyperlink")
            .First(element => string.Equals(element.Attribute("ref")?.Value, "C:C", StringComparison.Ordinal))
            .Attribute(RelNs + "id")?.Value;
        var normalRelationshipId = savedWorksheetXml.Root!
            .Element(WorkbookNs + "hyperlinks")!
            .Elements(WorkbookNs + "hyperlink")
            .First(element => string.Equals(element.Attribute("ref")?.Value, "B2", StringComparison.Ordinal))
            .Attribute(RelNs + "id")?.Value;
        strippedRelationshipId.Should().NotBe(
            normalRelationshipId,
            "the two hyperlinks must resolve to two DIFFERENT relationships even after any id renumbering");

        // The actual defect: clicking the reemitted whole-column hyperlink must open the URL the user
        // originally set for it, not whatever URL happens to now sit under its stale r:id.
        ResolveHyperlinkTargetUrl(savedWorksheetXml, savedRelsXml, "C:C").Should().Be(
            StrippedTargetUrl,
            "the reemitted stripped-range hyperlink's r:id must be rebound to follow its own relationship " +
            "even when that relationship was renumbered by XlsxPackageMetadataMerger.MergeRelationshipParts " +
            "due to an id collision with ClosedXML's own regenerated worksheet relationships");

        ResolveHyperlinkTargetUrl(savedWorksheetXml, savedRelsXml, "B2").Should().Be(
            NormalTargetUrl,
            "the normal (non-stripped) hyperlink must independently keep resolving to its own URL");
    }

    [Fact]
    public void FullSave_KeepsReemittedExternalHyperlinkRelationshipId_WhenNoIdCollisionOccurs()
    {
        // No-regression sibling: when the stripped hyperlink's source r:id does NOT collide with anything
        // ClosedXML's own regeneration assigns, MergeRelationshipParts copies the relationship through
        // under its original id (no renumbering), so the rebind lookup here must be a no-op that leaves
        // the reemitted element's r:id exactly as it was -- verifying the fix does not regress the
        // ordinary non-colliding pass-through case the original two backlog tests already covered (with
        // internal "location" hyperlinks; this covers the EXTERNAL r:id variant of that same pass-through).
        using var source = CreateSourcePackage(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheetData>
                <row r="1"><c r="A1"><v>1</v></c></row>
              </sheetData>
              <hyperlinks>
                <hyperlink ref="A1:A200000" r:id="rId1" display="stripped"/>
              </hyperlinks>
            </worksheet>
            """,
            $"""
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="{StrippedTargetUrl}" TargetMode="External"/>
            </Relationships>
            """);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("r99-hyperlink-no-collision-full-save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = savedArchive.GetEntry("xl/worksheets/sheet1.xml");
        var relsEntry = savedArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        worksheetEntry.Should().NotBeNull();
        relsEntry.Should().NotBeNull();

        using var worksheetStream = worksheetEntry!.Open();
        var savedWorksheetXml = XDocument.Load(worksheetStream);
        using var relsStream = relsEntry!.Open();
        var savedRelsXml = XDocument.Load(relsStream);

        ResolveHyperlinkTargetUrl(savedWorksheetXml, savedRelsXml, "A1:A200000").Should().Be(
            StrippedTargetUrl,
            "with no id collision, the reemitted stripped-range hyperlink must still resolve to its " +
            "original external URL exactly as the pre-existing backlog regression tests expect");
    }
}
