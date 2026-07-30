using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 100 regression coverage for src/FreeX.Core.IO/XlsxPackageMetadataMerger.cs
/// (ShouldPreserveRelationship): removing a cell's external hyperlink (RemoveHyperlinksCommand /
/// ClearHyperlinksCommand, or simply reassigning <c>Sheet.Hyperlinks</c>) always forces a FULL
/// (ClosedXML) save, because <c>XlsxWorksheetHyperlinkPatch.TryCreate</c> bails whenever the
/// hyperlink count changes. On that full save, <c>PreserveSourcePackageParts</c> merges relationship
/// parts from the session's stored pre-edit source package via
/// <c>XlsxPackageMetadataMerger.MergeRelationshipParts</c>, whose <c>ShouldPreserveRelationship</c>
/// used to short-circuit to <c>true</c> for ANY relationship with <c>TargetMode="External"</c>
/// before ever checking whether the freshly regenerated worksheet XML still referenced it. That left
/// the deleted hyperlink's relationship entry (Type=.../hyperlink, TargetMode=External, pointing at
/// the removed URL) behind in the worksheet's <c>.rels</c> part forever -- and because
/// ApplyPackagePostProcessing re-captures the saved package as the next source-package snapshot, the
/// orphan survives every subsequent save of the session too.
///
/// These tests exercise the real end-to-end entry point (<see cref="XlsxFileAdapter.Save"/> over a
/// workbook with a live, tracked source package from a prior <see cref="XlsxFileAdapter.Load"/>),
/// exactly the reproduction path described in the finding, rather than unit-testing the merge helper
/// directly against a hand-built relationship fragment.
/// </summary>
public sealed class R100_OrphanedWorksheetHyperlinkRelationshipPruneTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Save_AfterRemovingOneOfTwoExternalHyperlinks_PrunesOnlyItsOrphanedRelationship()
    {
        var sourceBytes = CreateTwoHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var keepAddress = new CellAddress(sheet.Id, 1, 1);
        var removeAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.Hyperlinks[keepAddress].Should().Be("https://example.com/keep-me");
        sheet.Hyperlinks[removeAddress].Should().Be("https://example.com/remove-me");

        // Act: the user removes only the A2 hyperlink (mirrors RemoveHyperlinksCommand /
        // ClearHyperlinksCommand simply reassigning Sheet.Hyperlinks) and saves.
        sheet.Hyperlinks.Remove(removeAddress);
        sheet.HyperlinkMetadata.Remove(removeAddress);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Premise: a hyperlink-count change always bails the fast cell-patch path onto a full
        // (ClosedXML) save -- exactly the path PreserveSourcePackageParts/MergeRelationshipParts
        // runs on, per XlsxWorksheetHyperlinkPatch.TryCreate's Count-mismatch bailout.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var relsRoot = ReadWorksheetRelationshipsRoot(savedBytes);
        var externalRelationships = relsRoot?
            .Elements(RelationshipNs + "Relationship")
            .Where(element => (string?)element.Attribute("TargetMode") == "External")
            .ToList() ?? [];

        externalRelationships.Should().ContainSingle(
            element => (string?)element.Attribute("Target") == "https://example.com/keep-me",
            "the still-live hyperlink's relationship must survive the full save");
        externalRelationships.Should().NotContain(
            element => (string?)element.Attribute("Target") == "https://example.com/remove-me",
            "the removed hyperlink's relationship has no <hyperlink r:id=...\"/> referencing it " +
            "anywhere in the freshly regenerated worksheet XML and must not be carried forward as an " +
            "orphan (R100-io-hyperlink-1)");

        // Reload to make sure the pruned relationship doesn't come back as a phantom hyperlink and
        // that the removal survives a save/reload round trip.
        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedKeepAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        var reloadedRemoveAddress = new CellAddress(reloadedSheet.Id, 2, 1);
        reloadedSheet.Hyperlinks.Should().ContainKey(reloadedKeepAddress);
        reloadedSheet.Hyperlinks.Should().NotContainKey(reloadedRemoveAddress);
    }

    [Fact]
    public void Save_AfterUnrelatedEdit_StillPreservesBothLiveExternalHyperlinkRelationships()
    {
        // Sibling no-regression case: when neither hyperlink is touched, a full save forced by an
        // unrelated structural change must still carry BOTH live external hyperlink relationships
        // forward unchanged -- the fix must not turn "prune the orphan" into "prune every external
        // hyperlink relationship regardless of whether the worksheet XML still references it".
        var sourceBytes = CreateTwoHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        // Unrelated structural change to force a full (ClosedXML) save without touching hyperlinks.
        workbook.AddSheet("ExtraSheet");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var relsRoot = ReadWorksheetRelationshipsRoot(savedBytes);
        var externalRelationships = relsRoot?
            .Elements(RelationshipNs + "Relationship")
            .Where(element => (string?)element.Attribute("TargetMode") == "External")
            .ToList() ?? [];

        externalRelationships.Should().ContainSingle(element => (string?)element.Attribute("Target") == "https://example.com/keep-me");
        externalRelationships.Should().ContainSingle(element => (string?)element.Attribute("Target") == "https://example.com/remove-me");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.Hyperlinks[new CellAddress(reloadedSheet.Id, 1, 1)].Should().Be("https://example.com/keep-me");
        reloadedSheet.Hyperlinks[new CellAddress(reloadedSheet.Id, 2, 1)].Should().Be("https://example.com/remove-me");
    }

    private static XElement? ReadWorksheetRelationshipsRoot(byte[] savedBytes)
    {
        using var package = new MemoryStream(savedBytes, writable: false);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        if (entry is null)
            return null;

        using var stream = entry.Open();
        return XDocument.Load(stream).Root;
    }

    private static byte[] CreateTwoHyperlinkSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:A2"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Keep</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>Remove</t></is></c></row>
                  </sheetData>
                  <hyperlinks>
                    <hyperlink ref="A1" r:id="rIdKeep"/>
                    <hyperlink ref="A2" r:id="rIdRemove"/>
                  </hyperlinks>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdKeep" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/keep-me" TargetMode="External"/>
                  <Relationship Id="rIdRemove" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com/remove-me" TargetMode="External"/>
                </Relationships>
                """));

        return package.ToArray();
    }
}
