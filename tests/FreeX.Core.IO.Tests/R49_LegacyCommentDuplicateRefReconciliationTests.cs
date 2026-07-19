using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R49-io-comment-legacy-vml-3-2: legacy comment reconciliation must tolerate a source
/// xl/commentsN.xml part that has two &lt;comment&gt; elements with the SAME ref (not something
/// real Excel itself writes, but exactly the kind of duplication that shows up in files produced
/// or re-saved by third-party tools, hand-patched XML, or a file that already went through a
/// lossy repair/merge cycle). Before the fix, <c>ReadLegacyCommentElementsByReference</c> built
/// its ref-&gt;element map via a plain <c>ToDictionary</c>, which throws
/// <see cref="ArgumentException"/> on the second duplicate key -- and since nothing in the save
/// call chain catches it, the WHOLE save aborted, losing every other pending edit in the
/// workbook, not just comment fidelity.
/// </summary>
public sealed class R49_LegacyCommentDuplicateRefReconciliationTests
{
    [Fact]
    public void DuplicateRefInSourceComments_SaveDoesNotThrow_AndPreservesOtherEdits()
    {
        // Arrange: source comments1.xml has TWO <comment ref="A1"> entries (duplicate ref) plus
        // one normal, unique-ref comment on B2.
        using var sourcePackage = CreateDuplicateRefCommentPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Act: make an edit unrelated to the duplicate (author-only change on the surviving B2
        // note), then save. Before the fix this throws ArgumentException and the whole save --
        // including this edit -- is lost.
        var b2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 2);
        sheet.CommentAuthors[b2] = "UpdatedAuthor";

        var adapterAct = () =>
        {
            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            return saved;
        };

        adapterAct.Should().NotThrow(
            "a duplicate <comment ref=...> in the source comments part must be tolerated " +
            "(de-duplicated), not crash the entire save (R49-io-comment-legacy-vml-3-2)");

        // Assert: the edit that triggered the save must actually have landed (i.e. the save
        // wasn't silently a no-op either) -- reload and check.
        using var saved2 = new MemoryStream();
        adapter.Save(workbook, saved2);
        saved2.Position = 0;
        var reloaded = adapter.Load(saved2);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedB2 = reloadedSheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 2);
        reloadedSheet.CommentAuthors.Should().ContainKey(reloadedB2);
        reloadedSheet.CommentAuthors[reloadedB2].Should().Be("UpdatedAuthor",
            "the pending author edit must survive the save, not be lost to an aborted save");
    }

    [Fact]
    public void NoDuplicateRefs_SaveStillWorks_NoRegression()
    {
        // Sibling no-regression case: the ordinary (no duplicate ref) reconciliation path must
        // keep working exactly as before.
        using var sourcePackage = CreateNoDuplicateRefCommentPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var b2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 2);
        sheet.CommentAuthors[b2] = "UpdatedAuthor";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedB2 = reloadedSheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 2);
        reloadedSheet.CommentAuthors.Should().ContainKey(reloadedB2);
        reloadedSheet.CommentAuthors[reloadedB2].Should().Be("UpdatedAuthor");

        var a1 = reloadedSheet.Comments.Keys.Single(a => a.Row == 1 && a.Col == 1);
        reloadedSheet.Comments[a1].Should().Be("Only note on A1");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

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

    private static string WorksheetXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:B2"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>a1</t></is></c></row>
            <row r="2"><c r="B2" t="inlineStr"><is><t>b2</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string SheetRelsWithComments() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;

    private static string VmlDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="_x0000_s1025" type="#_x0000_t202"
                   style="position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden"
                   fillcolor="#ffffe1" o:insetmode="auto">
            <v:fill color2="#ffffe1"/>
            <v:shadow color="black" obscured="t"/>
            <v:path o:connecttype="none"/>
            <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
            <x:ClientData ObjectType="Note">
              <x:MoveWithCells/>
              <x:SizeWithCells/>
              <x:Anchor>0, 15, 0, 2, 2, 15, 4, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>0</x:Row>
              <x:Column>0</x:Column>
            </x:ClientData>
          </v:shape>
          <v:shape id="_x0000_s1026" type="#_x0000_t202"
                   style="position:absolute;margin-left:60pt;margin-top:20pt;width:108pt;height:59.25pt;z-index:2;visibility:hidden"
                   fillcolor="#ffffe1" o:insetmode="auto">
            <v:fill color2="#ffffe1"/>
            <v:shadow color="black" obscured="t"/>
            <v:path o:connecttype="none"/>
            <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
            <x:ClientData ObjectType="Note">
              <x:MoveWithCells/>
              <x:SizeWithCells/>
              <x:Anchor>1, 15, 1, 2, 3, 15, 5, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>1</x:Row>
              <x:Column>1</x:Column>
            </x:ClientData>
          </v:shape>
        </xml>
        """;

    private static MemoryStream CreateDuplicateRefCommentPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
                <author>Bob</author>
              </authors>
              <commentList>
                <comment ref="A1" authorId="0">
                  <text><r><t>First A1 (should be superseded)</t></r></text>
                </comment>
                <comment ref="A1" authorId="1">
                  <text><r><t>Second A1 (duplicate ref)</t></r></text>
                </comment>
                <comment ref="B2" authorId="0">
                  <text><r><t>Only note on B2</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXml()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", VmlDrawing()));
    }

    private static MemoryStream CreateNoDuplicateRefCommentPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
              </authors>
              <commentList>
                <comment ref="A1" authorId="0">
                  <text><r><t>Only note on A1</t></r></text>
                </comment>
                <comment ref="B2" authorId="0">
                  <text><r><t>Only note on B2</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXml()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", VmlDrawing()));
    }
}
