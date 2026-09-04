using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-37 legacy-comment / VML fixes:
///
///  - R37-io-comments-legacy-vml-2-1: toggling a note's pin state (Show/Hide Comment) must sync
///    the VML shape's CSS <c>visibility</c> style property, not just the ClientData
///    <c>&lt;x:Visible/&gt;</c> flag -- real Excel (and any VML-conformant renderer) paints the
///    box according to the CSS property.
///
///  - R37-io-comments-legacy-vml-2-2: the VML-side shift-aware shape match must apply the same
///    same-text tiebreak (sort old/new addresses, pair index-for-index) the comments.xml side
///    already got in R33-meta-2, so two identical-text notes don't swap box geometry on a
///    row/column shift.
///
///  - R37-io-comments-legacy-vml-2-3: comment plain-text extraction must exclude
///    &lt;rPh&gt;/&lt;t&gt; phonetic-guide (furigana/pinyin reading-hint) runs, which CT_Rst
///    allows alongside the visible &lt;r&gt;/&lt;t&gt; runs but which real Excel never displays
///    as part of the comment's text.
/// </summary>
public sealed class XlsxLegacyCommentVmlBugsTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    // ─────────────────────────────────────────────────────────────────────────
    // R37-io-comments-legacy-vml-2-1 -- visibility toggle must sync VML CSS style
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VisibilityToggle_PinAndUnpin_SyncsVmlStyleAttribute()
    {
        // Arrange: C2 pinned/visible, D4 hidden -- exactly how real Excel authors the two states
        // (ClientData <x:Visible/> AND style="...;visibility:visible|hidden" kept in sync).
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var c2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);

        // Act: flip the pins -- unpin C2 (was shown), pin D4 (was hidden).
        sheet.ShownComments.Remove(c2);
        sheet.ShownComments.Add(d4);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull("a VML drawing must be referenced by the worksheet in the saved package");

        var c2Shape = FindNoteShape(vmlXml!, row0: 1, col0: 2);
        var d4Shape = FindNoteShape(vmlXml!, row0: 3, col0: 3);
        c2Shape.Should().NotBeNull();
        d4Shape.Should().NotBeNull();

        // Assert: the shape's CSS visibility must now match the NEW pin state, not just the
        // ClientData flag -- otherwise real Excel still paints the box in its old state.
        var c2Style = c2Shape!.Attribute("style")?.Value ?? "";
        c2Style.Should().Contain("visibility:hidden",
            "C2 was unpinned, so its VML shape's CSS visibility must flip to hidden " +
            "(R37-io-comments-legacy-vml-2-1)");
        c2Style.Should().NotContain("visibility:visible");

        var d4Style = d4Shape!.Attribute("style")?.Value ?? "";
        d4Style.Should().Contain("visibility:visible",
            "D4 was pinned, so its VML shape's CSS visibility must flip to visible " +
            "(R37-io-comments-legacy-vml-2-1)");
        d4Style.Should().NotContain("visibility:hidden");
    }

    [Fact]
    public void VisibilityToggle_PureRoundTrip_StyleAndGeometryUnchanged_NoRegression()
    {
        // Sibling no-regression case: when nothing is toggled, both the visibility CSS property
        // and every other CSS property (custom geometry) must survive completely unchanged.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull();

        var c2Shape = FindNoteShape(vmlXml!, row0: 1, col0: 2);
        var d4Shape = FindNoteShape(vmlXml!, row0: 3, col0: 3);

        var c2Style = c2Shape!.Attribute("style")?.Value ?? "";
        c2Style.Should().Contain("visibility:visible", "C2 was already pinned and must stay so");
        c2Style.Should().Contain("width:200pt", "C2's custom geometry must be untouched");
        c2Style.Should().Contain("height:120pt", "C2's custom geometry must be untouched");

        var d4Style = d4Shape!.Attribute("style")?.Value ?? "";
        d4Style.Should().Contain("visibility:hidden", "D4 was already unpinned and must stay so");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R37-io-comments-legacy-vml-2-2 -- VML shift match same-text tiebreak
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoSameTextNotes_RowShift_VmlShapesKeepOwnGeometry()
    {
        // Arrange: A5 has a CUSTOM box (200pt x 120pt), A10 has the SAME text ("Reminder") but
        // the DEFAULT box (108pt x 59.25pt). Both notes shift down by one row (as if a row was
        // inserted above row 5) in the same save.
        using var sourcePackage = CreateTwoSameTextGeometryNotePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a5 = sheet.Comments.Keys.Single(a => a.Row == 5 && a.Col == 1);
        var a10 = sheet.Comments.Keys.Single(a => a.Row == 10 && a.Col == 1);
        sheet.Comments[a5].Should().Be("Reminder");
        sheet.Comments[a10].Should().Be("Reminder");

        // Act: simulate "insert one row above row 5" -- both notes shift down by 1.
        sheet.Comments.Remove(a5);
        sheet.Comments.Remove(a10);
        var a6New = new CellAddress(sheet.Id, 6, 1);
        var a11New = new CellAddress(sheet.Id, 11, 1);
        sheet.Comments[a6New] = "Reminder";
        sheet.Comments[a11New] = "Reminder";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the shape now at A6 (0-based row 5, was A5) must keep its OWN 200x120 box; the
        // shape now at A11 (0-based row 10, was A10) must keep its OWN default 108x59.25 box.
        // Without the same-text tiebreak, an unordered dictionary scan can pair them the other
        // way around and swap the two notes' geometry (R37-io-comments-legacy-vml-2-2).
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull();

        var a6Shape = FindNoteShape(vmlXml!, row0: 5, col0: 0);
        var a11Shape = FindNoteShape(vmlXml!, row0: 10, col0: 0);
        a6Shape.Should().NotBeNull("the note that was at A5 must have a shape at its new address A6");
        a11Shape.Should().NotBeNull("the note that was at A10 must have a shape at its new address A11");

        a6Shape!.Attribute("style")!.Value.Should().Contain("width:200pt")
            .And.Contain("height:120pt",
                "A6 (originally A5's shape) must keep its OWN custom geometry, not A10's default " +
                "box (R37-io-comments-legacy-vml-2-2)");

        a11Shape!.Attribute("style")!.Value.Should().Contain("width:108pt")
            .And.Contain("height:59.25pt",
                "A11 (originally A10's shape) must keep its OWN default geometry, not A5's custom " +
                "200x120 box (R37-io-comments-legacy-vml-2-2)");
    }

    [Fact]
    public void TwoSameTextNotes_NoShift_PureRoundTrip_EachKeepsOwnGeometry_NoRegression()
    {
        // Sibling no-regression case: the same identical-text/different-geometry pair, saved
        // WITHOUT any address shift, must still round-trip through the direct (same-cell) match
        // path unaffected by the shift-aware pass.
        using var sourcePackage = CreateTwoSameTextGeometryNotePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull();

        var a5Shape = FindNoteShape(vmlXml!, row0: 4, col0: 0);
        var a10Shape = FindNoteShape(vmlXml!, row0: 9, col0: 0);
        a5Shape.Should().NotBeNull();
        a10Shape.Should().NotBeNull();

        a5Shape!.Attribute("style")!.Value.Should().Contain("width:200pt").And.Contain("height:120pt");
        a10Shape!.Attribute("style")!.Value.Should().Contain("width:108pt").And.Contain("height:59.25pt");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R37-io-comments-legacy-vml-2-3 -- exclude <rPh> phonetic-guide text
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CommentWithPhoneticGuide_LoadsOnlyVisibleRunText()
    {
        // Arrange: a comment whose <text> carries a visible run PLUS an <rPh> phonetic-guide run
        // (furigana), exactly the CT_Rst shape real Excel writes for Japanese/Chinese templates.
        using var sourcePackage = CreatePhoneticGuideCommentPackage();
        var adapter = new XlsxFileAdapter();

        // Act.
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Assert: only the visible run's text is modeled -- the <rPh> reading hint must not be
        // appended/mixed into it (R37-io-comments-legacy-vml-2-3).
        var address = sheet.Comments.Keys.Single();
        sheet.Comments[address].Should().Be("Tanaka",
            "the phonetic-guide <rPh> text must be excluded from the modeled comment text; " +
            "only the visible run's text is what real Excel displays");
    }

    [Fact]
    public void CommentWithMultipleRunsNoPhoneticGuide_LoadsFullConcatenatedText_NoRegression()
    {
        // Sibling no-regression case: a normal multi-run comment (no <rPh> at all) must still
        // concatenate ALL of its visible run text -- the <rPh> exclusion must not accidentally
        // drop ordinary <r>/<t> runs.
        using var sourcePackage = CreateMultiRunCommentPackage();
        var adapter = new XlsxFileAdapter();

        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var address = sheet.Comments.Keys.Single();
        sheet.Comments[address].Should().Be("Hello, world!",
            "ordinary multi-run comment text (no phonetic guide) must still concatenate fully");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures shared by all three finding groups
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

    private static string SheetRelsWithComments() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture: two notes, C2 (custom 200x120, pinned) / D4 (default, hidden) -- for finding -2-1
    // ─────────────────────────────────────────────────────────────────────────

    private static string WorksheetXmlCD() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:D4"/>
          <sheetData>
            <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
            <row r="4"><c r="D4" t="inlineStr"><is><t>check</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static MemoryStream CreateTwoNoteGeometryPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
                <author>Bob</author>
              </authors>
              <commentList>
                <comment ref="C2" authorId="0">
                  <text><r><t>Pinned note on C2</t></r></text>
                </comment>
                <comment ref="D4" authorId="1">
                  <text><r><t>Standard note on D4</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        var vmlXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="_x0000_s1025" type="#_x0000_t202"
                       style="position:absolute;margin-left:80pt;margin-top:6pt;width:200pt;height:120pt;z-index:1;visibility:visible"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>2, 15, 1, 2, 6, 15, 7, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>1</x:Row>
                  <x:Column>2</x:Column>
                  <x:Visible/>
                </x:ClientData>
              </v:shape>
              <v:shape id="_x0000_s1026" type="#_x0000_t202"
                       style="position:absolute;margin-left:120pt;margin-top:20pt;width:108pt;height:59.25pt;z-index:2;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>3, 15, 3, 2, 5, 15, 6, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>3</x:Row>
                  <x:Column>3</x:Column>
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
            ("xl/worksheets/sheet1.xml", WorksheetXmlCD()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture: A5/A10 same text "Reminder", different geometry -- for finding -2-2
    // ─────────────────────────────────────────────────────────────────────────

    private static string WorksheetXmlA5A10() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:A11"/>
          <sheetData>
            <row r="5"><c r="A5" t="inlineStr"><is><t>five</t></is></c></row>
            <row r="10"><c r="A10" t="inlineStr"><is><t>ten</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static MemoryStream CreateTwoSameTextGeometryNotePackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
              </authors>
              <commentList>
                <comment ref="A5" authorId="0">
                  <text><r><t>Reminder</t></r></text>
                </comment>
                <comment ref="A10" authorId="0">
                  <text><r><t>Reminder</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        var vmlXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="_x0000_s1025" type="#_x0000_t202"
                       style="position:absolute;margin-left:80pt;margin-top:6pt;width:200pt;height:120pt;z-index:1;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>0, 15, 4, 2, 2, 15, 8, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>4</x:Row>
                  <x:Column>0</x:Column>
                </x:ClientData>
              </v:shape>
              <v:shape id="_x0000_s1026" type="#_x0000_t202"
                       style="position:absolute;margin-left:160pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:2;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>0, 15, 9, 2, 2, 15, 13, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>9</x:Row>
                  <x:Column>0</x:Column>
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
            ("xl/worksheets/sheet1.xml", WorksheetXmlA5A10()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures: phonetic-guide (<rPh>) comment -- for finding -2-3
    // ─────────────────────────────────────────────────────────────────────────

    private static string WorksheetXmlA1() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:A1"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>name</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string MinimalVmlDrawingA1() => """
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
        </xml>
        """;

    private static MemoryStream CreatePhoneticGuideCommentPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
              </authors>
              <commentList>
                <comment ref="A1" authorId="0">
                  <text><r><t>Tanaka</t></r><rPh sb="0" eb="6"><t>タナカ</t></rPh><phoneticPr fontId="1"/></text>
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
            ("xl/worksheets/sheet1.xml", WorksheetXmlA1()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", MinimalVmlDrawingA1()));
    }

    private static MemoryStream CreateMultiRunCommentPackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
              </authors>
              <commentList>
                <comment ref="A1" authorId="0">
                  <text><r><t>Hello, </t></r><r><t>world!</t></r></text>
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
            ("xl/worksheets/sheet1.xml", WorksheetXmlA1()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", MinimalVmlDrawingA1()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VML inspection helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static XElement? FindNoteShape(XDocument vmlXml, uint row0, uint col0)
    {
        if (vmlXml.Root is null) return null;
        return vmlXml.Root.Elements(VmlNs + "shape")
            .FirstOrDefault(shape =>
            {
                var cd = shape.Elements(ExcelVmlNs + "ClientData")
                    .FirstOrDefault(c => string.Equals(
                        c.Attribute("ObjectType")?.Value, "Note",
                        StringComparison.OrdinalIgnoreCase));
                if (cd is null) return false;
                return uint.TryParse(cd.Element(ExcelVmlNs + "Row")?.Value, out var r) && r == row0 &&
                       uint.TryParse(cd.Element(ExcelVmlNs + "Column")?.Value, out var c2) && c2 == col0;
            });
    }

    /// <summary>
    /// Loads the VML drawing file referenced by the worksheet's <c>&lt;legacyDrawing r:id="..."/&gt;</c>
    /// element via the worksheet's relationship file. Returns null if not found.
    /// </summary>
    private static XDocument? LoadWorksheetReferencedVml(ZipArchive archive, string worksheetPath)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace wsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string vmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

        var wsEntry = archive.GetEntry(worksheetPath);
        if (wsEntry is null) return null;

        XDocument wsXml;
        using (var s = wsEntry.Open()) wsXml = XDocument.Load(s);
        var vmlRelId = wsXml.Root?.Element(wsNs + "legacyDrawing")?.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(vmlRelId)) return null;

        var relsPath = "xl/worksheets/_rels/" + System.IO.Path.GetFileName(worksheetPath) + ".rels";
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null) return null;

        XDocument relsXml;
        using (var s = relsEntry.Open()) relsXml = XDocument.Load(s);
        var vmlTarget = relsXml.Root?.Elements(pkgRelNs + "Relationship")
            .Where(r => string.Equals(r.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal) &&
                        string.Equals(r.Attribute("Type")?.Value, vmlRelType, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Attribute("Target")?.Value)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        if (string.IsNullOrWhiteSpace(vmlTarget)) return null;

        var vmlPath = vmlTarget!.StartsWith("..", StringComparison.Ordinal)
            ? "xl/drawings/" + System.IO.Path.GetFileName(vmlTarget)
            : vmlTarget.TrimStart('/');

        var vmlEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, vmlPath, StringComparison.OrdinalIgnoreCase));
        if (vmlEntry is null) return null;

        using var vs = vmlEntry.Open();
        return XDocument.Load(vs);
    }
}
