using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip fidelity tests for per-cell rich-text run sequences via
/// XlsxFileAdapter (load → re-save → reload) and NativeJsonAdapter (save → reload).
///
/// The XLSX round-trip path tests use hand-crafted minimal XLSX packages that
/// contain &lt;is&gt; inline-string cells with &lt;r&gt; run elements.  This
/// avoids depending on the ClosedXML full-save path, which does not know about
/// Sheet.RichTextRuns (Wave 1 does not add ClosedXML support).
/// </summary>
public sealed class XlsxRichTextRunRoundTripTests
{
    // ── Package helpers ──────────────────────────────────────────────────────

    private const string WorkbookNs     = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PackageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string WorkbookRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    /// <summary>
    /// Builds the smallest possible valid XLSX stream that contains one worksheet
    /// with the given worksheet XML body inside &lt;sheetData&gt;.
    /// </summary>
    private static MemoryStream BuildMinimalXlsx(string sheetDataInnerXml)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                {sheetDataInnerXml}
              </sheetData>
            </worksheet>
            """;

        var workbookXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Sheet1" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;

        var workbookRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet1.xml"/>
            </Relationships>
            """;

        var packageRels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml"
                ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """;

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",        contentTypes);
            Write(archive, "_rels/.rels",                packageRels);
            Write(archive, "xl/workbook.xml",            workbookXml);
            Write(archive, "xl/_rels/workbook.xml.rels", workbookRels);
            Write(archive, "xl/worksheets/sheet1.xml",   worksheetXml);
        }

        ms.Position = 0;
        return ms;

        static void Write(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static Workbook LoadXlsx(Stream stream)
    {
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream SaveJson(Workbook workbook)
    {
        var ms = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, ms);
        ms.Position = 0;
        return ms;
    }

    private static Workbook ReloadJson(MemoryStream stream)
    {
        stream.Position = 0;
        return new NativeJsonAdapter().Load(stream);
    }

    private static CellAddress A(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    // ── XLSX load tests (parsing) ────────────────────────────────────────────

    [Fact]
    public void RichRun_SubscriptInlineStr_LoadsFromXlsx()
    {
        // A1 contains "H₂O" as three runs: H | 2(subscript) | O
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>H</t></r>
                  <r><rPr><vertAlign val="subscript"/></rPr><t>2</t></r>
                  <r><t>O</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));
        var runs = sheet.RichTextRuns[A(sheet, 1, 1)];
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("H");
        runs[1].Text.Should().Be("2");
        runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Subscript);
        runs[2].Text.Should().Be("O");
    }

    [Fact]
    public void RichRun_SuperscriptInlineStr_LoadsFromXlsx()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>X</t></r>
                  <r><rPr><vertAlign val="superscript"/></rPr><t>2</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));
        var runs = sheet.RichTextRuns[A(sheet, 1, 1)];
        runs.Should().HaveCount(2);
        runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
    }

    [Fact]
    public void RichRun_BoldAndColorRuns_LoadFromXlsx()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><b/></rPr><t>Hello</t></r>
                  <r><t> </t></r>
                  <r><rPr><color rgb="FFFF0000"/></rPr><t>World</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));
        var runs = sheet.RichTextRuns[A(sheet, 1, 1)];
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("Hello");
        runs[0].Bold.Should().BeTrue();
        runs[2].Text.Should().Be("World");
        runs[2].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(255, 0, 0)));
    }

    [Fact]
    public void RichRun_FontSizeRuns_LoadFromXlsx()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><sz val="18"/></rPr><t>Big</t></r>
                  <r><rPr><sz val="8"/></rPr><t>Small</t></r>
                </is>
              </c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));
        var runs = sheet.RichTextRuns[A(sheet, 1, 1)];
        runs.Should().HaveCount(2);
        runs[0].FontSize.Should().BeApproximately(18.0, 0.01);
        runs[1].FontSize.Should().BeApproximately(8.0, 0.01);
    }

    // ── XLSX round-trip (load → modify another cell → patch-save → reload) ──

    [Fact]
    public void RichRun_SurvivesPatchSaveRoundTrip_WhenAnotherCellModified()
    {
        // Load a workbook that has A1 with rich runs.
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><b/></rPr><t>Bold</t></r>
                  <r><t> text</t></r>
                </is>
              </c>
            </row>
            <row r="2">
              <c r="A2" t="inlineStr"><is><t>plain</t></is></c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        // Verify runs are loaded.
        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));

        // Touch B2 — this creates a change that triggers patch-save on the sheet.
        sheet.SetCell(A(sheet, 2, 2), new NumberValue(42));

        // Patch-save and reload.
        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(2);
        runs[0].Bold.Should().BeTrue();
    }

    [Fact]
    public void RichRun_ClearedOverride_PersistsThroughPatchSave_WhenValueAndStyleAreUnchanged()
    {
        // R61-io-rich-text-runs-6-1 ("an even starker variant"): a whole-cell command
        // (mirrors ApplyStyleCommand.ClearOverriddenRunProperties) clears the stale per-run
        // color override on "World" WITHOUT changing cell.Value or cell.StyleId at all (e.g.
        // the command's whole-cell style diff happened to be a no-op against this cell's
        // existing style). Before the fix, TryGetPatchableValueChanges's skip condition
        // (`!formulaChanged && !valueChanged && !styleChanged`) saw no reason to visit this
        // cell at all -- the RichTextRuns clearing that DID happen in memory was 100%
        // invisible to patch-save, so "World" kept its stale <color rgb="FFFF0000"/> in the
        // saved package forever despite the user's whole-cell command clearing it in memory.
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>Hello </t></r>
                  <r><rPr><color rgb="FFFF0000"/></rPr><t>World</t></r>
                </is>
              </c>
            </row>
            """);

        var adapter  = new XlsxFileAdapter();
        var workbook = adapter.Load(pkg);
        var sheet    = workbook.GetSheetAt(0);
        var addr     = A(sheet, 1, 1);
        var originalStyleId = sheet.GetCell(addr)!.StyleId;

        // Materialize the cell-patch baseline against the freshly-loaded (unedited) state --
        // mirrors what the real app does right after opening a file, before any user edit is
        // possible. Without this, the baseline would lazily materialize on the FIRST save call
        // below (i.e. from the ALREADY-cleared state), making the edit invisible for reasons
        // unrelated to this finding.
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason)
            .Should().BeTrue(prepareBlockReason);

        // Sanity: the stale red override loaded correctly.
        sheet.RichTextRuns.Should().ContainKey(addr);
        sheet.RichTextRuns[addr][1].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(0xFF, 0x00, 0x00)));

        // Clear the per-run override in memory -- cell.Value and cell.StyleId are left exactly
        // as they were.
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Hello ", Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
            new("World", Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null, FontColor: null),
        };

        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var rs       = reloaded.GetSheetAt(0);
        var rAddr    = A(rs, 1, 1);

        rs.GetCell(rAddr)!.StyleId.Should().Be(originalStyleId);
        rs.GetCell(rAddr)!.Value.Should().Be(new TextValue("Hello World"));

        // The key assertion: "World" must NOT still carry the stale red override.
        if (rs.RichTextRuns.TryGetValue(rAddr, out var reloadedRuns))
        {
            reloadedRuns.Should().OnlyContain(r => r.FontColor == null,
                "the cleared per-run color override must not resurrect on reload");
        }
    }

    /// <summary>Sibling no-regression check: a cell whose rich-run overrides genuinely did NOT
    /// change still round-trips its (correct) per-run formatting untouched when a different
    /// cell triggers patch-save -- the new runsChanged detection must not force a spurious
    /// rewrite of a cell nothing happened to.</summary>
    [Fact]
    public void RichRun_UnchangedOverride_StillPersistsThroughPatchSave_WhenAnotherCellModified()
    {
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><t>Hello </t></r>
                  <r><rPr><color rgb="FFFF0000"/></rPr><t>World</t></r>
                </is>
              </c>
            </row>
            <row r="2">
              <c r="A2" t="inlineStr"><is><t>plain</t></is></c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        // Touch a different cell -- A1's own value/style/runs are untouched.
        sheet.SetCell(A(sheet, 2, 1), new TextValue("changed"));

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);
        var       rAddr    = A(rs, 1, 1);

        rs.RichTextRuns.Should().ContainKey(rAddr);
        var runs = rs.RichTextRuns[rAddr];
        runs.Should().HaveCount(2);
        runs[1].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(0xFF, 0x00, 0x00)),
            "a cell whose runs never changed must keep its original formatting");
    }

    // ── NativeJson round-trip ────────────────────────────────────────────────

    [Fact]
    public void RichRun_AllPropertiesSet_SurviveNativeJsonRoundTrip()
    {
        var workbook = new Workbook("RichRunNativeJson");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 3, 3);

        sheet.SetCell(addr, new TextValue("Test"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Te",
                Bold:          true,
                Italic:        true,
                Underline:     true,
                Strikethrough: false,
                FontName:      "Arial",
                FontSize:      14.0,
                FontColor:     CellRunColor.FromRgb(new CellColor(0x12, 0x34, 0x56))),
            new("st",
                Bold:          null,
                Italic:        null,
                Underline:     null,
                Strikethrough: null,
                FontName:      null,
                FontSize:      null,
                FontColor:     null,
                VertAlign:     CellTextRunVertAlign.Subscript),
        };

        using var saved    = SaveJson(workbook);
        var       reloaded = ReloadJson(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 3, 3));
        var runs = rs.RichTextRuns[A(rs, 3, 3)];
        runs.Should().HaveCount(2);

        runs[0].Text.Should().Be("Te");
        runs[0].Bold.Should().BeTrue();
        runs[0].Italic.Should().BeTrue();
        runs[0].Underline.Should().BeTrue();
        runs[0].Strikethrough.Should().BeFalse();
        runs[0].FontName.Should().Be("Arial");
        runs[0].FontSize.Should().BeApproximately(14.0, 0.001);
        runs[0].FontColor.Should().Be(CellRunColor.FromRgb(new CellColor(0x12, 0x34, 0x56)));

        runs[1].Text.Should().Be("st");
        runs[1].Bold.Should().BeNull();
        runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Subscript);
    }

    [Fact]
    public void RichRun_DoubleUnderlineCharsetFamilyScheme_SurviveNativeJsonRoundTrip()
    {
        // R61-io-rich-text-runs-6-2: NativeJsonAdapter's CellTextRunDto previously had no
        // DoubleUnderline/Charset/Family/Scheme fields at all, so a double-underline run
        // (or one with an explicit charset/family/scheme) silently downgraded to a plain
        // single-underline run with those four properties reset to null on reload.
        var workbook = new Workbook("RichRunDoubleUnderlineNativeJson");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 4, 4);

        sheet.SetCell(addr, new TextValue("Total: 500"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Total: 500",
                Bold:            true,
                Italic:          null,
                Underline:       true,
                Strikethrough:   null,
                FontName:        "MS Gothic",
                FontSize:        11.0,
                FontColor:       null,
                VertAlign:       CellTextRunVertAlign.None,
                DoubleUnderline: true,
                Charset:         128,
                Family:          2,
                Scheme:          "minor"),
        };

        using var saved    = SaveJson(workbook);
        var       reloaded = ReloadJson(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 4, 4));
        var run = rs.RichTextRuns[A(rs, 4, 4)].Should().ContainSingle().Subject;

        run.Underline.Should().BeTrue();
        run.DoubleUnderline.Should().BeTrue();
        run.Charset.Should().Be(128);
        run.Family.Should().Be(2);
        run.Scheme.Should().Be("minor");
    }

    [Fact]
    public void RichRun_EmptyRunMap_IsNotPersistedToNativeJson()
    {
        var workbook = new Workbook("RichRunEmpty");
        var sheet    = workbook.AddSheet("Sheet1");
        sheet.SetCell(A(sheet, 1, 1), new TextValue("NoRuns"));
        // RichTextRuns is empty — no entries added.

        using var saved    = SaveJson(workbook);
        var       reloaded = ReloadJson(saved);

        reloaded.GetSheetAt(0).RichTextRuns.Should().BeEmpty();
    }

    [Fact]
    public void RichRun_NullOptionalProperties_RoundTripAsNullInNativeJson()
    {
        var workbook = new Workbook("RichRunNullProps");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("AB"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            // All optional properties are null — only Text is set.
            new("A", null, null, null, null, null, null, null),
            new("B", null, null, null, null, null, null, null),
        };

        using var saved    = SaveJson(workbook);
        var       reloaded = ReloadJson(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(2);
        runs[0].Bold.Should().BeNull();
        runs[0].FontName.Should().BeNull();
        runs[0].FontColor.Should().BeNull();
    }

    [Fact]
    public void RichRun_MultipleRunCells_BothSurviveNativeJsonRoundTrip()
    {
        var workbook = new Workbook("RichRunMultiCell");
        var sheet    = workbook.AddSheet("Sheet1");

        var addrA1 = A(sheet, 1, 1);
        sheet.SetCell(addrA1, new TextValue("AB"));
        sheet.RichTextRuns[addrA1] = new List<CellTextRun>
        {
            new("A", Bold: true,  Italic: null, Underline: null, Strikethrough: null, null, null, null),
            new("B", Bold: false, Italic: null, Underline: null, Strikethrough: null, null, null, null),
        };

        var addrB2 = A(sheet, 2, 2);
        sheet.SetCell(addrB2, new TextValue("X2"));
        sheet.RichTextRuns[addrB2] = new List<CellTextRun>
        {
            new("X", null, null, null, null, null, null, null),
            new("2", null, null, null, null, null, null, null, CellTextRunVertAlign.Superscript),
        };

        using var saved    = SaveJson(workbook);
        var       reloaded = ReloadJson(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        rs.RichTextRuns.Should().ContainKey(A(rs, 2, 2));

        rs.RichTextRuns[A(rs, 1, 1)][0].Bold.Should().BeTrue();
        rs.RichTextRuns[A(rs, 1, 1)][1].Bold.Should().BeFalse();
        rs.RichTextRuns[A(rs, 2, 2)][1].VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
    }

    // ── Full-save (ClosedXML) round-trip ────────────────────────────────────
    // These tests build a brand-new Workbook (no source package), so Save() must
    // go through the ClosedXML full-save path.  Rich runs must survive.

    [Fact]
    public void RichRun_FullSave_SubscriptSurvivesRoundTrip()
    {
        // H₂O: three runs, "2" is subscript.
        var workbook = new Workbook("FullSaveSubscript");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("H2O"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("H",  null, null, null, null, null, null, null),
            new("2",  null, null, null, null, null, null, null, CellTextRunVertAlign.Subscript),
            new("O",  null, null, null, null, null, null, null),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be("H");
        runs[1].Text.Should().Be("2");
        runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Subscript);
        runs[2].Text.Should().Be("O");
    }

    [Fact]
    public void RichRun_FullSave_BoldRedMixedSurvivesRoundTrip()
    {
        // "Hello World": "Hello" bold, " " plain, "World" red.
        var workbook = new Workbook("FullSaveBoldRed");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 2, 3);

        sheet.SetCell(addr, new TextValue("Hello World"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Hello", Bold: true,  Italic: null, Underline: null, Strikethrough: null,
                         FontName: null, FontSize: null,
                         FontColor: null),
            new(" ",     Bold: null,  Italic: null, Underline: null, Strikethrough: null,
                         FontName: null, FontSize: null,
                         FontColor: null),
            new("World", Bold: null,  Italic: null, Underline: null, Strikethrough: null,
                         FontName: null, FontSize: null,
                         FontColor: CellRunColor.FromRgb(new CellColor(255, 0, 0))),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 2, 3));
        var runs = rs.RichTextRuns[A(rs, 2, 3)];
        runs.Should().HaveCount(3);

        runs[0].Text.Should().Be("Hello");
        runs[0].Bold.Should().BeTrue();

        runs[2].Text.Should().Be("World");
        // Color may come back as RGB CellRunColor.FromRgb(255, 0, 0)
        runs[2].FontColor.Should().NotBeNull();
        runs[2].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Rgb);
        runs[2].FontColor!.Value.Rgb.R.Should().Be(255);
        runs[2].FontColor!.Value.Rgb.G.Should().Be(0);
        runs[2].FontColor!.Value.Rgb.B.Should().Be(0);
    }

    [Fact]
    public void RichRun_FullSave_AllFormattingPropertiesSurvive()
    {
        // Single run with every explicit property set.
        var workbook = new Workbook("FullSaveAllProps");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("Test"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Te",
                Bold:          true,
                Italic:        true,
                Underline:     true,
                Strikethrough: false,
                FontName:      "Arial",
                FontSize:      14.0,
                FontColor:     CellRunColor.FromRgb(new CellColor(0x12, 0x34, 0x56))),
            new("st",
                Bold:          null,
                Italic:        null,
                Underline:     null,
                Strikethrough: null,
                FontName:      null,
                FontSize:      null,
                FontColor:     null,
                VertAlign:     CellTextRunVertAlign.Superscript),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(2);

        runs[0].Text.Should().Be("Te");
        runs[0].Bold.Should().BeTrue();
        runs[0].Italic.Should().BeTrue();
        runs[0].Underline.Should().BeTrue();
        // ClosedXML limitation: it does not emit <strike val="0"/> for an explicitly-false
        // strikethrough, so Strikethrough=false round-trips as null (omit element = default).
        // The value is therefore lost on the full-save path but preserved on patch-save.
        runs[0].Strikethrough.Should().BeNull();
        runs[0].FontName.Should().Be("Arial");
        runs[0].FontSize.Should().BeApproximately(14.0, 0.01);
        runs[0].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Rgb);
        runs[0].FontColor!.Value.Rgb.R.Should().Be(0x12);
        runs[0].FontColor!.Value.Rgb.G.Should().Be(0x34);
        runs[0].FontColor!.Value.Rgb.B.Should().Be(0x56);

        runs[1].Text.Should().Be("st");
        runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
    }

    [Fact]
    public void RichRun_FullSave_ThemeColorSurvivesRoundTrip()
    {
        // Theme color (Accent1, index 4) must round-trip as theme reference, not RGB.
        var workbook = new Workbook("FullSaveTheme");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("Color"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Color",
                Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null,
                FontColor: CellRunColor.FromTheme(4)),   // Accent1
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(1);
        runs[0].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Theme);
        runs[0].FontColor!.Value.ThemeIndex.Should().Be(4);
    }

    [Fact]
    public void RichRun_FullSave_MultipleRichCellsSurvive()
    {
        // Two different cells with rich runs on the same sheet.
        var workbook = new Workbook("FullSaveMultiCell");
        var sheet    = workbook.AddSheet("Sheet1");

        var addrA1 = A(sheet, 1, 1);
        sheet.SetCell(addrA1, new TextValue("A1"));
        sheet.RichTextRuns[addrA1] = new List<CellTextRun>
        {
            new("A", Bold: true,  Italic: null, Underline: null, Strikethrough: null, null, null, null),
            new("1", Bold: false, Italic: null, Underline: null, Strikethrough: null, null, null, null),
        };

        var addrB2 = A(sheet, 2, 2);
        sheet.SetCell(addrB2, new TextValue("X2"));
        sheet.RichTextRuns[addrB2] = new List<CellTextRun>
        {
            new("X", null, null, null, null, null, null, null),
            new("2", null, null, null, null, null, null, null, CellTextRunVertAlign.Superscript),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        rs.RichTextRuns.Should().ContainKey(A(rs, 2, 2));
        rs.RichTextRuns[A(rs, 1, 1)][0].Bold.Should().BeTrue();
        // ClosedXML limitation: Bold=false is not emitted as <b val="0"/> — it round-trips as null.
        rs.RichTextRuns[A(rs, 1, 1)][1].Bold.Should().BeNull();
        rs.RichTextRuns[A(rs, 2, 2)][1].VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
    }

    [Fact]
    public void RichRun_LoadedFromXlsx_PatchSaveStillPreservesRuns()
    {
        // Regression: loaded-from-xlsx (patch path) must not be broken by the full-save change.
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><b/></rPr><t>Bold</t></r>
                  <r><t> plain</t></r>
                </is>
              </c>
            </row>
            <row r="2">
              <c r="B2"><v>0</v></c>
            </row>
            """);

        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        // Verify runs are loaded.
        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));

        // Dirty the workbook so patch-save is triggered.
        sheet.SetCell(A(sheet, 3, 3), new NumberValue(99));

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        rs.RichTextRuns.Should().ContainKey(A(rs, 1, 1));
        var runs = rs.RichTextRuns[A(rs, 1, 1)];
        runs.Should().HaveCount(2);
        runs[0].Bold.Should().BeTrue();
        runs[0].Text.Should().Be("Bold");
    }

    // ── BX1 regression: Auto run color must not corrupt to black on full-save ─

    /// <summary>
    /// BX1: A run with <c>CellRunColor.Auto()</c> must not round-trip as opaque black
    /// after a full-save (ClosedXML path).
    ///
    /// Before the fix, <see cref="XlsxFileAdapter"/> mapped Auto to
    /// <c>XLColor.FromArgb(0,0,0,0)</c> (transparent black), which ClosedXML
    /// serialized as <c>&lt;color rgb="00000000"/&gt;</c>.  The reader stripped the
    /// alpha byte and returned <c>CellRunColor.FromRgb(black)</c> — a visible
    /// corruption.  The fix: skip setting FontColor entirely for Auto runs so
    /// ClosedXML emits no &lt;color&gt; element; on reload FontColor is null
    /// (inherit), not black.
    /// </summary>
    [Fact]
    public void RichRun_FullSave_AutoColorDoesNotReloadAsBlack()
    {
        var workbook = new Workbook("FullSaveAutoColor");
        var sheet    = workbook.AddSheet("Sheet1");
        var addr     = A(sheet, 1, 1);

        sheet.SetCell(addr, new TextValue("Auto"));
        sheet.RichTextRuns[addr] = new List<CellTextRun>
        {
            new("Auto",
                Bold: null, Italic: null, Underline: null, Strikethrough: null,
                FontName: null, FontSize: null,
                FontColor: CellRunColor.Auto()),
        };

        using var saved    = SaveXlsx(workbook);
        var       reloaded = LoadXlsx(saved);
        var       rs       = reloaded.GetSheetAt(0);

        // After full-save, the run must exist (ClosedXML emits runs for any cell that
        // was assigned IXLRichText content, even if individual runs lack a color element).
        // The critical assertion: FontColor must NOT be opaque black (the pre-fix corruption).
        // It may come back as null (no color element emitted) — that is the correct outcome.
        if (rs.RichTextRuns.TryGetValue(A(rs, 1, 1), out var runs) && runs.Count > 0)
        {
            var color = runs[0].FontColor;
            if (color is { } c)
            {
                // If ClosedXML did emit a color, it must not be opaque black (the bug).
                var isOpaqueBlack = c.Kind == CellRunColorKind.Rgb
                    && c.Rgb.R == 0 && c.Rgb.G == 0 && c.Rgb.B == 0;
                isOpaqueBlack.Should().BeFalse(
                    "an Auto-color run must not reload as opaque black after full-save (BX1 regression)");
            }
            // color == null means no <color> element was emitted — acceptable, correct behavior.
        }
        // If no runs at all (ClosedXML dropped the single-run cell), that is a separate
        // limitation (BX3) and not what this test guards against.
    }

    /// <summary>
    /// BX1 consistency: a run with Auto color must reload with an equivalent color
    /// from both the patch-save path (which correctly emits <c>&lt;color auto="1"/&gt;</c>)
    /// and the full-save path (which must not emit opaque black).
    ///
    /// Specifically, neither path should produce an opaque-black RGB color, even though
    /// the patch-save path may produce <c>CellRunColor.Auto()</c> while the full-save
    /// path produces <c>FontColor == null</c>.  Both are acceptable non-black results.
    /// </summary>
    [Fact]
    public void RichRun_AutoColor_PatchAndFullSaveBothAvoidOpaqueBlack()
    {
        // Build a package that already has a run with <color auto="1"/> so the patch-save
        // path is exercised (it reuses the source package XML).
        using var pkg = BuildMinimalXlsx("""
            <row r="1">
              <c r="A1" t="inlineStr">
                <is>
                  <r><rPr><color auto="1"/></rPr><t>Auto</t></r>
                  <r><t> normal</t></r>
                </is>
              </c>
            </row>
            <row r="2">
              <c r="B2"><v>0</v></c>
            </row>
            """);

        // ── Patch-save path ──────────────────────────────────────────────────
        var workbook = LoadXlsx(pkg);
        var sheet    = workbook.GetSheetAt(0);

        // Verify the Auto run loaded correctly.
        sheet.RichTextRuns.Should().ContainKey(A(sheet, 1, 1));
        sheet.RichTextRuns[A(sheet, 1, 1)][0].FontColor.Should().Be(CellRunColor.Auto());

        // Dirty sheet → triggers patch-save (source package is modified in-place).
        sheet.SetCell(A(sheet, 3, 3), new NumberValue(1));

        using var patchSaved    = SaveXlsx(workbook);
        var       patchReloaded = LoadXlsx(patchSaved);
        var       patchRs       = patchReloaded.GetSheetAt(0);

        patchRs.RichTextRuns.Should().ContainKey(A(patchRs, 1, 1));
        var patchColor = patchRs.RichTextRuns[A(patchRs, 1, 1)][0].FontColor;
        // Patch-save round-trips Auto as Auto (correct XML preserved).
        patchColor.Should().Be(CellRunColor.Auto(),
            "patch-save must preserve <color auto=\"1\"/> and reload it as CellRunColor.Auto()");

        // ── Full-save path ───────────────────────────────────────────────────
        // Build a brand-new Workbook with an Auto-color run; no source package → full-save.
        var workbook2 = new Workbook("FullSaveAutoColorConsistency");
        var sheet2    = workbook2.AddSheet("Sheet1");
        var addr2     = A(sheet2, 1, 1);

        sheet2.SetCell(addr2, new TextValue("Auto normal"));
        sheet2.RichTextRuns[addr2] = new List<CellTextRun>
        {
            new("Auto",   Bold: null, Italic: null, Underline: null, Strikethrough: null,
                          FontName: null, FontSize: null, FontColor: CellRunColor.Auto()),
            new(" normal", Bold: null, Italic: null, Underline: null, Strikethrough: null,
                           FontName: null, FontSize: null, FontColor: null),
        };

        using var fullSaved    = SaveXlsx(workbook2);
        var       fullReloaded = LoadXlsx(fullSaved);
        var       fullRs       = fullReloaded.GetSheetAt(0);

        // Both paths must agree: no opaque-black color on the Auto run.
        if (fullRs.RichTextRuns.TryGetValue(A(fullRs, 1, 1), out var fullRuns) && fullRuns.Count > 0)
        {
            var fullColor = fullRuns[0].FontColor;
            if (fullColor is { } fc)
            {
                var isOpaqueBlack = fc.Kind == CellRunColorKind.Rgb
                    && fc.Rgb.R == 0 && fc.Rgb.G == 0 && fc.Rgb.B == 0;
                isOpaqueBlack.Should().BeFalse(
                    "full-save must not corrupt Auto color to opaque black (BX1)");
            }
        }
    }
}
