using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip test for R22-comments-hyperlinks-1 -- PreserveReconciledVmlDrawing indexed the
/// source VML note shapes by their ON-DISK (pre-shift) cell but looked them up by the model's
/// CURRENT (post-shift) address, so a legacy note whose commented cell moved (row/column insert,
/// delete, sort, or move) since the source package was written lost its custom box geometry back
/// to ClosedXML's default shape on save.
///
/// The fixture keeps a second, UNCHANGED note (A1) alongside the note that shifts (C5 -&gt; C6):
/// this is what a real multi-note workbook looks like, and it keeps
/// TryBuildReconciledCommentsXml's "at least one note still matches its on-disk ref" gate open so
/// the save actually reaches the VML reconciliation path this finding is about, instead of
/// short-circuiting earlier (a separate, pre-existing gap in the comments-XML reconciliation that
/// is out of this finding's scope).
/// </summary>
public sealed class XlsxLegacyNoteAddressShiftRoundTripTests
{
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    [Fact]
    public void RowInsertAboveNote_CustomGeometryAndVisiblePreservedAtShiftedAddress()
    {
        // Arrange: a pristine on-disk source package (the only code path PreserveReconciledVmlDrawing
        // runs on) with two notes -- A1 is a stationary reference note (standard geometry); C5 has
        // CUSTOM geometry (200pt x 120pt) + <x:Visible/> (pinned open) and is the note that shifts.
        using var sourcePackage = CreateFixturePackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var c5 = sheet.Comments.Keys.Single(a => a.Row == 5 && a.Col == 3);
        var c5Text = sheet.Comments[c5];
        var a1 = sheet.Comments.Keys.Single(a => a.Row == 1 && a.Col == 1);

        // Act: simulate "insert a row above row 5" the same way InsertRowsCommand does via
        // RowColumnShiftHelpers.ShiftCommentRowsUp/ShiftCommentSetRowsUp -- C5 moves down to C6
        // while A1 (above the insertion point) stays put, and text/author/pinned-state travel with
        // the moved note unchanged.
        ShiftRowsDown(sheet, shiftFromRow: 5, count: 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the NEW (shifted) address must carry the ORIGINAL custom geometry, not
        // ClosedXML's default box.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var vmlXml = LoadWorksheetReferencedVml(archive, "xl/worksheets/sheet1.xml");
        vmlXml.Should().NotBeNull("a VML drawing must be referenced by the worksheet in the saved package");

        // C5 (row=4,col=2 0-based) shifted to C6 (row=5,col=2 0-based).
        var shiftedShape = FindNoteShape(vmlXml!, row0: 5, col0: 2);
        shiftedShape.Should().NotBeNull("the note's shape must exist at its shifted address (C6)");
        shiftedShape!.Attribute("style")?.Value.Should().Contain("width:200pt",
            "custom width must survive a row insert that shifts the note's address (R22-comments-hyperlinks-1)");
        shiftedShape.Attribute("style")?.Value.Should().Contain("height:120pt",
            "custom height must survive a row insert that shifts the note's address (R22-comments-hyperlinks-1)");
        HasVisible(shiftedShape).Should().BeTrue(
            "<x:Visible/> (pinned) state must survive a row insert that shifts the note's address");

        // The OLD address (row=4,col=2 0-based) must no longer carry a shape -- it moved, it wasn't
        // duplicated.
        var staleShape = FindNoteShape(vmlXml!, row0: 4, col0: 2);
        staleShape.Should().BeNull("the shape must move with the note, not remain at the stale old address");

        // A1 (row=0,col=0 0-based, above the insertion point) must be completely unaffected.
        var stationaryShape = FindNoteShape(vmlXml!, row0: 0, col0: 0);
        stationaryShape.Should().NotBeNull("the stationary note above the insertion point must be untouched");

        // The reloaded model must carry the correct text at the new address too.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var rs = reloaded.GetSheetAt(0);
        var rc6 = rs.Comments.Keys.Single(a => a.Row == 6 && a.Col == 3); // C6
        rs.Comments[rc6].Should().Be(c5Text);
        rs.ShownComments.Should().Contain(rc6, "the pinned state must travel with the note to its new address");

        var ra1 = rs.Comments.Keys.Single(a => a.Row == 1 && a.Col == 1);
        rs.Comments[ra1].Should().Be(sheet.Comments[a1]);
    }

    /// <summary>
    /// Mimics RowColumnShiftHelpers.ShiftCommentRowsUp / ShiftCommentSetRowsUp -- the exact helper
    /// InsertRowsCommand calls on Sheet.Comments/CommentAuthors/ShownComments during a real row
    /// insert -- directly on the model, without a FreeX.Core.Commands dependency: every note whose
    /// row is &gt;= <paramref name="shiftFromRow"/> moves down by <paramref name="count"/> rows.
    /// </summary>
    private static void ShiftRowsDown(Sheet sheet, uint shiftFromRow, uint count)
    {
        var toShift = sheet.Comments.Keys.Where(a => a.Row >= shiftFromRow).ToList();
        foreach (var addr in toShift)
        {
            var text = sheet.Comments[addr];
            sheet.Comments.Remove(addr);
            var hasAuthor = sheet.CommentAuthors.TryGetValue(addr, out var author);
            if (hasAuthor)
                sheet.CommentAuthors.Remove(addr);
            var pinned = sheet.ShownComments.Remove(addr);

            var newAddr = new CellAddress(addr.Sheet, addr.Row + count, addr.Col);
            sheet.Comments[newAddr] = text;
            if (hasAuthor)
                sheet.CommentAuthors[newAddr] = author!;
            if (pinned)
                sheet.ShownComments.Add(newAddr);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture: A1 stationary reference note (standard geometry), C5 with custom geometry +
    // Visible (the note that shifts to C6 in the test).
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateFixturePackage()
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
                  <text><r><t>Stationary reference note</t></r></text>
                </comment>
                <comment ref="C5" authorId="1">
                  <text><r><t>Pinned note that will shift</t></r></text>
                </comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRels()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsWithStyles()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", FixtureVmlDrawing()));
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
        </Types>
        """;

    private static string RootRels() => """
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

    private static string WorkbookRelsWithStyles() => """
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
          <dimension ref="A1:D5"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>ref</t></is></c></row>
            <row r="5"><c r="C5" t="inlineStr"><is><t>shift</t></is></c></row>
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
    /// VML with two note shapes:
    ///   Shape 1 -> A1 (row=0,col=0) -- standard 108pt x 59.25pt, hidden (stationary reference note)
    ///   Shape 2 -> C5 (row=4,col=2) -- custom 200pt x 120pt, pinned visible (the note that shifts)
    /// </summary>
    private static string FixtureVmlDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="_x0000_s1025" type="#_x0000_t202"
                   style="position:absolute;margin-left:10pt;margin-top:2pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden"
                   fillcolor="#ffffe1" o:insetmode="auto">
            <v:fill color2="#ffffe1"/>
            <v:shadow color="black" obscured="t"/>
            <v:path o:connecttype="none"/>
            <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
            <x:ClientData ObjectType="Note">
              <x:MoveWithCells/>
              <x:SizeWithCells/>
              <x:Anchor>0, 15, 0, 2, 2, 15, 2, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>0</x:Row>
              <x:Column>0</x:Column>
            </x:ClientData>
          </v:shape>
          <v:shape id="_x0000_s1026" type="#_x0000_t202"
                   style="position:absolute;margin-left:80pt;margin-top:60pt;width:200pt;height:120pt;z-index:2;visibility:visible"
                   fillcolor="#ffffe1" o:insetmode="auto">
            <v:fill color2="#ffffe1"/>
            <v:shadow color="black" obscured="t"/>
            <v:path o:connecttype="none"/>
            <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
            <x:ClientData ObjectType="Note">
              <x:MoveWithCells/>
              <x:SizeWithCells/>
              <x:Anchor>2, 15, 4, 2, 6, 15, 8, 3</x:Anchor>
              <x:AutoFill>False</x:AutoFill>
              <x:Row>4</x:Row>
              <x:Column>2</x:Column>
              <x:Visible/>
            </x:ClientData>
          </v:shape>
        </xml>
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // VML inspection helpers (mirror XlsxLegacyNoteRoundTripTests).
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

    private static bool HasVisible(XElement shape) =>
        shape.Elements(ExcelVmlNs + "ClientData")
            .Any(cd => cd.Element(ExcelVmlNs + "Visible") is not null);

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
