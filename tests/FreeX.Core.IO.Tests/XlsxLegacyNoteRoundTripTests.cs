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
    // ShownComments – pinned-state model surfacing and round-trip
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShownComments_Load_PinsOnlyNotesWithVmlVisible()
    {
        // Arrange: geometry package where C2 has <x:Visible/> and D4 does not.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();

        // Act: load.
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Assert: only C2 (row=2, col=3) should be in ShownComments.
        var c2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);

        sheet.ShownComments.Should().Contain(c2,
            "C2 has <x:Visible/> in VML and must be surfaced into ShownComments on load");
        sheet.ShownComments.Should().NotContain(d4,
            "D4 does not have <x:Visible/> in VML and must NOT appear in ShownComments");
    }

    [Fact]
    public void ShownComments_RoundTrip_PinnedStateSurvivesSaveAndReload()
    {
        // Arrange: load a package where C2 is pinned.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Act: save and reload.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        // Assert: C2 is still pinned, D4 still not.
        var rs = reloaded.GetSheetAt(0);
        var rc2 = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        var rd4 = rs.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);

        rs.ShownComments.Should().Contain(rc2,
            "<x:Visible/> must survive a save+reload cycle (pinned state round-trip)");
        rs.ShownComments.Should().NotContain(rd4,
            "non-pinned note must remain non-pinned after save+reload");
    }

    [Fact]
    public void ShownComments_TogglePin_SurvivesSaveAndReload()
    {
        // Arrange: load with C2 pinned, D4 not.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var c2 = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);

        // Toggle: unpin C2, pin D4.
        sheet.ShownComments.Remove(c2);
        sheet.ShownComments.Add(d4);

        // Act: save and reload.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        // Assert: pins are flipped.
        var rs = reloaded.GetSheetAt(0);
        var rc2 = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        var rd4 = rs.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);

        rs.ShownComments.Should().NotContain(rc2,
            "C2 was unpinned so ShownComments must not contain it after reload");
        rs.ShownComments.Should().Contain(rd4,
            "D4 was pinned so ShownComments must contain it after reload");
    }

    [Fact]
    public void ShownComments_FreshWorkbook_NoPinnedNotes()
    {
        // A fresh workbook with a manually-added note must have no ShownComments.
        var workbook = new Workbook("FreshTest");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Hello";

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);

        var rs = reloaded.GetSheetAt(0);
        rs.ShownComments.Should().BeEmpty("a freshly-created note must not be pinned by default");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 4 – box geometry + Visible preserved across add/delete
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gap4_AddOneNote_ExistingNoteGeometryPreserved()
    {
        // Arrange: two-note package where C2 has CUSTOM box geometry (width=200pt, height=120pt)
        // and <x:Visible/> (pinned open), D4 has default geometry.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Add a third note on E5.
        var newAddr = new CellAddress(sheet.Id, 5, 5);
        sheet.Comments[newAddr] = "New note on E5";
        sheet.CommentAuthors[newAddr] = "Carol";

        // Act: save.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: inspect the VML referenced by the saved worksheet (not just any VML in the package).
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull("a VML drawing must be referenced by the worksheet in the saved package");

        // C2 shape (row=1,col=2 in 0-based): custom geometry and <x:Visible/> must survive.
        var c2Shape = FindNoteShape(vmlXml!, row0: 1, col0: 2);
        c2Shape.Should().NotBeNull("shape for C2 must be in the reconciled VML");
        c2Shape!.Attribute("style")!.Value.Should().Contain("width:200pt",
            "custom width of C2 shape must be preserved after adding a note (GAP 4)");
        c2Shape.Attribute("style")!.Value.Should().Contain("height:120pt",
            "custom height of C2 shape must be preserved after adding a note (GAP 4)");
        HasVisible(c2Shape).Should().BeTrue(
            "<x:Visible/> of C2 shape must be preserved after adding a note (GAP 4)");

        // D4 shape (row=3,col=3 in 0-based) must also survive.
        var d4Shape = FindNoteShape(vmlXml!, row0: 3, col0: 3);
        d4Shape.Should().NotBeNull("shape for D4 must be in the reconciled VML after adding a note");

        // E5 (row=4,col=4 in 0-based): new note must have a shape.
        var e5Shape = FindNoteShape(vmlXml!, row0: 4, col0: 4);
        e5Shape.Should().NotBeNull("new note E5 must have a shape in the reconciled VML (GAP 4)");
    }

    [Fact]
    public void Gap4_DeleteOneNote_RemainingNoteGeometryPreserved()
    {
        // Arrange: two-note package with custom geometry on C2.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Delete the D4 note.
        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);
        sheet.Comments.Remove(d4);
        sheet.CommentAuthors.Remove(d4);

        // Act.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: C2 shape geometry (including Visible) must survive.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull("a VML drawing must be referenced by the worksheet");

        var c2Shape = FindNoteShape(vmlXml!, row0: 1, col0: 2);
        c2Shape.Should().NotBeNull("shape for C2 must be preserved after deleting D4 (GAP 4)");
        c2Shape!.Attribute("style")!.Value.Should().Contain("width:200pt",
            "custom width must survive deletion of another note (GAP 4)");
        c2Shape.Attribute("style")!.Value.Should().Contain("height:120pt",
            "custom height must survive deletion of another note (GAP 4)");
        HasVisible(c2Shape).Should().BeTrue(
            "<x:Visible/> must survive deletion of another note (GAP 4)");

        // D4 shape must be gone.
        var d4Shape = FindNoteShape(vmlXml!, row0: 3, col0: 3);
        d4Shape.Should().BeNull("deleted note D4 must not have a shape in the reconciled VML");
    }

    [Fact]
    public void Gap4_PureRoundTrip_GeometryPreserved()
    {
        // Arrange: two-note package with custom geometry — no edits.
        using var sourcePackage = CreateTwoNoteGeometryPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        // Act: save without any changes.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: C2 shape geometry and Visible must survive unchanged.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull("a VML drawing must be referenced by the worksheet");

        var c2Shape = FindNoteShape(vmlXml!, row0: 1, col0: 2);
        c2Shape.Should().NotBeNull("C2 shape must survive a pure round-trip (regression guard)");
        c2Shape!.Attribute("style")!.Value.Should().Contain("width:200pt");
        c2Shape.Attribute("style")!.Value.Should().Contain("height:120pt");
        HasVisible(c2Shape).Should().BeTrue(
            "<x:Visible/> must survive a pure round-trip (regression guard)");

        var d4Shape = FindNoteShape(vmlXml!, row0: 3, col0: 3);
        d4Shape.Should().NotBeNull("D4 shape must also survive a pure round-trip");
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

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 4 fixture: two notes with distinct custom geometry + Visible on C2
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an XLSX package with two legacy notes:
    ///   C2 (row=1,col=2 0-based) — CUSTOM box size (200pt × 120pt) + <x:Visible/> (pinned)
    ///   D4 (row=3,col=3 0-based) — standard geometry
    /// Both have named authors so GAP 1/2/5 tests still pass if reused.
    /// </summary>
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

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", TwoNoteContentTypesXml()),
            ("_rels/.rels", MinimalRootRels()),
            ("xl/workbook.xml", MinimalWorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsWithStyles()),
            ("xl/styles.xml", MinimalStylesXml()),
            ("xl/worksheets/sheet1.xml", MinimalWorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", TwoNoteGeometryVmlDrawing()));
    }

    /// <summary>
    /// VML with two note shapes:
    ///   Shape 1 → C2 (row=1,col=2) — custom 200pt×120pt, pinned visible
    ///   Shape 2 → D4 (row=3,col=3) — standard 108pt×59.25pt, hidden
    /// </summary>
    private static string TwoNoteGeometryVmlDrawing() => """
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

    // ─────────────────────────────────────────────────────────────────────────
    // VML inspection helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    /// <summary>
    /// Finds the <c>&lt;v:shape&gt;</c> element whose ClientData has the given
    /// 0-based row and column in the VML XML document.
    /// </summary>
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

    /// <summary>Returns true when the shape's ClientData contains an <c>&lt;x:Visible/&gt;</c> element.</summary>
    private static bool HasVisible(XElement shape) =>
        shape.Elements(ExcelVmlNs + "ClientData")
            .Any(cd => cd.Element(ExcelVmlNs + "Visible") is not null);

    /// <summary>
    /// Loads the VML drawing file referenced by the worksheet's <c>&lt;legacyDrawing r:id="..."/&gt;</c>
    /// element via the worksheet's relationship file. Returns null if not found.
    /// This avoids finding the wrong VML when multiple VML files exist in the archive.
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

        // Resolve relative target to absolute package path.
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
