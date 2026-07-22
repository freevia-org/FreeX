using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R74-io-comments-threaded-4-1 regression coverage: XlsxLegacyCommentPreserver's four
/// shim-preservation lookups (TryBuildReconciledCommentsXml, SourceCommentsHaveOnlyUnmodeledEntries,
/// TryBuildShimsOnlyCommentsXml, PreserveReconciledVmlDrawing) all probed
/// <see cref="Sheet.ThreadedComments"/> using the shim's OWN (source-file) <c>ref</c> address,
/// never accounting for a row/column insert/delete that RowColumnShiftHelpers already used to
/// relocate the thread's key in the model -- silently concluding the thread was deleted and
/// purging its legacy <c>comments1.xml</c> entry/VML note shape even though the thread itself is
/// still alive at its new address.
/// </summary>
public sealed class XlsxLegacyCommentThreadedShimShiftTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ─────────────────────────────────────────────────────────────────────────
    // Branch A: sheet ALSO has a genuine legacy Note (Sheet.Comments.Count > 0) --
    // exercises TryBuildReconciledCommentsXml + PreserveReconciledVmlDrawing.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadedCommentShim_RowShift_PreservedAtNewAddressAlongsideRealNote()
    {
        // Arrange: a real note at C2 (stays put) and a threaded comment + its legacy shim at B5
        // (will shift). Comments.Count > 0 throughout because of the real note.
        using var sourcePackage = CreateFixturePackage(threadRef: "B5", includeRealNote: true);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.ThreadedComments.Should().ContainSingle();
        sheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");

        // Act: simulate "insert one row above row 5" the same way RowColumnShiftHelpers shifts
        // Sheet.ThreadedComments' key -- only the thread (at row 5) moves; the real note (row 2)
        // is above the insertion point and stays put.
        ShiftThreadedCommentsDown(sheet, shiftFromRow: 5, count: 1);
        var b6 = new CellAddress(sheet.Id, 6, 2);
        sheet.ThreadedComments.Should().ContainKey(b6);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        var entries = commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment").ToList();

        entries.Any(c => c.Attribute("ref")?.Value == "B6").Should().BeTrue(
            "the shim must be re-anchored to the thread's shifted address, not dropped as if deleted (R74-io-comments-threaded-4-1)");
        entries.Any(c => c.Attribute("ref")?.Value == "B5").Should().BeFalse(
            "the shim must move with the thread, not remain (duplicated) at the stale old address");

        var vmlXml = LoadActiveVmlXml(archive);
        HasNoteShapeAt(vmlXml, row0: 5, col0: 1).Should().BeTrue(
            "the shim's VML note shape must exist at the shifted address B6 (0-based row 5, col 1)");
        HasNoteShapeAt(vmlXml, row0: 4, col0: 1).Should().BeFalse(
            "no leftover shape may remain at the stale old address B5");

        // Sibling case: the real note must be completely unaffected by the thread's shift.
        var reloaded = adapter.Load(saved.CloneForReload());
        reloaded.GetSheetAt(0).Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");
    }

    [Fact]
    public void ThreadedCommentShim_GenuineThreadDeletion_StillPurgedAlongsideRealNote()
    {
        // Sibling no-regression case: with NO shift at all, actually deleting the whole thread
        // must still purge its shim -- proves the shift-aware fallback does not accidentally
        // rescue a genuinely deleted thread.
        using var sourcePackage = CreateFixturePackage(threadRef: "B5", includeRealNote: true);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.ThreadedComments.Clear();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment")
            .Any(c => c.Attribute("ref")?.Value == "B5").Should().BeFalse(
                "a genuinely deleted thread's shim must still be purged, not resurrected by the shift-aware fallback");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Branch B: the thread is the ONLY thing on the sheet (Sheet.Comments.Count == 0) --
    // exercises SourceCommentsHaveOnlyUnmodeledEntries + TryBuildShimsOnlyCommentsXml +
    // ReconcileShimsOnlyVmlDrawing.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadedCommentShim_OnlyThreadOnSheet_RowShift_PreservedAtNewAddress()
    {
        using var sourcePackage = CreateFixturePackage(threadRef: "B2", includeRealNote: false);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.Comments.Should().BeEmpty();
        sheet.ThreadedComments.Should().ContainSingle();

        ShiftThreadedCommentsDown(sheet, shiftFromRow: 2, count: 1);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.ThreadedComments.Should().ContainKey(b3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment")
            .Any(c => c.Attribute("ref")?.Value == "B3").Should().BeTrue(
                "the shim must survive re-anchored to the thread's shifted address even when it is the ONLY thing on the sheet (R74-io-comments-threaded-4-1)");

        var vmlXml = LoadActiveVmlXml(archive);
        HasNoteShapeAt(vmlXml, row0: 2, col0: 1).Should().BeTrue(
            "the shim's VML note shape must exist at the shifted address B3 (0-based row 2, col 1)");
    }

    [Fact]
    public void ThreadedCommentShim_OnlyThreadOnSheet_GenuineThreadDeletion_FullyPurged()
    {
        // Sibling no-regression case: with the thread the ONLY thing on the sheet, a genuine
        // (unshifted) full deletion must still fully purge the legacy comments part -- the
        // shift-aware fallback must not resurrect it.
        using var sourcePackage = CreateFixturePackage(threadRef: "B2", includeRealNote: false);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.ThreadedComments.Clear();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.Entries.Any(e =>
                e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse(
                "a genuinely deleted thread that was the only thing on the sheet must fully purge the legacy comments part, not resurrect it via the shift-aware fallback");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Model-shift helper (mirrors RowColumnShiftHelpers.ShiftCommentRowsDown/Sets for
    // Sheet.Comments, applied here to Sheet.ThreadedComments).
    // ─────────────────────────────────────────────────────────────────────────

    private static void ShiftThreadedCommentsDown(Sheet sheet, uint shiftFromRow, uint count)
    {
        var toShift = sheet.ThreadedComments.Keys.Where(a => a.Row >= shiftFromRow).ToList();
        foreach (var addr in toShift)
        {
            var comment = sheet.ThreadedComments[addr];
            sheet.ThreadedComments.Remove(addr);
            var newAddr = new CellAddress(addr.Sheet, addr.Row + count, addr.Col);
            sheet.ThreadedComments[newAddr] = comment;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VML / active-part inspection helpers (mirror XlsxLegacyCommentPreserverDeepTests).
    // ─────────────────────────────────────────────────────────────────────────

    private static XDocument LoadActiveVmlXml(ZipArchive archive)
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
        var vmlPath = XlsxPackagePath.ResolveRelationshipTarget("xl/worksheets/sheet1.xml", target);

        using var vmlStream = archive.GetEntry(vmlPath)!.Open();
        return XDocument.Load(vmlStream);
    }

    private static XDocument LoadActiveLegacyCommentsXml(ZipArchive archive)
    {
        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!;
        using var relsStream = relsEntry.Open();
        var relsXml = XDocument.Load(relsStream);
        var target = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Single(r => (r.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
            .Attribute("Target")!.Value;
        var commentsPath = XlsxPackagePath.ResolveRelationshipTarget("xl/worksheets/sheet1.xml", target);

        using var commentsStream = archive.GetEntry(commentsPath)!.Open();
        return XDocument.Load(commentsStream);
    }

    private static bool HasNoteShapeAt(XDocument vmlXml, uint row0, uint col0) =>
        vmlXml.Root!.Elements(VmlNs + "shape").Any(shape =>
            shape.Elements(ExcelVmlNs + "ClientData").Any(cd =>
                string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase) &&
                cd.Element(ExcelVmlNs + "Row")?.Value == row0.ToString() &&
                cd.Element(ExcelVmlNs + "Column")?.Value == col0.ToString()));

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateFixturePackage(string threadRef, bool includeRealNote)
    {
        var (threadRow0, threadCol0) = ParseA1ZeroBased(threadRef);
        const string realNoteRef = "C2";
        var (realRow0, realCol0) = ParseA1ZeroBased(realNoteRef);

        var authorsXml = includeRealNote
            ? """<author>tc={5A2F1234-0000-0000-0000-000000000001}</author><author>Alice</author>"""
            : """<author>tc={5A2F1234-0000-0000-0000-000000000001}</author>""";
        var shimCommentXml = $"""
            <comment ref="{threadRef}" authorId="0"><text><r><t>[Threaded comment]

            Your version of Excel allows you to read this threaded comment.</t></r></text></comment>
            """;
        var realNoteCommentXml = includeRealNote
            ? $"""<comment ref="{realNoteRef}" authorId="1"><text><r><t>Confidential</t></r></text></comment>"""
            : "";
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{shimCommentXml}{realNoteCommentXml}</commentList>
            </comments>
            """;

        const string personId = "{6B3A1111-0000-0000-0000-000000000002}";
        const string threadId = "{7C4B2222-0000-0000-0000-000000000003}";
        var threadedCommentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <threadedComment ref="{threadRef}" dT="2026-01-01T00:00:00Z" personId="{personId}" id="{threadId}">
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

        var shimShapeXml = $"""
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
                  <x:Anchor>{threadCol0}, 15, {threadRow0}, 2, {threadCol0 + 2}, 15, {threadRow0 + 4}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{threadRow0}</x:Row>
                  <x:Column>{threadCol0}</x:Column>
                </x:ClientData>
              </v:shape>
            """;
        var realNoteShapeXml = includeRealNote
            ? $"""
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
                  <x:Anchor>{realCol0}, 15, {realRow0}, 2, {realCol0 + 2}, 15, {realRow0 + 4}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{realRow0}</x:Row>
                  <x:Column>{realCol0}</x:Column>
                </x:ClientData>
              </v:shape>
            """
            : "";
        var vmlXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
            {shimShapeXml}{realNoteShapeXml}
            </xml>
            """;

        var sheetDataRows = includeRealNote
            ? $"""<row r="2"><c r="{realNoteRef}" t="inlineStr"><is><t>note</t></is></c></row><row r="5"><c r="{threadRef}" t="inlineStr"><is><t>review</t></is></c></row>"""
            : $"""<row r="2"><c r="{threadRef}" t="inlineStr"><is><t>review</t></is></c></row>""";

        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <dimension ref="A1:E11"/>
              <sheetData>{sheetDataRows}</sheetData>
              <legacyDrawing r:id="rId2"/>
            </worksheet>
            """;

        var contentTypesXml = """
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

        var rootRelsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Data" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;

        var workbookRelsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """;

        var stylesXml = """
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

        var sheetRelsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
              <Relationship Id="rId3" Type="http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" Target="../threadedComments/threadedComment1.xml"/>
            </Relationships>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", contentTypesXml),
            ("_rels/.rels", rootRelsXml),
            ("xl/workbook.xml", workbookXml),
            ("xl/_rels/workbook.xml.rels", workbookRelsXml),
            ("xl/styles.xml", stylesXml),
            ("xl/worksheets/sheet1.xml", worksheetXml),
            ("xl/worksheets/_rels/sheet1.xml.rels", sheetRelsXml),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", vmlXml),
            ("xl/threadedComments/threadedComment1.xml", threadedCommentsXml),
            ("xl/persons/person.xml", personXml));
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
}

file static class MemoryStreamCloneExtensions3
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
