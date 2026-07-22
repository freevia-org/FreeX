using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R68-io-comment-note-6-1 regression coverage for XlsxLegacyCommentPreserver:
///
/// Deleting the ONLY modeled legacy note on a sheet (Sheet.Comments.Count drops to 0) used to
/// purge the ENTIRE comments{N}.xml (and its VML) once GAP 6's SourceCommentsHaveOnlyUnmodeledEntries
/// guard found a genuine deletion -- an all-or-nothing purge that also destroyed an UNTOUCHED
/// threaded-comment's legacy compatibility shim sharing the same comments part. The fix rebuilds
/// the comments part keeping only the shim(s) that still need preserving instead of purging
/// everything, and only falls back to the full purge when nothing needs protecting.
/// </summary>
public sealed class R68_LegacyCommentNoteDeletionPreservesShimTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    [Fact]
    public void RealNoteDeleted_LiveThreadedShimOnSameSheet_ShimSurvivesReconciledCommentsPart()
    {
        // Arrange: A1 has a real legacy note (Alice); B5 has ONLY a live threaded comment plus its
        // legacy compatibility shim -- both entries share the same xl/comments1.xml/VML part.
        using var sourcePackage = CreateNotePlusShimPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a1 = sheet.Comments.Keys.Single();
        sheet.Comments.Should().ContainKey(a1);
        var b5 = new CellAddress(sheet.Id, 5, 2);
        sheet.ThreadedComments.Should().ContainKey(b5, "the paired thread must load into the model");

        // Act: delete the ONLY real note; leave the threaded comment at B5 completely untouched.
        sheet.Comments.Remove(a1);
        sheet.CommentAuthors.Remove(a1);
        sheet.ShownComments.Remove(a1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the comments part must still exist (it was not all-or-nothing purged) ...
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.SingleOrDefault(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        commentsEntry.Should().NotBeNull(
            "the live threaded-comment shim at B5 must keep the comments part alive (R68-io-comment-note-6-1)");

        using var stream = commentsEntry!.Open();
        var commentsXml = XDocument.Load(stream);
        var refs = commentsXml.Root!
            .Element(MainNs + "commentList")!
            .Elements(MainNs + "comment")
            .Select(c => c.Attribute("ref")?.Value)
            .ToList();

        // ... the deleted note's entry must be gone ...
        refs.Should().NotContain("A1", "the deleted note must not survive in the reconciled comments part");
        // ... but the shim's entry must be preserved.
        refs.Should().ContainSingle(r => string.Equals(r, "B5", StringComparison.OrdinalIgnoreCase),
            "the untouched thread's legacy compatibility shim must survive the reconciled comments part");

        // The VML note-shape count must stay consistent with the reconciled comments part: only
        // the shim's own shape (anchored at B5, 0-based row 4/col 1) should remain.
        var vmlEntry = archive.Entries.Single(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));
        using var vmlStream = vmlEntry.Open();
        var vmlXml = XDocument.Load(vmlStream);
        var noteShapes = vmlXml.Root!.Elements(VmlNs + "shape")
            .Where(shape => shape.Elements(ExcelVmlNs + "ClientData")
                .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        noteShapes.Should().ContainSingle(
            "the deleted note's VML shape must not survive alongside the reconciled comments part (would otherwise desync from it)");
        var survivingClientData = noteShapes[0].Element(ExcelVmlNs + "ClientData")!;
        survivingClientData.Element(ExcelVmlNs + "Row")!.Value.Should().Be("4");
        survivingClientData.Element(ExcelVmlNs + "Column")!.Value.Should().Be("1");

        // The model itself must also come back clean/correct on reload.
        var reloaded = adapter.Load(saved.CloneForReload());
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Comments.Should().BeEmpty("the deleted note must not resurface");
        reloadedSheet.ThreadedComments.Should().ContainKey(new CellAddress(reloadedSheet.Id, 5, 2),
            "the untouched thread must still load after the note deletion + shim reconciliation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sibling regression guard — no live shim on the sheet still purges cleanly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RealNoteDeleted_NoShimOnSheet_StillPurgesCleanly()
    {
        // Arrange: the same fixture, but WITHOUT any threaded comment/shim at all -- only the real
        // note at A1. This is the pre-existing GAP 6 purge path and must be unaffected.
        using var sourcePackage = CreateNoteOnlyPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a1 = sheet.Comments.Keys.Single();
        sheet.Comments.Remove(a1);
        sheet.CommentAuthors.Remove(a1);
        sheet.ShownComments.Remove(a1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase))
            .ToList();
        commentsEntries.Should().BeEmpty(
            "with no live shim to protect, the resurrected comments part must still be purged entirely (no regression)");

        var reloaded = adapter.Load(saved.CloneForReload());
        reloaded.GetSheetAt(0).Comments.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateNotePlusShimPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
                <author>tc={5A2F1234-0000-0000-0000-000000000001}</author>
              </authors>
              <commentList>
                <comment ref="A1" authorId="0"><text><r><t>Confidential: do not share</t></r></text></comment>
                <comment ref="B5" authorId="1"><text><r><t>[Threaded comment]

            Your version of Excel allows you to read this threaded comment.</t></r></text></comment>
              </commentList>
            </comments>
            """;

        var threadedCommentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <threadedComment ref="B5" dT="2026-01-01T00:00:00Z" personId="{6B3A1111-0000-0000-0000-000000000002}" id="{7C4B2222-0000-0000-0000-000000000003}">
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
                  <x:Row>0</x:Row>
                  <x:Column>0</x:Column>
                </x:ClientData>
              </v:shape>
              <v:shape id="_x0000_s1026" type="#_x0000_t202"
                       style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:2;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>2, 15, 5, 2, 4, 15, 9, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>4</x:Row>
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

    private static MemoryStream CreateNoteOnlyPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors><author>Alice</author></authors>
              <commentList>
                <comment ref="A1" authorId="0"><text><r><t>Confidential: do not share</t></r></text></comment>
              </commentList>
            </comments>
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
                  <x:Row>0</x:Row>
                  <x:Column>0</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXmlNoThread()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithCommentsOnly()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));
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

    private static string ContentTypesXmlNoThread() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
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
          <dimension ref="A1:C5"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>secret</t></is></c></row>
            <row r="5"><c r="B5" t="inlineStr"><is><t>review</t></is></c></row>
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

    private static string SheetRelsWithCommentsOnly() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;
}

file static class MemoryStreamCloneExtensions4
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
