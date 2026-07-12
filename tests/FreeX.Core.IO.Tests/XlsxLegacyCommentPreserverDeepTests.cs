using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-32 deep-review regression coverage for XlsxLegacyCommentPreserver:
///
/// deep-1: when a sheet has BOTH a real Note and a still-active threaded-comment compatibility
/// shim, TryBuildReconciledCommentsXml/PreserveReconciledVmlDrawing must not drop the shim's
/// comments.xml entry/VML shape just because the shim's address is never a key in
/// Sheet.Comments.
///
/// deep-2: PreserveReconciledVmlDrawing's direct (row,col) shape match must verify the matched
/// source shape actually belongs to the comment being processed (not a sibling note whose OLD
/// cell now coincides with this note's NEW cell after a row/column insert), else two adjacent
/// notes swap/cross-contaminate their box geometry.
///
/// deep-3: TryBuildReconciledCommentsXml must have the same shift-aware (text-matching) fallback
/// as its VML sibling, so a rich-text/custom-author note that merely shifted address (row/column
/// insert) is not flattened to a plain-text/default-author ClosedXML regeneration.
/// </summary>
public sealed class XlsxLegacyCommentPreserverDeepTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Resolves the VML entry actually wired to the worksheet via its &lt;legacyDrawing&gt;
    /// marker/relationship — NOT a blind "any .vml part" lookup, because ClosedXML's own
    /// generated VML (a second, unreferenced part) also survives alongside the reconciled one
    /// this preserver writes back at the original source path.
    /// </summary>
    private static ZipArchiveEntry GetActiveVmlEntry(ZipArchive archive)
    {
        var wsEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var wsStream = wsEntry.Open();
        var wsXml = XDocument.Load(wsStream);
        var vmlRelId = wsXml.Root!.Element(MainNs + "legacyDrawing")!.Attribute(RelNs + "id")!.Value;

        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!;
        using var relsStream = relsEntry.Open();
        var relsXml = XDocument.Load(relsStream);
        var target = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => r.Attribute("Id")?.Value == vmlRelId)
            .Attribute("Target")!.Value;
        var vmlPath = "xl/drawings/" + target.Split('/').Last();
        return archive.GetEntry(vmlPath)!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // deep-1 — threaded-comment shim must survive alongside a real Note
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadedCommentShimAndRealNote_UnrelatedSave_ShimSurvives()
    {
        // Arrange: B2 has an active threaded comment (with its legacy comments1.xml/VML shim),
        // C2 has a genuine, independently-authored legacy Note. Both are on the same sheet.
        using var sourcePackage = CreateShimAndNotePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        // Sanity: the shim never surfaces as a Note, but the real note and the thread both do.
        sheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.ThreadedComments.Should().ContainKey(b2);

        // Act: an unrelated edit to the real note forces reconciliation (Comments.Count > 0), not
        // touching the thread at B2 at all.
        var c2 = sheet.Comments.Keys.Single();
        sheet.Comments[c2] = "Confidential v2";
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the shim's legacy comment entry and VML shape must still be present verbatim.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var commentsStream = commentsEntry.Open();
        var commentsXml = XDocument.Load(commentsStream);
        var authors = commentsXml.Root!.Element(MainNs + "authors")!
            .Elements(MainNs + "author").Select(a => a.Value).ToList();
        var shimEntry = commentsXml.Root!.Element(MainNs + "commentList")!
            .Elements(MainNs + "comment")
            .FirstOrDefault(c => c.Attribute("ref")?.Value == "B2");
        shimEntry.Should().NotBeNull(
            "the legacy threaded-comment compatibility shim at B2 must survive an unrelated save (R32-io-hyperlink-comment-deep-1)");
        var shimAuthorId = int.Parse(shimEntry!.Attribute("authorId")!.Value);
        authors[shimAuthorId].Should().StartWith("tc=");

        var vmlEntry = GetActiveVmlEntry(archive);
        using var vmlStream = vmlEntry.Open();
        var vmlXml = XDocument.Load(vmlStream);
        var hasShimShape = vmlXml.Root!.Elements(VmlNs + "shape").Any(shape =>
            shape.Elements(ExcelVmlNs + "ClientData").Any(cd =>
                string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase) &&
                cd.Element(ExcelVmlNs + "Row")?.Value == "1" &&
                cd.Element(ExcelVmlNs + "Column")?.Value == "1"));
        hasShimShape.Should().BeTrue(
            "the shim's VML note shape at B2 (0-based row 1, col 1) must also survive (R32-io-hyperlink-comment-deep-1)");

        // Sibling case: the genuine note's edited text must still round-trip correctly.
        var reloaded = adapter.Load(saved.CloneForReload());
        var rs = reloaded.GetSheetAt(0);
        rs.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential v2");
        rs.ThreadedComments.Should().ContainKey(new CellAddress(rs.Id, 2, 2));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // deep-2 — two adjacent notes must not swap geometry after a row shift
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoAdjacentNotes_RowShift_EachKeepsOwnGeometry()
    {
        // Arrange: A5="Alpha" (small box) and A6="Beta" (large box) — distinctly different VML
        // <v:shape style="..."> geometry so cross-contamination is detectable.
        using var sourcePackage = CreateTwoGeometryNotePackage(
            "A5", "Alpha", "width:133pt;height:77pt",
            "A6", "Beta", "width:245pt;height:111pt");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var a5 = sheet.Comments.Keys.Single(a => a.Row == 5 && a.Col == 1);
        var a6 = sheet.Comments.Keys.Single(a => a.Row == 6 && a.Col == 1);
        sheet.Comments[a5].Should().Be("Alpha");
        sheet.Comments[a6].Should().Be("Beta");

        // Act: simulate "insert one row above row 5" — every comment at row>=5 shifts down by 1
        // (mirrors what RowColumnShiftHelpers.ShiftCommentRowsDown does to Sheet.Comments' keys).
        sheet.Comments.Remove(a5);
        sheet.Comments.Remove(a6);
        var a6New = new CellAddress(sheet.Id, 6, 1);
        var a7New = new CellAddress(sheet.Id, 7, 1);
        sheet.Comments[a6New] = "Alpha";
        sheet.Comments[a7New] = "Beta";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: Alpha's shape (now at A6, 0-based row 5) must keep ITS OWN geometry, not
        // Beta's; Beta's shape (now at A7, 0-based row 6) must keep its own geometry too.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlEntry = GetActiveVmlEntry(archive);
        using var vmlStream = vmlEntry.Open();
        var vmlXml = XDocument.Load(vmlStream);

        string? StyleAt(uint row0) => vmlXml.Root!.Elements(VmlNs + "shape")
            .FirstOrDefault(shape => shape.Elements(ExcelVmlNs + "ClientData").Any(cd =>
                string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase) &&
                cd.Element(ExcelVmlNs + "Row")?.Value == row0.ToString()))
            ?.Attribute("style")?.Value;

        StyleAt(5).Should().Contain("133pt",
            "Alpha's shape must keep its OWN geometry after shifting to A6, not Beta's (R32-io-hyperlink-comment-deep-2)");
        StyleAt(6).Should().Contain("245pt",
            "Beta's shape must keep its OWN geometry after shifting to A7, not fall back to a generic default (R32-io-hyperlink-comment-deep-2)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // deep-3 — rich-text/custom-author note must survive a row shift
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RichTextNoteWithCustomAuthor_RowShift_FormattingAndAuthorSurvive()
    {
        // Arrange: a note at B10 with a bold run + a plain run, authored by "Carol".
        using var sourcePackage = CreateRichTextNotePackage("B10", "Carol");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var b10 = sheet.Comments.Keys.Single();
        sheet.Comments[b10].Should().Be("Important note");
        sheet.CommentAuthors[b10].Should().Be("Carol");

        // Sibling case: an UNSHIFTED rich-text note (same package, untouched) round-trips intact
        // via the pre-existing exact-address match path — prove the new shift-aware pass doesn't
        // regress it by checking a pure resave first.
        using var unshiftedSaved = new MemoryStream();
        adapter.Save(workbook, unshiftedSaved);
        unshiftedSaved.Position = 0;
        AssertRichRunsPreserved(unshiftedSaved, "B10", "Carol");

        // Act: simulate "insert one row above row 10" shifting the note to B11 (text unchanged).
        sheet.Comments.Remove(b10);
        var author = sheet.CommentAuthors[b10];
        sheet.CommentAuthors.Remove(b10);
        var b11 = new CellAddress(sheet.Id, 11, 2);
        sheet.Comments[b11] = "Important note";
        sheet.CommentAuthors[b11] = author;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: B11's entry must still carry the original rich-text runs and author, not a
        // ClosedXML-regenerated plain-text/default-author entry.
        AssertRichRunsPreserved(saved, "B11", "Carol");
    }

    private static void AssertRichRunsPreserved(MemoryStream saved, string expectedRef, string expectedAuthor)
    {
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var stream = commentsEntry.Open();
        var commentsXml = XDocument.Load(stream);
        var authors = commentsXml.Root!.Element(MainNs + "authors")!
            .Elements(MainNs + "author").Select(a => a.Value).ToList();
        var entry = commentsXml.Root!.Element(MainNs + "commentList")!
            .Elements(MainNs + "comment")
            .Single(c => c.Attribute("ref")?.Value == expectedRef);

        var runs = entry.Element(MainNs + "text")!.Elements(MainNs + "r").ToList();
        runs.Should().HaveCount(2,
            $"the note's two formatting runs must survive at {expectedRef} (R32-io-hyperlink-comment-deep-3)");
        runs[0].Element(MainNs + "rPr")?.Element(MainNs + "b").Should().NotBeNull(
            "the bold run must not be flattened to plain text (R32-io-hyperlink-comment-deep-3)");
        runs[0].Element(MainNs + "t")!.Value.Should().Be("Important");
        runs[1].Element(MainNs + "t")!.Value.Should().Be(" note");

        var authorId = int.Parse(entry.Attribute("authorId")!.Value);
        authors[authorId].Should().Be(expectedAuthor,
            "the note's custom author must survive the shift (R32-io-hyperlink-comment-deep-3)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateShimAndNotePackage()
    {
        var authorsXml = """<author>tc={5A2F1234-0000-0000-0000-000000000001}</author><author>Alice</author>""";
        var commentListXml = """
            <comment ref="B2" authorId="0"><text><r><t>[Threaded comment]

            Your version of Excel allows you to read this threaded comment.</t></r></text></comment>
            <comment ref="C2" authorId="1"><text><r><t>Confidential</t></r></text></comment>
            """;
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{commentListXml}</commentList>
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
                  <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>1</x:Row>
                  <x:Column>2</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml(hasThreadedComments: true)),
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

    private static MemoryStream CreateTwoGeometryNotePackage(
        string ref1, string text1, string style1,
        string ref2, string text2, string style2)
    {
        var authorsXml = """<author>Alice</author>""";
        var commentListXml =
            $"""<comment ref="{ref1}" authorId="0"><text><r><t>{text1}</t></r></text></comment>""" +
            $"""<comment ref="{ref2}" authorId="0"><text><r><t>{text2}</t></r></text></comment>""";
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{commentListXml}</commentList>
            </comments>
            """;

        var (row1_0, col1_0) = ParseA1ZeroBased(ref1);
        var (row2_0, col2_0) = ParseA1ZeroBased(ref2);
        var vmlXml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="_x0000_s1025" type="#_x0000_t202"
                       style="position:absolute;margin-left:80pt;margin-top:6pt;{{style1}};z-index:1;visibility:hidden"
                       fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>{{col1_0}}, 15, {{row1_0}}, 2, {{col1_0 + 2}}, 15, {{row1_0 + 4}}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{{row1_0}}</x:Row>
                  <x:Column>{{col1_0}}</x:Column>
                </x:ClientData>
              </v:shape>
              <v:shape id="_x0000_s1026" type="#_x0000_t202"
                       style="position:absolute;margin-left:160pt;margin-top:6pt;{{style2}};z-index:2;visibility:hidden"
                       fillcolor="#ccffcc" o:insetmode="auto">
                <v:fill color2="#ccffcc"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>{{col2_0}}, 15, {{row2_0}}, 2, {{col2_0 + 2}}, 15, {{row2_0 + 4}}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{{row2_0}}</x:Row>
                  <x:Column>{{col2_0}}</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml(hasThreadedComments: false)),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));
    }

    private static MemoryStream CreateRichTextNotePackage(string reference, string author)
    {
        var authorsXml = $"""<author>{author}</author>""";
        var commentListXml = $"""
            <comment ref="{reference}" authorId="0">
              <text>
                <r><rPr><b/><color rgb="FFFF0000"/><sz val="9"/><rFont val="Tahoma"/></rPr><t>Important</t></r>
                <r><rPr><sz val="9"/><rFont val="Tahoma"/></rPr><t> note</t></r>
              </text>
            </comment>
            """;
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{commentListXml}</commentList>
            </comments>
            """;

        var (row0, col0) = ParseA1ZeroBased(reference);
        var vmlXml = $$"""
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
                  <x:Anchor>{{col0}}, 15, {{row0}}, 2, {{col0 + 2}}, 15, {{row0 + 4}}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{{row0}}</x:Row>
                  <x:Column>{{col0}}</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml(hasThreadedComments: false)),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));
    }

    private static (uint Row0, uint Col0) ParseA1ZeroBased(string reference)
    {
        var colPart = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var rowPart = reference[colPart.Length..];
        var col = 0u;
        foreach (var ch in colPart)
            col = col * 26 + (uint)(char.ToUpperInvariant(ch) - 'A' + 1);
        return (uint.Parse(rowPart) - 1, col - 1);
    }

    private static string ContentTypesXml(bool hasThreadedComments)
    {
        var threadedOverrides = hasThreadedComments
            ? """
              <Override PartName="/xl/threadedComments/threadedComment1.xml" ContentType="application/vnd.ms-excel.threadedcomments+xml"/>
              <Override PartName="/xl/persons/person.xml" ContentType="application/vnd.ms-excel.person+xml"/>
              """
            : "";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
              {threadedOverrides}
            </Types>
            """;
    }

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
          <dimension ref="A1:E11"/>
          <sheetData>
            <row r="2"><c r="B2" t="inlineStr"><is><t>review</t></is></c></row>
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

    private static string SheetRelsWithCommentsAndThread() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
          <Relationship Id="rId3" Type="http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" Target="../threadedComments/threadedComment1.xml"/>
        </Relationships>
        """;
}

file static class MemoryStreamCloneExtensions2
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
