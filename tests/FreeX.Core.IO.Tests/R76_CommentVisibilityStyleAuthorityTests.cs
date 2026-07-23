using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R76-io-vml-legacy-4-3: the note's shown/pinned state must be read with the VML shape's CSS
/// <c>visibility</c> style as the authoritative signal (matching
/// <c>XlsxLegacyCommentPreserver.ApplyVisibleFlag</c>'s write-side precedence), honoring the
/// ClientData <c>&lt;x:Visible/&gt;</c> element only as a legacy fallback when the shape has no
/// <c>visibility</c> style property at all.
/// </summary>
public sealed class R76_CommentVisibilityStyleAuthorityTests
{
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

    private static string WorksheetXmlA1() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:A1"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>note</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string CommentsXmlForA1() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <authors>
            <author>Alice</author>
          </authors>
          <commentList>
            <comment ref="A1" authorId="0">
              <text><r><t>Some note</t></r></text>
            </comment>
          </commentList>
        </comments>
        """;

    private static string VmlWithShape(string style, bool includeVisibleElement) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="_x0000_s1025" type="#_x0000_t202"
                   style="{style}"
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
              {(includeVisibleElement ? "<x:Visible/>" : "")}
            </x:ClientData>
          </v:shape>
        </xml>
        """;

    private static MemoryStream CreatePackage(string vmlXml) =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlA1()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", CommentsXmlForA1()),
            ("xl/drawings/vmlDrawing1.vml", vmlXml));

    [Fact]
    public void StyleHiddenWithStrayVisibleElement_LoadsAsNotShown_StyleWins()
    {
        // A shape whose CSS says hidden but whose ClientData still carries a stray <x:Visible/> --
        // exactly the disagreement the writer never produces itself but a hand-edited or
        // third-party-written file could contain. The style must win.
        var vml = VmlWithShape(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden",
            includeVisibleElement: true);
        using var package = CreatePackage(vml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        var address = sheet.Comments.Keys.Single();

        sheet.ShownComments.Should().NotContain(address,
            "the shape's CSS visibility:hidden is authoritative even though a stray <x:Visible/> " +
            "is present (R76-io-vml-legacy-4-3)");
    }

    [Fact]
    public void StyleVisibleWithVisibleElement_LoadsAsShown()
    {
        var vml = VmlWithShape(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:visible",
            includeVisibleElement: true);
        using var package = CreatePackage(vml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        var address = sheet.Comments.Keys.Single();

        sheet.ShownComments.Should().Contain(address);
    }

    [Fact]
    public void StyleAbsentVisibilityWithVisibleElement_LoadsAsShown_LegacyFallback()
    {
        // No "visibility:" property at all in the style attribute -- the style signal is absent,
        // so the legacy ClientData <x:Visible/> flag is honored as the fallback.
        var vml = VmlWithShape(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1",
            includeVisibleElement: true);
        using var package = CreatePackage(vml);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        var address = sheet.Comments.Keys.Single();

        sheet.ShownComments.Should().Contain(address,
            "when the shape has no visibility style property, <x:Visible/> is honored as the " +
            "legacy fallback signal (R76-io-vml-legacy-4-3)");
    }

    [Fact]
    public void NormalShownAndHiddenComments_UnchangedRoundTrip_NoRegression()
    {
        // Sibling no-regression case: the ordinary, internally-consistent shown/hidden shapes (as
        // the writer always produces) must keep behaving exactly as before.
        var shownVml = VmlWithShape(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:visible",
            includeVisibleElement: true);
        using var shownPackage = CreatePackage(shownVml);
        var adapter = new XlsxFileAdapter();
        var shownWorkbook = adapter.Load(shownPackage);
        var shownSheet = shownWorkbook.GetSheetAt(0);
        shownSheet.ShownComments.Should().Contain(shownSheet.Comments.Keys.Single());

        var hiddenVml = VmlWithShape(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden",
            includeVisibleElement: false);
        using var hiddenPackage = CreatePackage(hiddenVml);
        var hiddenWorkbook = adapter.Load(hiddenPackage);
        var hiddenSheet = hiddenWorkbook.GetSheetAt(0);
        hiddenSheet.ShownComments.Should().NotContain(hiddenSheet.Comments.Keys.Single());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resave fixture: TWO notes so an unrelated ShownComments edit forces a real
    // full-save VML reconciliation (an untouched model takes a verbatim source-copy fast path
    // that never runs XlsxLegacyCommentPreserver, so the resave check needs its own fixture).
    // ─────────────────────────────────────────────────────────────────────────

    private static string WorksheetXmlA1B2() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <dimension ref="A1:B2"/>
          <sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>note</t></is></c></row>
            <row r="2"><c r="B2" t="inlineStr"><is><t>other</t></is></c></row>
          </sheetData>
          <legacyDrawing r:id="rId2"/>
        </worksheet>
        """;

    private static string CommentsXmlForA1B2() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <authors>
            <author>Alice</author>
          </authors>
          <commentList>
            <comment ref="A1" authorId="0">
              <text><r><t>Some note</t></r></text>
            </comment>
            <comment ref="B2" authorId="0">
              <text><r><t>Other note</t></r></text>
            </comment>
          </commentList>
        </comments>
        """;

    private static string VmlWithTwoShapes(string a1Style, bool a1IncludesVisibleElement) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="_x0000_s1025" type="#_x0000_t202"
                   style="{a1Style}"
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
              {(a1IncludesVisibleElement ? "<x:Visible/>" : "")}
            </x:ClientData>
          </v:shape>
          <v:shape id="_x0000_s1026" type="#_x0000_t202"
                   style="position:absolute;margin-left:120pt;margin-top:20pt;width:108pt;height:59.25pt;z-index:2;visibility:visible"
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
              <x:Visible/>
            </x:ClientData>
          </v:shape>
        </xml>
        """;

    private static MemoryStream CreateTwoNotePackage(string a1Style, bool a1IncludesVisibleElement) =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlA1B2()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", CommentsXmlForA1B2()),
            ("xl/drawings/vmlDrawing1.vml", VmlWithTwoShapes(a1Style, a1IncludesVisibleElement)));

    [Fact]
    public void StyleHiddenWithStrayVisibleElement_ResaveKeepsStyleConsistentState()
    {
        // A1 is the mismatched note under test (style hidden + stray <x:Visible/>, so the
        // corrected reader models it as NOT shown). B2 is a normal shown note that we explicitly
        // unpin -- an edit only representable via the full ClosedXML rebuild + VML reconciliation
        // path (a fully-unmodified model instead takes a verbatim source-copy fast path that never
        // runs XlsxLegacyCommentPreserver, which would trivially "pass" without exercising the fix).
        // The resave must write BOTH A1 signals back out consistent with the style-authoritative
        // state that was modeled (hidden, no stray <x:Visible/>), not re-introduce the disagreement.
        using var package = CreateTwoNotePackage(
            "position:absolute;margin-left:20pt;margin-top:1pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden",
            a1IncludesVisibleElement: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        var a1Address = sheet.Comments.Keys.Single(a => a.Row == 1 && a.Col == 1);
        var b2Address = sheet.Comments.Keys.Single(a => a.Row == 2 && a.Col == 2);
        sheet.ShownComments.Should().NotContain(a1Address);
        sheet.ShownComments.Should().Contain(b2Address);

        // Force a genuine full-save reconciliation by unpinning the unrelated B2 note.
        sheet.ShownComments.Remove(b2Address);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);

        // Resolve the VML part actually wired to the worksheet's <legacyDrawing r:id> (there can be
        // a second, unreferenced ClosedXML-generated VML part left in the package -- the reconciled
        // part is the one reached via the worksheet's own relationship, at the SOURCE's own path).
        var vmlEntry = archive.GetEntry("xl/drawings/vmlDrawing1.vml");
        vmlEntry.Should().NotBeNull();

        using var vmlStream = vmlEntry!.Open();
        var vmlXml = XDocument.Load(vmlStream);

        XNamespace vmlNs = "urn:schemas-microsoft-com:vml";
        XNamespace excelVmlNs = "urn:schemas-microsoft-com:office:excel";
        var a1Shape = vmlXml.Root!.Elements(vmlNs + "shape")
            .Single(shape =>
            {
                var clientData = shape.Elements(excelVmlNs + "ClientData")
                    .FirstOrDefault(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase));
                return clientData is not null &&
                    clientData.Element(excelVmlNs + "Row")?.Value == "0" &&
                    clientData.Element(excelVmlNs + "Column")?.Value == "0";
            });

        var a1Style = a1Shape.Attribute("style")?.Value ?? "";
        a1Style.Should().Contain("visibility:hidden");
        a1Shape.Elements(excelVmlNs + "ClientData").Single()
            .Element(excelVmlNs + "Visible").Should().BeNull(
                "the reconciled shape must drop the stray <x:Visible/> to match the style-authoritative " +
                "(not shown) state that was modeled on load, not reintroduce the source disagreement " +
                "(R76-io-vml-legacy-4-3)");
    }
}
