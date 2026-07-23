using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R75-meta-1 regression coverage: XlsxLegacyCommentPreserver's
/// TryResolveShiftedThreadedCommentAddress same-address fast path
/// ("if (sheet.ThreadedComments.ContainsKey(oldAddress)) return (true, oldAddress);") matched by
/// ADDRESS ONLY, never checking that the thread now sitting at that address is actually the shim's
/// OWN thread. A row/column DELETE can remove the shim's own thread entirely while shifting a
/// completely UNRELATED thread onto the shim's old address -- the buggy fast path would then
/// silently reattach the shim to that unrelated thread instead of purging it (the shim's own thread
/// was, in fact, deleted). The fix cross-checks the surviving thread's stable Id against the shim's
/// own source thread Id before trusting the same-address match, falling through to the existing
/// Id-based search (and correctly finding nothing) when they differ.
///
/// A real legacy note is included on every fixture (Sheet.Comments.Count &gt; 0) so these tests
/// exercise TryBuildReconciledCommentsXml directly -- the Comments.Count == 0 branch's own
/// unrelated-thread gate (SourceCommentsHaveOnlyUnmodeledEntries) is a separate concern.
/// </summary>
public sealed class R75Meta1_LegacyCommentShimUnrelatedThreadTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RealNoteRef = "C2";

    [Fact]
    public void ThreadedCommentShim_RowDeleteShiftsUnrelatedThreadOntoOldAddress_ShimIsPurgedNotReattached()
    {
        // ThreadA (the shim's own thread) lives at B5; ThreadB (unrelated) lives at B6. Only ThreadA
        // has a legacy compatibility shim in comments1.xml.
        using var sourcePackage = CreateTwoThreadFixturePackage(
            threadARef: "B5", threadAId: "{AAAAAAAA-0000-0000-0000-00000000AAAA}",
            threadBRef: "B6", threadBId: "{BBBBBBBB-0000-0000-0000-00000000BBBB}");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");
        sheet.ThreadedComments.Should().HaveCount(2);

        // Act: simulate "delete row 5" the way RowColumnShiftHelpers would -- ThreadA (at row 5) is
        // removed outright, and ThreadB (at row 6) shifts up onto row 5, landing exactly on the
        // shim's old address. The real note at C2 (row 2) is above the deletion point and unaffected.
        var b5 = new CellAddress(sheet.Id, 5, 2);
        var b6 = new CellAddress(sheet.Id, 6, 2);
        sheet.ThreadedComments.Should().ContainKey(b5);
        var threadBComment = sheet.ThreadedComments[b6];
        sheet.ThreadedComments.Remove(b5);
        sheet.ThreadedComments.Remove(b6);
        sheet.ThreadedComments[b5] = threadBComment;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        var entries = commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment").ToList();

        entries.Any(c => c.Attribute("ref")?.Value == "B5").Should().BeFalse(
            "the shim's own thread (ThreadA) was deleted, so its legacy shim must be purged -- " +
            "matching by address alone would wrongly reattach it to the unrelated thread (ThreadB) " +
            "that shifted onto B5");

        // Sibling: the real note (unrelated to any thread) must survive completely unaffected.
        entries.Any(c => c.Attribute("ref")?.Value == RealNoteRef).Should().BeTrue(
            "the real note must round-trip unaffected by the thread shift/deletion");
    }

    [Fact]
    public void ThreadedCommentShim_PureSingleThreadRowShift_StillReanchorsCorrectly_NoRegression()
    {
        // Sibling no-regression case: a single thread (with its shim) shifting to a new address --
        // with nothing else moving onto its old address -- must still re-anchor at the new address.
        using var sourcePackage = CreateTwoThreadFixturePackage(
            threadARef: "B5", threadAId: "{AAAAAAAA-0000-0000-0000-00000000AAAA}",
            threadBRef: null, threadBId: null);
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        sheet.ThreadedComments.Should().ContainSingle();

        var b5 = new CellAddress(sheet.Id, 5, 2);
        var b6 = new CellAddress(sheet.Id, 6, 2);
        var comment = sheet.ThreadedComments[b5];
        sheet.ThreadedComments.Remove(b5);
        sheet.ThreadedComments[b6] = comment;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        var entries = commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment").ToList();

        entries.Any(c => c.Attribute("ref")?.Value == "B6").Should().BeTrue(
            "a pure single-thread shift with nothing else taking its old address must still re-anchor the shim");
        entries.Any(c => c.Attribute("ref")?.Value == "B5").Should().BeFalse(
            "the shim must move with the thread, not remain (duplicated) at the stale old address");
    }

    [Fact]
    public void ThreadedCommentShim_UnshiftedSave_StillPreservesShimAtSameAddress_NoRegression()
    {
        // Sibling no-regression case: an unmodified save (nothing shifted, nothing deleted) must
        // still preserve the shim at its own unchanged address via the same-address fast path.
        using var sourcePackage = CreateTwoThreadFixturePackage(
            threadARef: "B5", threadAId: "{AAAAAAAA-0000-0000-0000-00000000AAAA}",
            threadBRef: "B6", threadBId: "{BBBBBBBB-0000-0000-0000-00000000BBBB}");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsXml = LoadActiveLegacyCommentsXml(archive);
        commentsXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment")
            .Any(c => c.Attribute("ref")?.Value == "B5").Should().BeTrue(
                "an unmodified save must still preserve the shim at its own unchanged address");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Active-part inspection helper (mirrors XlsxLegacyCommentThreadedShimShiftTests).
    // ─────────────────────────────────────────────────────────────────────────

    private static XDocument LoadActiveLegacyCommentsXml(ZipArchive archive)
    {
        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!;
        using var relsStream = relsEntry.Open();
        var relsXml = XDocument.Load(relsStream);
        var packageRelNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
        var target = relsXml.Root!.Elements(packageRelNs + "Relationship")
            .Single(r => (r.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
            .Attribute("Target")!.Value;
        var commentsPath = XlsxPackagePath.ResolveRelationshipTarget("xl/worksheets/sheet1.xml", target);

        using var commentsStream = archive.GetEntry(commentsPath)!.Open();
        return XDocument.Load(commentsStream);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture: comments1.xml has a real note (C2) plus ONE legacy shim (for ThreadA);
    // threadedComments1.xml has one or two live threads. When threadBRef/threadBId are null only
    // ThreadA exists.
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateTwoThreadFixturePackage(
        string threadARef, string threadAId, string? threadBRef, string? threadBId)
    {
        const string personId = "{6B3A1111-0000-0000-0000-000000000002}";

        var authorsXml = """<author>tc={5A2F1234-0000-0000-0000-000000000001}</author><author>Alice</author>""";
        var shimCommentXml = $"""
            <comment ref="{threadARef}" authorId="0"><text><r><t>[Threaded comment]

            Your version of Excel allows you to read this threaded comment.</t></r></text></comment>
            """;
        var realNoteCommentXml = $"""<comment ref="{RealNoteRef}" authorId="1"><text><r><t>Confidential</t></r></text></comment>""";
        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{shimCommentXml}{realNoteCommentXml}</commentList>
            </comments>
            """;

        var threadBElement = threadBRef is not null
            ? $"""
              <threadedComment ref="{threadBRef}" dT="2026-01-01T00:00:00Z" personId="{personId}" id="{threadBId}">
                <text>Second thread</text>
              </threadedComment>
              """
            : "";
        var threadedCommentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ThreadedComments xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <threadedComment ref="{threadARef}" dT="2026-01-01T00:00:00Z" personId="{personId}" id="{threadAId}">
                <text>Please review</text>
              </threadedComment>
              {threadBElement}
            </ThreadedComments>
            """;

        var personXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <personList xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <person displayName="Dana" id="{6B3A1111-0000-0000-0000-000000000002}" userId="Dana" providerId="None"/>
            </personList>
            """;

        var (threadARow0, threadACol0) = ParseA1ZeroBased(threadARef);
        var (realRow0, realCol0) = ParseA1ZeroBased(RealNoteRef);
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
                  <x:Anchor>{threadACol0}, 15, {threadARow0}, 2, {threadACol0 + 2}, 15, {threadARow0 + 4}, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>{threadARow0}</x:Row>
                  <x:Column>{threadACol0}</x:Column>
                </x:ClientData>
              </v:shape>
            """;
        var realNoteShapeXml = $"""
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
            """;
        var vmlXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
            {shimShapeXml}{realNoteShapeXml}
            </xml>
            """;

        var sheetDataRows =
            $"""<row r="{realRow0 + 1}"><c r="{RealNoteRef}" t="inlineStr"><is><t>note</t></is></c></row>""" +
            $"""<row r="{threadARow0 + 1}"><c r="{threadARef}" t="inlineStr"><is><t>a</t></is></c></row>""" +
            (threadBRef is not null
                ? $"""<row r="{ParseA1ZeroBased(threadBRef).Row0 + 1}"><c r="{threadBRef}" t="inlineStr"><is><t>b</t></is></c></row>"""
                : "");

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
