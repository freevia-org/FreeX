using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for legacy-note (non-threaded comment) data-loss bugs.
///
/// GAP 1 – Author dropped on full save (ClosedXML CreateComment never set Author).
/// GAP 2 – Author-only change not detected by the patch baseline (EqualsModel ignored Authors).
/// GAP 5 – Preserve-guard dropped ALL notes' XML when any one note was added or deleted.
/// </summary>
public sealed class XlsxLegacyNoteRoundTripTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 1 – author fidelity on full save
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gap1_FullSave_PreservesNoteAuthor()
    {
        // Arrange: a fresh workbook (no source package) with a note that has a named author.
        var workbook = new Workbook("Gap1Test");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.Comments[address] = "Review this";
        sheet.CommentAuthors[address] = "Alice";

        // Act: save and reload.
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        // Assert: text AND author survive the round-trip.
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.Comments.Should().ContainKey(reloadedAddress);
        reloadedSheet.Comments[reloadedAddress].Should().Be("Review this");
        reloadedSheet.CommentAuthors.Should().ContainKey(reloadedAddress,
            "the author set on the model must survive save+reload (GAP 1)");
        reloadedSheet.CommentAuthors[reloadedAddress].Should().Be("Alice");
    }

    [Fact]
    public void Gap1_FullSave_PackageXml_RecordsCorrectAuthor()
    {
        // Also verify the saved package's comments XML directly.
        var workbook = new Workbook("Gap1PackageTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Comments[address] = "Hello";
        sheet.CommentAuthors[address] = "Bob";

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var commentsEntry = archive.Entries.SingleOrDefault(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        commentsEntry.Should().NotBeNull("comments XML must be present in the package");

        using var stream = commentsEntry!.Open();
        var commentsXml = XDocument.Load(stream);

        var authors = commentsXml.Root!
            .Element(MainNs + "authors")?
            .Elements(MainNs + "author")
            .Select(a => a.Value)
            .ToList();
        authors.Should().NotBeNull().And.Contain("Bob",
            "the author name must be written to <authors> in comments XML (GAP 1)");
    }

    [Fact]
    public void Gap1_FullSave_MultipleAuthors_AllPreserved()
    {
        var workbook = new Workbook("Gap1MultiAuthor");
        var sheet = workbook.AddSheet("S1");
        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 2, 2);
        sheet.Comments[addr1] = "First note";
        sheet.CommentAuthors[addr1] = "Alice";
        sheet.Comments[addr2] = "Second note";
        sheet.CommentAuthors[addr2] = "Bob";

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var rs = reloaded.GetSheetAt(0);
        var ra1 = new CellAddress(rs.Id, 1, 1);
        var ra2 = new CellAddress(rs.Id, 2, 2);
        rs.CommentAuthors.Should().ContainKey(ra1);
        rs.CommentAuthors[ra1].Should().Be("Alice");
        rs.CommentAuthors.Should().ContainKey(ra2);
        rs.CommentAuthors[ra2].Should().Be("Bob");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 2 – author-only change detected as a difference
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gap2_AuthorOnlyChange_IsPreservedAfterSaveReload()
    {
        // Arrange: create a workbook that already has a saved source package (simulates
        // loading an existing xlsx and editing only the author).
        using var sourcePackage = CreateTwoNotePackage("C2", "Review this", "Alice",
                                                       "D4", "Another note", "Charlie");

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Find the C2 address in the loaded model.
        var c2Address = sheet.Comments.Keys
            .Single(a => a.Row == 2 && a.Col == 3);

        // Change ONLY the author (text unchanged).
        sheet.CommentAuthors[c2Address] = "UpdatedAlice";

        // Act.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        // Assert.
        var rs = reloaded.GetSheetAt(0);
        var ra = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        rs.Comments[ra].Should().Be("Review this", "text must be unchanged");
        rs.CommentAuthors.Should().ContainKey(ra,
            "author-only change must survive save+reload (GAP 2)");
        rs.CommentAuthors[ra].Should().Be("UpdatedAlice",
            "the new author must be written, not silently dropped (GAP 2)");
    }

    [Fact]
    public void Gap2_AuthorOnlyChange_OtherNoteUnaffected()
    {
        using var sourcePackage = CreateTwoNotePackage("C2", "Note1", "Alice",
                                                       "D4", "Note2", "Bob");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Change only the author of C2.
        var c2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        sheet.CommentAuthors[c2] = "NewAlice";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var rs = reloaded.GetSheetAt(0);
        var d4 = rs.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);
        rs.CommentAuthors.Should().ContainKey(d4,
            "untouched note's author must also be preserved");
        rs.CommentAuthors[d4].Should().Be("Bob");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 5 – preserve-guard resilience when notes are added or deleted
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gap5_AddOneNote_OriginalTwoNotesPreserveAuthor()
    {
        // Arrange: 2 existing notes with authors (stored in a source package).
        using var sourcePackage = CreateTwoNotePackage("C2", "Original note 1", "Alice",
                                                       "D4", "Original note 2", "Bob");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Act: add a 3rd note (this changes note count so naive guard fails).
        var newAddress = new CellAddress(sheet.Id, 5, 5); // E5
        sheet.Comments[newAddress] = "New note";
        sheet.CommentAuthors[newAddress] = "Carol";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        // Assert: all 3 notes are present.
        var rs = reloaded.GetSheetAt(0);
        rs.Comments.Should().HaveCount(3, "all three notes must survive");

        var rc2 = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        rs.Comments[rc2].Should().Be("Original note 1");
        rs.CommentAuthors.Should().ContainKey(rc2,
            "original note's author must be preserved even though a note was added (GAP 5)");
        rs.CommentAuthors[rc2].Should().Be("Alice");

        var rd4 = rs.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);
        rs.Comments[rd4].Should().Be("Original note 2");
        rs.CommentAuthors.Should().ContainKey(rd4,
            "second original note's author must also be preserved (GAP 5)");
        rs.CommentAuthors[rd4].Should().Be("Bob");

        var re5 = rs.Comments.Keys.Single(a => a.Row == 5 && a.Col == 5);
        rs.Comments[re5].Should().Be("New note");
    }

    [Fact]
    public void Gap5_DeleteOneNote_RemainingNotePreservesAuthor()
    {
        using var sourcePackage = CreateTwoNotePackage("C2", "Note to keep", "Alice",
                                                       "D4", "Note to delete", "Bob");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Delete the D4 note.
        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);
        sheet.Comments.Remove(d4);
        sheet.CommentAuthors.Remove(d4);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var rs = reloaded.GetSheetAt(0);
        rs.Comments.Should().HaveCount(1, "only one note must remain");

        var rc2 = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        rs.Comments[rc2].Should().Be("Note to keep");
        rs.CommentAuthors.Should().ContainKey(rc2,
            "remaining note's author must survive when another note is deleted (GAP 5)");
        rs.CommentAuthors[rc2].Should().Be("Alice");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal XLSX package with two legacy notes, each with a named author.
    /// </summary>
    private static MemoryStream CreateTwoNotePackage(
        string ref1, string text1, string author1,
        string ref2, string text2, string author2)
    {
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>
                <author>{SecurityEscape(author1)}</author>
                <author>{SecurityEscape(author2)}</author>
              </authors>
              <commentList>
                <comment ref="{ref1}" authorId="0">
                  <text><r><t>{SecurityEscape(text1)}</t></r></text>
                </comment>
                <comment ref="{ref2}" authorId="1">
                  <text><r><t>{SecurityEscape(text2)}</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", TwoNoteContentTypesXml()),
            ("_rels/.rels", MinimalRootRels()),
            ("xl/workbook.xml", MinimalWorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsWithStyles()),
            ("xl/styles.xml", MinimalStylesXml()),
            ("xl/worksheets/sheet1.xml", MinimalWorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", MinimalVmlDrawing()));
    }

    private static string SecurityEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string TwoNoteContentTypesXml() => """
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

    private static string MinimalRootRels() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string MinimalWorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Data" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelsWithStyles() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string MinimalStylesXml() => """
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

    private static string MinimalWorksheetXmlWithLegacyDrawing() => """
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

    private static string SheetRelsWithComments() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;

    private static string MinimalVmlDrawing() => """
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
              <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>1</x:Row>
              <x:Column>2</x:Column>
            </x:ClientData>
          </v:shape>
        </xml>
        """;
}
