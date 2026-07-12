using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R34-io-comments-threaded-mentions-1 regression coverage for XlsxLegacyCommentPreserver:
///
/// When a sheet has ONLY a threaded comment (no real legacy Note, so Sheet.Comments.Count == 0),
/// Excel's legacy comments1.xml/VML "[Threaded comment]" compatibility shim must be purged once
/// the user deletes the thread itself (removed from Sheet.ThreadedComments) -- the
/// Comments.Count == 0 early-return branch previously treated ANY shim-only comments part as
/// "nothing to do" via SourceCommentsHaveOnlyUnmodeledEntries, even when the paired thread was
/// gone, silently resurrecting the deleted thread's legacy shim forever.
///
/// A sibling regression guard verifies an UNTOUCHED thread's shim still survives an unrelated
/// save (no over-correction of the existing "shim-only, nothing deleted" round-trip case).
/// </summary>
public sealed class XlsxLegacyCommentThreadedShimDeletionTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    [Fact]
    public void OnlyThreadDeleted_ShimCommentsAndVmlArePurged()
    {
        // Arrange: B2 has ONLY an active threaded comment and its legacy compatibility shim --
        // no real legacy Note anywhere on the sheet, so Sheet.Comments.Count == 0 from load.
        using var sourcePackage = CreateShimOnlyPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.Comments.Should().BeEmpty("the shim never surfaces as a Note (pre-existing behavior)");
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.ThreadedComments.Should().ContainKey(b2, "the paired thread must load into the model");

        // Act: the user deletes the thread itself (not a Note -- there isn't one).
        sheet.ThreadedComments.Remove(b2);
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the now-orphaned shim must not survive anywhere in the physical package.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase))
            .ToList();
        commentsEntries.Should().BeEmpty(
            "the shim's comments.xml must be purged once its paired thread is deleted (R34-io-comments-threaded-mentions-1)");

        // VML: as with the GAP 6 real-note purge (XlsxLegacyCommentAllDeletedPurgeTests), the
        // worksheet's own <legacyDrawing> marker may be restored verbatim by a separate, unrelated
        // preserver (XlsxWorksheetMetadataPreserver byte-preserves worksheet metadata blocks
        // including <legacyDrawing> whenever present in source, regardless of comment state -- an
        // adjacent gap outside XlsxLegacyCommentPreserver's scope). Only assert the VML note shape
        // is gone when that marker is absent; the comments.xml purge above is this finding's core
        // assertion either way.
        var wsEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        wsEntry.Should().NotBeNull();
        using var wsStream = wsEntry!.Open();
        var wsXml = XDocument.Load(wsStream);
        var hasLegacyDrawingMarker = wsXml.Root?.Element(MainNs + "legacyDrawing") is not null;

        if (!hasLegacyDrawingMarker)
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                var vml = XDocument.Load(stream);
                var hasNoteShape = vml.Root?.Elements(VmlNs + "shape")
                    .Any(shape => shape.Elements(ExcelVmlNs + "ClientData")
                        .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)))
                    ?? false;
                hasNoteShape.Should().BeFalse(
                    $"no leftover shim VML shape should remain in {entry.FullName} once its thread is deleted (R34-io-comments-threaded-mentions-1)");
            }
        }

        var reloaded = adapter.Load(saved.CloneForReload());
        var rs = reloaded.GetSheetAt(0);
        rs.Comments.Should().BeEmpty();
        rs.ThreadedComments.Should().NotContainKey(new CellAddress(rs.Id, 2, 2));
    }

    [Fact]
    public void ShimOnly_ThreadUntouched_ShimSurvives()
    {
        // Sibling regression guard: same shim-only fixture, but the thread is left completely
        // alone -- this must remain the pre-existing "nothing deleted" round-trip behavior.
        using var sourcePackage = CreateShimOnlyPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.ThreadedComments.Should().ContainKey(b2);

        // Act: pure round-trip, no edits at all.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the shim's comments.xml (and its VML) must still be present.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase))
            .ToList();
        commentsEntries.Should().NotBeEmpty(
            "the shim must survive an unrelated save while its thread is still alive (no over-correction)");

        var vmlEntries = archive.Entries.Where(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase)).ToList();
        var hasShimShape = vmlEntries.Any(entry =>
        {
            using var stream = entry.Open();
            var vml = XDocument.Load(stream);
            return vml.Root?.Elements(VmlNs + "shape")
                .Any(shape => shape.Elements(ExcelVmlNs + "ClientData")
                    .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)))
                ?? false;
        });
        hasShimShape.Should().BeTrue("the shim's VML note shape must also survive while its thread is untouched");

        var reloaded = adapter.Load(saved.CloneForReload());
        reloaded.GetSheetAt(0).ThreadedComments.Should().ContainKey(new CellAddress(reloaded.GetSheetAt(0).Id, 2, 2));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture: a sheet with ONLY a threaded comment (+ its legacy shim) at B2
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateShimOnlyPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors><author>tc={5A2F1234-0000-0000-0000-000000000001}</author></authors>
              <commentList>
                <comment ref="B2" authorId="0"><text><r><t>[Threaded comment]

            Your version of Excel allows you to read this threaded comment.</t></r></text></comment>
              </commentList>
            </comments>
            """;

        var threadedCommentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <threadedComment ref="B2" dT="2026-01-01T00:00:00Z" personId="{6B3A1111-0000-0000-0000-000000000002}" id="{7C4B2222-0000-0000-0000-000000000003}">
                <text>Please review</text>
              </threadedComment>
            </ThreadedComments>
            """;

        var personXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <personList xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <person displayName="Dana" id="{6B3A1111-0000-0000-0000-000000000002}" userId="Dana" providerId="None"/>
            </personList>
            """;

        var vmlXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="_x0000_s1025" type="#_x0000_t202"
                       style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>1, 15, 0, 2, 3, 15, 4, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>1</x:Row>
                  <x:Column>1</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithCommentsAndThread()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml),
            ("xl/threadedComments/threadedComment1.xml", threadedCommentsXml),
            ("xl/persons/person.xml", personXml));
    }

    private static string ContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
          <Override PartName="/xl/threadedComments/threadedComment1.xml" ContentType="application/vnd.ms-excel.threadedcomments+xml"/>
          <Override PartName="/xl/persons/person.xml" ContentType="application/vnd.ms-excel.person+xml"/>
        </Types>
        """;

    private static string RootRelsXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelsXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
          </fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
          <dxfs count="0"/>
          <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
        </styleSheet>
        """;

    private static string WorksheetXmlWithLegacyDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:D4"/>
          <sheetData>
            <row r="2"><c r="B2" t="inlineStr"><is><t>review</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string SheetRelsWithCommentsAndThread() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
          <Relationship Id="rId3" Type="http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" Target="../threadedComments/threadedComment1.xml"/>
        </Relationships>
        """;
}

file static class MemoryStreamCloneExtensions3
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
