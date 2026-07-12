using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R33-meta-2 regression: TryBuildReconciledCommentsXml's shift-aware fallback pass (added for
/// R32-io-hyperlink-comment-deep-3) matched an unmatched source &lt;comment&gt; entry to its new,
/// shifted address purely by PLAIN-TEXT equality via <c>List.FindIndex</c> over the model's
/// remaining comment addresses. When two source notes shared IDENTICAL text, <c>FindIndex</c>
/// always returned the FIRST remaining candidate for whichever source entry was processed first
/// (dictionary/list enumeration order) -- which is not guaranteed to correspond to which note
/// actually moved where, so the two notes' rich-text runs/authors could be swapped onto each
/// other's new address.
///
/// Fix: within each identical-text group, sort the still-unmatched source entries by their OLD
/// address and the still-unclaimed candidate addresses by their (new) address, then pair
/// index-for-index -- a row/column insert or delete is a monotonic shift, so two same-text notes
/// keep their relative order across the shift.
/// </summary>
public sealed class XlsxLegacyCommentSameTextTiebreakTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void TwoSameTextNotes_RowShift_EachKeepsOwnAuthorAndRichText()
    {
        // Arrange: A5="Reminder" authored by Alice with a RED run, A10="Reminder" (IDENTICAL
        // text) authored by Bob with a BLUE run -- the only way to tell them apart after a save
        // is author + formatting, exactly what a naive text-only match could swap.
        using var sourcePackage = CreateTwoSameTextNotePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a5 = sheet.Comments.Keys.Single(a => a.Row == 5 && a.Col == 1);
        var a10 = sheet.Comments.Keys.Single(a => a.Row == 10 && a.Col == 1);
        sheet.Comments[a5].Should().Be("Reminder");
        sheet.Comments[a10].Should().Be("Reminder");
        sheet.CommentAuthors[a5].Should().Be("Alice");
        sheet.CommentAuthors[a10].Should().Be("Bob");

        // Act: simulate "insert one row above row 5" -- both notes shift down by 1 (mirrors
        // RowColumnShiftHelpers.ShiftCommentRowsDown), preserving their relative row order.
        sheet.Comments.Remove(a5);
        sheet.Comments.Remove(a10);
        sheet.CommentAuthors.Remove(a5);
        sheet.CommentAuthors.Remove(a10);
        var a6New = new CellAddress(sheet.Id, 6, 1);
        var a11New = new CellAddress(sheet.Id, 11, 1);
        sheet.Comments[a6New] = "Reminder";
        sheet.Comments[a11New] = "Reminder";
        sheet.CommentAuthors[a6New] = "Alice";
        sheet.CommentAuthors[a11New] = "Bob";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: A6 (the note that WAS at A5) must keep Alice's authorship + red run; A11 (the
        // note that WAS at A10) must keep Bob's authorship + blue run. No swap.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var stream = commentsEntry.Open();
        var commentsXml = XDocument.Load(stream);
        var authors = commentsXml.Root!.Element(MainNs + "authors")!
            .Elements(MainNs + "author").Select(a => a.Value).ToList();
        var entries = commentsXml.Root!.Element(MainNs + "commentList")!
            .Elements(MainNs + "comment").ToList();

        var a6Entry = entries.Single(c => c.Attribute("ref")?.Value == "A6");
        var a11Entry = entries.Single(c => c.Attribute("ref")?.Value == "A11");

        authors[int.Parse(a6Entry.Attribute("authorId")!.Value)].Should().Be("Alice",
            "the note originally at A5 must keep Alice's authorship after shifting to A6, not Bob's (R33-meta-2)");
        authors[int.Parse(a11Entry.Attribute("authorId")!.Value)].Should().Be("Bob",
            "the note originally at A10 must keep Bob's authorship after shifting to A11, not Alice's (R33-meta-2)");

        var a6Run = a6Entry.Element(MainNs + "text")!.Element(MainNs + "r")!;
        a6Run.Element(MainNs + "rPr")!.Element(MainNs + "color")!.Attribute("rgb")!.Value.Should().Be("FFFF0000",
            "the note originally at A5 must keep its OWN red run after shifting to A6, not A10's blue run (R33-meta-2)");

        var a11Run = a11Entry.Element(MainNs + "text")!.Element(MainNs + "r")!;
        a11Run.Element(MainNs + "rPr")!.Element(MainNs + "color")!.Attribute("rgb")!.Value.Should().Be("FF0000FF",
            "the note originally at A10 must keep its OWN blue run after shifting to A11, not A5's red run (R33-meta-2)");

        // Reload sanity: the model still reports the correct author at each shifted address.
        var reloaded = adapter.Load(new MemoryStream(saved.ToArray()));
        var rs = reloaded.GetSheetAt(0);
        var ra6 = rs.Comments.Keys.Single(a => a.Row == 6 && a.Col == 1);
        var ra11 = rs.Comments.Keys.Single(a => a.Row == 11 && a.Col == 1);
        rs.CommentAuthors[ra6].Should().Be("Alice");
        rs.CommentAuthors[ra11].Should().Be("Bob");
    }

    [Fact]
    public void TwoDistinctTextNotes_RowShift_StillMatchCorrectly()
    {
        // Sibling case (must NOT regress): when the two shifting notes have DIFFERENT text, the
        // simple per-entry text match is unambiguous -- no group tie-break is needed, and each
        // note must still land on its own new address with its own author.
        using var sourcePackage = CreateTwoDistinctTextNotePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a5 = sheet.Comments.Keys.Single(a => a.Row == 5 && a.Col == 1);
        var a10 = sheet.Comments.Keys.Single(a => a.Row == 10 && a.Col == 1);
        sheet.Comments[a5].Should().Be("Alpha note");
        sheet.Comments[a10].Should().Be("Beta note");

        sheet.Comments.Remove(a5);
        sheet.Comments.Remove(a10);
        sheet.CommentAuthors.Remove(a5);
        sheet.CommentAuthors.Remove(a10);
        var a6New = new CellAddress(sheet.Id, 6, 1);
        var a11New = new CellAddress(sheet.Id, 11, 1);
        sheet.Comments[a6New] = "Alpha note";
        sheet.Comments[a11New] = "Beta note";
        sheet.CommentAuthors[a6New] = "Alice";
        sheet.CommentAuthors[a11New] = "Bob";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var stream = commentsEntry.Open();
        var commentsXml = XDocument.Load(stream);
        var authors = commentsXml.Root!.Element(MainNs + "authors")!
            .Elements(MainNs + "author").Select(a => a.Value).ToList();
        var entries = commentsXml.Root!.Element(MainNs + "commentList")!
            .Elements(MainNs + "comment").ToList();

        var a6Entry = entries.Single(c => c.Attribute("ref")?.Value == "A6");
        var a11Entry = entries.Single(c => c.Attribute("ref")?.Value == "A11");

        authors[int.Parse(a6Entry.Attribute("authorId")!.Value)].Should().Be("Alice");
        authors[int.Parse(a11Entry.Attribute("authorId")!.Value)].Should().Be("Bob");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateTwoSameTextNotePackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
                <author>Bob</author>
              </authors>
              <commentList>
                <comment ref="A5" authorId="0">
                  <text><r><rPr><b/><color rgb="FFFF0000"/><sz val="9"/><rFont val="Tahoma"/></rPr><t>Reminder</t></r></text>
                </comment>
                <comment ref="A10" authorId="1">
                  <text><r><rPr><color rgb="FF0000FF"/><sz val="9"/><rFont val="Tahoma"/></rPr><t>Reminder</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return BuildPackage(commentsXml);
    }

    private static MemoryStream CreateTwoDistinctTextNotePackage()
    {
        var commentsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>Alice</author>
                <author>Bob</author>
              </authors>
              <commentList>
                <comment ref="A5" authorId="0">
                  <text><r><t>Alpha note</t></r></text>
                </comment>
                <comment ref="A10" authorId="1">
                  <text><r><t>Beta note</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return BuildPackage(commentsXml);
    }

    private static MemoryStream BuildPackage(string commentsXml) =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXml()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", VmlDrawingXml()));

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
          <dimension ref="A1:A11"/>
          <sheetData>
            <row r="5"><c r="A5" t="inlineStr"><is><t>five</t></is></c></row>
            <row r="10"><c r="A10" t="inlineStr"><is><t>ten</t></is></c></row>
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

    /// <summary>
    /// Minimal VML with two note shapes anchored at A5 (0-based row 4, col 0) and A10 (0-based
    /// row 9, col 0) -- required for ClosedXML to load the comments part at all.
    /// </summary>
    private static string VmlDrawingXml() => """
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
}
