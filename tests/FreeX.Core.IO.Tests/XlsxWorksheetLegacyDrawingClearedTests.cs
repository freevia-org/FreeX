using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for the cleared-<c>&lt;legacyDrawing&gt;</c> gap in
/// <see cref="XlsxWorksheetMetadataPreserver"/> (<c>ShouldSkipClearedModeledWorksheetBlock</c>).
///
/// The plain worksheet <c>&lt;legacyDrawing&gt;</c> marker points at the VML part holding legacy
/// (VML) cell-comment note geometry AND legacy form-control shape geometry. The metadata preserver
/// restores every retained source worksheet block verbatim when it is missing from the ClosedXML-
/// regenerated target. Before the fix, <c>&lt;legacyDrawing&gt;</c> had no cleared-model gate, so
/// deleting every legacy note on a comment-only sheet still resurrected the marker — keeping a
/// dangling reference to the (now needless) VML part alive and blocking
/// <see cref="XlsxLegacyCommentPreserver"/>'s companion VML purge, which conservatively refuses to
/// remove a VML part still pointed at by a live <c>&lt;legacyDrawing&gt;</c> marker.
///
/// The first test proves the marker is dropped once the model no longer needs it (no comments, no
/// form controls). The second is a no-regression guard: a sheet whose model still owns legacy form
/// controls (but no comments) must keep its <c>&lt;legacyDrawing&gt;</c> marker so the controls stay
/// wired to their VML shape geometry.
/// </summary>
public sealed class XlsxWorksheetLegacyDrawingClearedTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    // ─────────────────────────────────────────────────────────────────────────
    // Comment-only sheet: deleting every note must drop the <legacyDrawing> marker
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllLegacyCommentsDeleted_NoFormControls_LegacyDrawingMarkerNotRestored()
    {
        // Arrange: a sheet with a single real legacy (VML) note and no form controls.
        using var sourcePackage = CreateSingleNotePackage("C2", "Confidential", "Alice");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);
        sheet.Comments.Should().ContainSingle("sanity: the source note loaded into the model");
        sheet.FormControls.Should().BeEmpty("sanity: this sheet has no legacy form controls");

        // Act: delete every note, then save.
        var address = sheet.Comments.Keys.Single();
        sheet.Comments.Remove(address);
        sheet.CommentAuthors.Remove(address);
        sheet.ShownComments.Remove(address);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the metadata preserver must NOT resurrect the worksheet's <legacyDrawing> marker
        // now that nothing in the model needs it.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var wsEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        wsEntry.Should().NotBeNull();
        XDocument wsXml;
        using (var wsStream = wsEntry!.Open())
            wsXml = XDocument.Load(wsStream);
        wsXml.Root!.Element(MainNs + "legacyDrawing").Should().BeNull(
            "with every note deleted and no form controls, the cleared <legacyDrawing> block must not be restored");

        // And, because the marker is now absent, XlsxLegacyCommentPreserver's companion purge can
        // remove the orphaned VML note part too (the whole point of dropping the dangling marker).
        var leftoverVml = archive.Entries.Any(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));
        leftoverVml.Should().BeFalse(
            "dropping the dangling <legacyDrawing> marker must let the orphaned VML note part be purged");

        // Model must also come back clean on reload.
        var reloaded = adapter.Load(saved.CloneForReload());
        reloaded.GetSheetAt(0).Comments.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // No-regression guard: a form-control sheet with no comments keeps <legacyDrawing>
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormControlsPresent_NoComments_LegacyDrawingMarkerStillRestored()
    {
        // Arrange: a sheet whose model owns a legacy form control (checkbox) and no comments.
        using var sourcePackage = CreateFormControlPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.Sheets[0];
        sheet.FormControls.Should().ContainSingle("sanity: the form control loaded into the model");
        sheet.Comments.Should().BeEmpty("sanity: this sheet has no legacy comments");

        // Act: save without any edits (pure round-trip).
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the <legacyDrawing> marker (the form control's VML shape geometry) must survive —
        // the cleared-model gate must NOT fire when the sheet still owns form controls.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var wsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        XDocument wsXml;
        using (var wsStream = wsEntry.Open())
            wsXml = XDocument.Load(wsStream);

        wsXml.Root!.Element(MainNs + "legacyDrawing").Should().NotBeNull(
            "a sheet that still owns form controls must keep its <legacyDrawing> marker so the controls stay wired to their VML geometry");
        wsXml.Descendants(MainNs + "control").Should().NotBeEmpty(
            "the form control reference itself must round-trip alongside its <legacyDrawing> marker");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // No-regression guard: a legacyDrawing FreeX never modeled must round-trip verbatim
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnmodeledLegacyDrawing_NoCommentsPart_LegacyDrawingMarkerPreservedAcrossEdit()
    {
        // Arrange: a worksheet whose <legacyDrawing> points at an unknown VML shape FreeX never
        // surfaces into the model — there is NO comments part at all, so Sheet.Comments is
        // legitimately empty with nothing deleted (mirrors the generated-worksheet-legacy-drawing-001
        // corpus row). The cleared-model gate must NOT fire here: an empty Sheet.Comments only means
        // "delete every note, drop the marker" when the source sheet actually had modeled notes.
        using var sourcePackage = CreateUnmodeledLegacyDrawingPackage();
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);
        sheet.Comments.Should().BeEmpty("sanity: the unmodeled VML drawing is not a loadable note");
        sheet.FormControls.Should().BeEmpty("sanity: there is no controls block");

        // Act: make an unrelated model edit, then save (matches the corpus retention scenario).
        sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("edit"));
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the unmodeled <legacyDrawing> marker and its VML part must round-trip verbatim.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var wsEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        wsEntry.Should().NotBeNull();
        XDocument wsXml;
        using (var wsStream = wsEntry!.Open())
            wsXml = XDocument.Load(wsStream);
        wsXml.Root!.Element(MainNs + "legacyDrawing").Should().NotBeNull(
            "a <legacyDrawing> FreeX never modeled as comments must be preserved verbatim, not dropped as if its notes were deleted");
        archive.Entries.Any(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the VML part the surviving marker points at must still exist in the saved package");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures — legacy note package (mirrors XlsxLegacyCommentAllDeletedPurgeTests)
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateSingleNotePackage(string reference, string text, string author)
    {
        var (row0, col0) = ParseA1ZeroBased(reference);
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors><author>{SecurityEscape(author)}</author></authors>
              <commentList>
                <comment ref="{reference}" authorId="0"><text><r><t>{SecurityEscape(text)}</t></r></text></comment>
              </commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", NoteContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", VmlNoteDrawing(row0, col0)));
    }

    private static string NoteContentTypesXml() => """
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
          <dimension ref="A1:D4"/>
          <sheetData>
            <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
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

    private static string VmlNoteDrawing(uint row0, uint col0) => $"""
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
              <x:Row>{row0}</x:Row>
              <x:Column>{col0}</x:Column>
            </x:ClientData>
          </v:shape>
        </xml>
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures — unmodeled legacyDrawing package (no comments part at all)
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateUnmodeledLegacyDrawingPackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", UnmodeledContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithVmlOnly()),
            ("xl/drawings/vmlDrawing1.vml", UnmodeledVmlDrawing()));

    private static string UnmodeledContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string SheetRelsWithVmlOnly() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
        </Relationships>
        """;

    private static string UnmodeledVmlDrawing() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <xml xmlns:v="urn:schemas-microsoft-com:vml"
             xmlns:o="urn:schemas-microsoft-com:office:office"
             xmlns:x="urn:schemas-microsoft-com:office:excel">
          <v:shape id="UnmodeledShape" type="#_x0000_t202"
                   style="position:absolute;visibility:hidden" o:insetmode="auto"/>
        </xml>
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures — form-control package (mirrors XlsxFormControlLoadRoundTripTests)
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateFormControlPackage()
    {
        // Start from a valid FreeX-saved package, then graft the form-control parts onto it (the
        // same technique XlsxFormControlLoadRoundTripTests uses).
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(true)); // I4 linked cell

        var baseStream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, baseStream);
        baseStream.Position = 0;

        var result = new MemoryStream();
        baseStream.CopyTo(result);
        result.Position = 0;

        using (var archive = new ZipArchive(result, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntry = archive.Entries.Single(e =>
                e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            var worksheetPath = worksheetEntry.FullName;

            XDocument worksheetXml;
            using (var read = worksheetEntry.Open())
                worksheetXml = XDocument.Load(read);
            var root = worksheetXml.Root!;
            root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
            root.Add(XElement.Parse(
                """
                <legacyDrawing xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                               r:id="rIdVml"/>
                """));
            root.Add(XElement.Parse(
                """
                <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                                     xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                     xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                     xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
                  <mc:Choice Requires="x14">
                    <controls>
                      <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                        <mc:Choice Requires="x14">
                          <control shapeId="1025" r:id="rIdCtrl" name="Check Box 1">
                            <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                              <anchor>
                                <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                                <to><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                              </anchor>
                            </controlPr>
                          </control>
                        </mc:Choice>
                      </mc:AlternateContent>
                    </controls>
                  </mc:Choice>
                </mc:AlternateContent>
                """));
            ReplaceEntry(archive, worksheetPath, worksheetXml);

            XNamespace fcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            var ctrlPropXml = new XDocument(new XElement(fcNs + "formControlPr",
                new XAttribute("objectType", "CheckBox"),
                new XAttribute("checked", "Checked"),
                new XAttribute("lockText", "1"),
                new XAttribute("noThreeD", "1"),
                new XAttribute("fmlaLink", "$I$4")));
            ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

            var vml =
                "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
                "<v:shape id=\"CheckBox1\" type=\"#_x0000_t201\"><x:ClientData ObjectType=\"Checkbox\">" +
                "<x:Anchor>1,0,1,0,3,0,3,0</x:Anchor><x:Checked>1</x:Checked><x:FmlaLink>$I$4</x:FmlaLink>" +
                "</x:ClientData></v:shape></xml>";
            ReplaceRawEntry(archive, "xl/drawings/vmlDrawing1.vml", vml);

            var relsPath = "xl/worksheets/_rels/" + Path.GetFileName(worksheetPath) + ".rels";
            XDocument relsXml;
            var relsEntry = archive.GetEntry(relsPath);
            if (relsEntry is not null)
            {
                using var read = relsEntry.Open();
                relsXml = XDocument.Load(read);
            }
            else
            {
                relsXml = new XDocument(new XElement(PackageRelNs + "Relationships"));
            }
            relsXml.Root!.Add(
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdVml"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                    new XAttribute("Target", "../drawings/vmlDrawing1.vml")),
                new XElement(PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdCtrl"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                    new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
            ReplaceEntry(archive, relsPath, relsXml);

            EnsureFormControlContentTypes(archive);
        }

        result.Position = 0;
        return result;
    }

    private static void EnsureFormControlContentTypes(ZipArchive archive)
    {
        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var entry = archive.GetEntry("[Content_Types].xml")!;
        XDocument xml;
        using (var read = entry.Open())
            xml = XDocument.Load(read);
        var root = xml.Root!;
        if (!root.Elements(ctNs + "Default").Any(d => string.Equals(d.Attribute("Extension")?.Value, "vml", StringComparison.OrdinalIgnoreCase)))
        {
            root.Add(new XElement(ctNs + "Default",
                new XAttribute("Extension", "vml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
        }
        root.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", xml);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void ReplaceEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void ReplaceRawEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string SecurityEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static (uint Row0, uint Col0) ParseA1ZeroBased(string reference)
    {
        var colPart = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var rowPart = reference[colPart.Length..];
        var col = 0u;
        foreach (var ch in colPart)
            col = col * 26 + (uint)(char.ToUpperInvariant(ch) - 'A' + 1);
        return (uint.Parse(rowPart) - 1, col - 1);
    }
}

file static class LegacyDrawingClearedMemoryStreamExtensions
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
