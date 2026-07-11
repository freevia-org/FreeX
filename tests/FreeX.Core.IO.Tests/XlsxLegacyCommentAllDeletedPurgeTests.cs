using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-26 regression coverage for XlsxLegacyCommentPreserver (GAP 6):
///
/// Deleting every legacy (VML) note on a sheet must actually purge the note's TEXT from the saved
/// package. ClosedXML writes nothing at the comments/VML paths for a note-free sheet, but
/// XlsxPackageMetadataMerger's CopyUnknownPackageParts/MergeRelationshipParts run BEFORE
/// XlsxLegacyCommentPreserver and unconditionally resurrect the stale source comments.xml (and its
/// VML) with a live relationship, so the deletion never took effect on disk -- a real
/// information-retention bug for a feature (comment deletion) users rely on to actually remove
/// content, not just hide it. The fix always purges the resurrected comments part; it also purges
/// the VML note shape when nothing else on the sheet still needs that VML part (it is left alone,
/// package-valid, when a separate preserver -- XlsxWorksheetMetadataPreserver -- has independently
/// restored the worksheet's &lt;legacyDrawing&gt; marker, which it does unconditionally whenever
/// present in source; that is an adjacent, out-of-scope gap).
///
/// A sibling regression guard also verifies the reconciliation path (some, but not all, notes
/// deleted) is unaffected, and a third guards against over-correction: Excel's legacy
/// threaded-comment compatibility shim legitimately produces Sheet.Comments.Count == 0 with
/// nothing deleted, and must be left alone.
/// </summary>
public sealed class XlsxLegacyCommentAllDeletedPurgeTests
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    // ─────────────────────────────────────────────────────────────────────────
    // GAP 6 – deleting the only note on a sheet must purge it from the saved file
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllNotesDeleted_PurgesStaleCommentTextAndVmlFromSavedPackage()
    {
        // Arrange: a sheet with a single real legacy note.
        using var sourcePackage = CreateSingleNotePackage("C2", "Confidential: do not share", "Alice");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var address = sheet.Comments.Keys.Single();
        sheet.Comments.Remove(address);
        sheet.CommentAuthors.Remove(address);
        sheet.ShownComments.Remove(address);

        // Act.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the deleted note's text must not survive anywhere in the physical package.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase))
            .ToList();
        commentsEntries.Should().BeEmpty(
            "the resurrected comments part must be purged once every note on the sheet is deleted (GAP 6)");

        // No dangling comments-relationship should remain in the worksheet's rels either.
        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        XDocument? relsXml = null;
        if (relsEntry is not null)
        {
            using var relsStream = relsEntry.Open();
            relsXml = XDocument.Load(relsStream);
            var hasCommentsRelationship = relsXml.Root?.Elements(PackageRelNs + "Relationship")
                .Any(r => (r.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
                ?? false;
            hasCommentsRelationship.Should().BeFalse(
                "no relationship should be left pointing at a purged comments part (GAP 6)");
        }

        // VML: the worksheet's own <legacyDrawing> marker is restored verbatim by a separate,
        // unrelated preserver (XlsxWorksheetMetadataPreserver byte-preserves worksheet metadata
        // blocks including <legacyDrawing> whenever present in source, regardless of comment
        // state -- an adjacent gap outside XlsxLegacyCommentPreserver's scope). When that marker
        // is absent, GAP 6's VML purge must have removed the orphaned Note shape; when it is
        // present, the package must still be structurally valid (the relationship it points to
        // must resolve to a part that exists -- never a dangling reference).
        var wsEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        wsEntry.Should().NotBeNull();
        using var wsStream = wsEntry!.Open();
        var wsXml = XDocument.Load(wsStream);
        var hasLegacyDrawingMarker = wsXml.Root?.Element(MainNs + "legacyDrawing") is not null;

        if (!hasLegacyDrawingMarker)
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = entry.Open();
                var vml = XDocument.Load(stream);
                var hasNoteShape = vml.Root?.Elements(VmlNs + "shape")
                    .Any(shape => shape.Elements(ExcelVmlNs + "ClientData")
                        .Any(cd => string.Equals(cd.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase)))
                    ?? false;
                hasNoteShape.Should().BeFalse(
                    $"no leftover Note VML shape should remain in {entry.FullName} once every note is deleted and nothing else needs the VML part (GAP 6)");
            }
        }
        else
        {
            var vmlRelId = wsXml.Root!.Element(MainNs + "legacyDrawing")!.Attribute(RelNs + "id")?.Value;
            var vmlTarget = relsXml?.Root?.Elements(PackageRelNs + "Relationship")
                .FirstOrDefault(r => string.Equals(r.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal))
                ?.Attribute("Target")?.Value;
            vmlTarget.Should().NotBeNullOrWhiteSpace(
                "a surviving <legacyDrawing> marker must still resolve to a relationship (no dangling r:id)");
            var vmlPath = "xl/drawings/" + System.IO.Path.GetFileName(vmlTarget);
            archive.Entries.Any(e => string.Equals(e.FullName, vmlPath, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue("a surviving <legacyDrawing> marker's relationship must resolve to a part that actually exists");
        }

        // The model itself must also come back clean on reload.
        var reloaded = adapter.Load(saved.CloneForReload());
        reloaded.GetSheetAt(0).Comments.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sibling regression guard – partial deletion still reconciles correctly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoNotes_OneDeleted_RemainingNoteTextAndVmlStillPreserved()
    {
        // Arrange: two notes; only one is deleted. This exercises the untouched
        // TryBuildReconciledCommentsXml/PreserveReconciledVmlDrawing path (Comments.Count > 0),
        // proving GAP 6's new branch didn't regress the existing reconciliation behavior.
        using var sourcePackage = CreateTwoNotePackage(
            "C2", "Note to keep", "Alice",
            "D4", "Note to delete", "Bob");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);

        var d4 = sheet.Comments.Keys.Single(a => a.Row == 4 && a.Col == 4);
        sheet.Comments.Remove(d4);
        sheet.CommentAuthors.Remove(d4);

        // Act.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the remaining note's text is still physically present and correct.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        using var stream = commentsEntry.Open();
        var commentsXml = XDocument.Load(stream);
        var texts = commentsXml.Root!
            .Element(MainNs + "commentList")!
            .Elements(MainNs + "comment")
            .Select(c => string.Concat(c.Element(MainNs + "text")?.Descendants(MainNs + "t").Select(t => t.Value) ?? []))
            .ToList();
        texts.Should().ContainSingle().Which.Should().Be("Note to keep",
            "the remaining note must survive verbatim when a sibling note is deleted (no regression)");

        // Reload and confirm the model state matches.
        var reloaded = adapter.Load(saved.CloneForReload());
        var rs = reloaded.GetSheetAt(0);
        rs.Comments.Should().HaveCount(1);
        var rc2 = rs.Comments.Keys.Single(a => a.Row == 2 && a.Col == 3);
        rs.Comments[rc2].Should().Be("Note to keep");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Over-correction guard – a threaded-comment-only shim must never be purged
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadedCommentShimOnly_PureRoundTrip_ShimIsNotPurged()
    {
        // Arrange: Excel writes a legacy comments1.xml/VML "note" shim for every threaded comment
        // (author literally "tc={GUID}", fixed "[Threaded comment]" banner text) purely for
        // backward compatibility with pre-2018 readers. XlsxWorksheetCommentReader deliberately
        // never loads this into Sheet.Comments, so Sheet.Comments.Count == 0 here is NORMAL, not a
        // deletion -- GAP 6's purge must not fire and destroy the compatibility shim.
        using var sourcePackage = CreateSingleNotePackage(
            "C2",
            "[Threaded comment]\n\nYour version of Excel allows you to read this threaded comment.",
            "tc={5A2F1234-0000-0000-0000-000000000001}");
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(sourcePackage);
        var sheet = workbook.GetSheetAt(0);
        sheet.Comments.Should().BeEmpty("the shim must never surface as a Note (pre-existing behavior)");

        // Act: pure round-trip, no edits at all.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Assert: the shim's comments.xml (and its VML) must still be present in the saved package.
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var commentsEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase))
            .ToList();
        commentsEntries.Should().NotBeEmpty(
            "the legacy threaded-comment compatibility shim must survive an unrelated save, not be purged as if it were a deletion");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

    private static MemoryStream CreateSingleNotePackage(string reference, string text, string author) =>
        CreateNotePackage([(reference, text, author)]);

    private static MemoryStream CreateTwoNotePackage(
        string ref1, string text1, string author1,
        string ref2, string text2, string author2) =>
        CreateNotePackage([(ref1, text1, author1), (ref2, text2, author2)]);

    private static MemoryStream CreateNotePackage(IReadOnlyList<(string Reference, string Text, string Author)> notes)
    {
        var authors = notes.Select(n => n.Author).Distinct().ToList();
        var authorsXml = string.Concat(authors.Select(a => $"<author>{SecurityEscape(a)}</author>"));
        var commentListXml = string.Concat(notes.Select(n =>
            $"""<comment ref="{n.Reference}" authorId="{authors.IndexOf(n.Author)}"><text><r><t>{SecurityEscape(n.Text)}</t></r></text></comment>"""));

        var commentsXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors>{authorsXml}</authors>
              <commentList>{commentListXml}</commentList>
            </comments>
            """;

        return XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", ContentTypesXml()),
            ("_rels/.rels", RootRelsXml()),
            ("xl/workbook.xml", WorkbookXml()),
            ("xl/_rels/workbook.xml.rels", WorkbookRelsXml()),
            ("xl/styles.xml", StylesXml()),
            ("xl/worksheets/sheet1.xml", WorksheetXmlWithLegacyDrawing()),
            ("xl/worksheets/_rels/sheet1.xml.rels", SheetRelsWithComments()),
            ("xl/comments1.xml", commentsXml),
            ("xl/drawings/vmlDrawing1.vml", VmlDrawing(notes)));
    }

    private static string SecurityEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

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

    private static string WorksheetXmlWithLegacyDrawing() => """
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

    private static string VmlDrawing(IReadOnlyList<(string Reference, string Text, string Author)> notes)
    {
        var shapes = string.Concat(notes.Select((n, i) =>
        {
            var (row0, col0) = ParseA1ZeroBased(n.Reference);
            return $$"""
                <v:shape id="_x0000_s{{1025 + i}}" type="#_x0000_t202"
                         style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:{{i + 1}};visibility:hidden"
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
                    <x:Row>{{row0}}</x:Row>
                    <x:Column>{{col0}}</x:Column>
                  </x:ClientData>
                </v:shape>
                """;
        }));

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:x="urn:schemas-microsoft-com:office:excel">
              {shapes}
            </xml>
            """;
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

file static class MemoryStreamCloneExtensions
{
    /// <summary>Returns an independent, position-0 copy so a stream already consumed by Save can be reloaded.</summary>
    public static MemoryStream CloneForReload(this MemoryStream source)
    {
        var clone = new MemoryStream(source.ToArray());
        clone.Position = 0;
        return clone;
    }
}
