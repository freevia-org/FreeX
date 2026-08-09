using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 124 regression coverage for src/FreeX.Core.IO/XlsxWorksheetHyperlinkRelationshipPruner.cs
/// (PruneOrphanedHyperlinkRelationships): the pruner used to iterate
/// <c>context.TargetSheets</c> (keyed by each sheet's CURRENT, post-rename name) and look each
/// entry up directly in <c>context.SourceSheets</c> (keyed by each sheet's LOAD-TIME name). For any
/// sheet renamed earlier in the same edit session, that lookup fails unconditionally --
/// indistinguishable from the sheet having been deleted -- so the whole sheet was silently skipped
/// by the <c>continue</c> at line 74, and any hyperlink removed on it in the same session left its
/// now-dangling relationship behind forever (per this class's own doc comment: an un-pruned orphan
/// is re-captured as the next session's source snapshot and can never be pruned afterward). Every
/// sibling preserver in this file set (XlsxWorksheetDrawingReferencePreserver,
/// XlsxWorksheetFormControlPreserver, XlsxWorksheetMetadataPreserver.MiscMetadata,
/// XlsxUnsupportedSheetReferencePreserver) already resolves this via
/// <see cref="XlsxRenamedSourceSheetResolver"/>; the pruner now does too.
///
/// These tests exercise the real end-to-end entry point (<see cref="XlsxFileAdapter.Save"/> over a
/// workbook with a live, tracked source package from a prior <see cref="XlsxFileAdapter.Load"/>),
/// combined with the real <see cref="RenameSheetCommand"/>, rather than unit-testing the pruner
/// directly against a hand-built relationship fragment.
/// </summary>
public sealed class R124_RenamedSheetHyperlinkRelationshipPruneTests
{
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void Save_AfterRenamingSheetThenRemovingItsHyperlink_PrunesOrphanedRelationship()
    {
        var sourceBytes = CreateTwoHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Sheet1");
        var keepAddress = new CellAddress(sheet.Id, 1, 1);
        var removeAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.Hyperlinks[keepAddress].Should().Be("https://example.com/keep-me");
        sheet.Hyperlinks[removeAddress].Should().Be("https://example.com/remove-me");

        // Act: rename the sheet AND remove one of its hyperlinks in the same edit session before
        // saving -- an ordinary reachable combination (RemoveHyperlinksCommand/ClearHyperlinksCommand
        // per this file's own R100 doc comment always forces a full ClosedXML save, same as a rename).
        var ctx = new TestCommandContext(workbook);
        new RenameSheetCommand(sheet.Id, "Sheet1Renamed").Apply(ctx).Success.Should().BeTrue();
        sheet.Hyperlinks.Remove(removeAddress);
        sheet.HyperlinkMetadata.Remove(removeAddress);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

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
            "the removed hyperlink's relationship on a RENAMED sheet must still be pruned as an " +
            "orphan (R124-io-hyperlink-rename-1), not skipped just because the sheet's name changed " +
            "in the same edit session");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.Name.Should().Be("Sheet1Renamed");
        var reloadedKeepAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        var reloadedRemoveAddress = new CellAddress(reloadedSheet.Id, 2, 1);
        reloadedSheet.Hyperlinks.Should().ContainKey(reloadedKeepAddress);
        reloadedSheet.Hyperlinks.Should().NotContainKey(reloadedRemoveAddress);
    }

    [Fact]
    public void Save_AfterRenamingSheetWithNoHyperlinkChange_StillPreservesBothLiveExternalHyperlinkRelationships()
    {
        // Sibling no-regression case: a rename alone (no hyperlink removed) must still carry BOTH
        // live external hyperlink relationships forward unchanged on the renamed sheet -- the fix
        // must not turn "resolve the renamed sheet so its orphan can be pruned" into "treat every
        // relationship on a renamed sheet as unprovable and drop it".
        var sourceBytes = CreateTwoHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        var ctx = new TestCommandContext(workbook);
        new RenameSheetCommand(sheet.Id, "Sheet1Renamed").Apply(ctx).Success.Should().BeTrue();

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
        reloadedSheet.Name.Should().Be("Sheet1Renamed");
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
