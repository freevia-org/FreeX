using System.IO;
using System.Linq;
using System.Text;

namespace FreeW.Core.IO.Tests;

public class RtfRoundTripTests
{
    private static TextDocument DocOf(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static byte[] Save(TextDocument document)
    {
        var adapter = new RtfFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static TextDocument Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return new RtfFileAdapter().Load(ms);
    }

    private static string[] Lines(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public void RoundTrip_PreservesParagraphText()
    {
        var reloaded = Load(Save(DocOf("First paragraph", "Second paragraph", "Third")));
        Lines(reloaded).Should().Contain("First paragraph");
        Lines(reloaded).Should().Contain("Second paragraph");
        Lines(reloaded).Should().Contain("Third");
    }

    [Fact]
    public void RoundTrip_PreservesNonAscii()
    {
        // Exercises the \uN / code-page escape path — non-ASCII must survive byte-for-byte at the char level.
        var reloaded = Load(Save(DocOf("café — naïve — ☕ — Ωμέγα")));
        Lines(reloaded).Should().Contain("café — naïve — ☕ — Ωμέγα");
    }

    [Fact]
    public void Save_IsDeterministic()
    {
        var document = DocOf("Determinism", "matters");
        Save(document).Should().Equal(Save(document));
    }

    [Fact]
    public void ColumnBreak_RoundTripsAsColumnControl()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(DocumentOps.CreateColumnBreak());

        var bytes = Save(document);
        Encoding.ASCII.GetString(bytes).Should().Contain(@"\column ");
        var paragraph = Load(bytes).Blocks.OfType<Paragraph>().Single();
        paragraph.Runs.Should().ContainSingle(run => run.IsColumnBreak);
        paragraph.Runs.Should().NotContain(run => run.IsPageBreak);
    }

    [Fact]
    public void Adapter_ExposesRtfOpenSaveFormat()
    {
        IDocumentFileAdapter adapter = new RtfFileAdapter();
        adapter.Formats.Should().ContainSingle();
        adapter.Formats[0].Extension.Should().Be(".rtf");
        adapter.Formats[0].CanOpen.Should().BeTrue();
        adapter.Formats[0].CanSave.Should().BeTrue();
    }

    [Fact]
    public void Save_ProducesRtfBytesAndReloadsModelledCompatibilitySubset()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain "));
        paragraph.Runs.Add(new Run("bold", RunFormatting.Default with { Bold = true }));
        document.Blocks.Add(paragraph);
        var table = new Table();
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                new TableCell("A1") { WidthPt = 96 },
                new TableCell("B1") { WidthPt = 144 },
            },
        });
        document.Blocks.Add(table);

        var bytes = Save(document);
        var rtf = Encoding.ASCII.GetString(bytes);
        rtf.Should().StartWith(@"{\rtf1");
        rtf.Should().Contain(@"\b ");
        rtf.Should().Contain(@"\trowd");

        var reloaded = Load(bytes);
        Lines(reloaded).Should().Contain(line => line.Contains("plain", StringComparison.Ordinal) && line.Contains("bold", StringComparison.Ordinal));
        var reloadedTable = reloaded.Blocks.OfType<Table>().Should().ContainSingle().Which;
        reloadedTable.Rows.Should().ContainSingle().Which.Cells.Select(cell => cell.PlainText).Should().Equal("A1", "B1");
    }

    // P9 regression — \uN fallback skip over a control-word must consume the ENTIRE control word, not just 2
    // chars.  Previously ☃\bullet emitted the ☃ char followed by "ullet" because the skip loop only
    // advanced past "\b", leaving "ullet" to be emitted as literal text.
    [Fact]
    public void Unicode_FallbackControlWord_DoesNotLeakTail()
    {
        // \uc1 sets ucskip=1.  RTF \uN takes a DECIMAL code point, so SNOWMAN (U+2603) is 霱 (9731=0x2603).
        // RTF writers that target legacy readers emit a control-word such as \bullet as the single fallback
        // unit.  The import must yield only the unicode char — the fallback control word must NOT appear
        // (even partially) as literal text.  All chars here are 7-bit ASCII so Encoding.ASCII is fine.
        const string rtf = "{\\rtf1\\ansi\\uc1\\u9731\\bullet After}";
        var doc = Load(Encoding.ASCII.GetBytes(rtf));
        var text = string.Concat(doc.Blocks.OfType<Paragraph>().Select(p => p.PlainText));
        // The unicode char U+2603 (snowman ☃) must be present; "ullet" or "bullet" must NOT appear.
        text.Should().Contain("☃");
        text.Should().NotContain("ullet");
        text.Should().Contain("After");
    }

    // R16 regression — \cellxN column widths must survive a save→load round-trip.
    // Previously the RtfReader had no \cellx handler so all per-cell widths were dropped and every column
    // came back as the uniform fallback (equal division of 6-inch default width).
    [Fact]
    public void Table_ColumnWidths_PreservedOnRoundTrip()
    {
        // Build a 3-column table with explicit, non-uniform widths.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        // Widths in points: 100 pt, 150 pt, 50 pt  (total = 300 pt).
        var widths = new[] { 100.0, 150.0, 50.0 };
        foreach (var w in widths)
            row.Cells.Add(new TableCell { WidthPt = w, Paragraphs = { new Paragraph("cell") } });
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));

        var reloadedTable = reloaded.Blocks.OfType<Table>().Single();
        var reloadedRow = reloadedTable.Rows.Single();
        reloadedRow.Cells.Should().HaveCount(3);

        // Allow ±0.1 pt tolerance for twips↔point rounding (1 twip = 0.05 pt).
        for (var i = 0; i < widths.Length; i++)
        {
            reloadedRow.Cells[i].WidthPt.Should().NotBeNull(because: $"column {i} width must be set after round-trip");
            reloadedRow.Cells[i].WidthPt!.Value.Should().BeApproximately(widths[i], precision: 0.1,
                because: $"column {i} width should survive RTF round-trip");
        }
    }

    // sweep88 F2 regression — a table nested inside another table's cell (TableCell.NestedTables) must not
    // be silently dropped from RTF output. DocumentSaveCompatibilityPlanner tells the user RTF "keeps ...
    // tables"; before the fix, RtfWriter.WriteCellContent walked only TableCell.Paragraphs and never read
    // NestedTables, so the nested table's content never reached the .rtf bytes at all -- no error, no
    // warning, just gone.
    [Fact]
    public void Table_NestedTableInCell_ContentIsNotDropped()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var outerCell = new TableCell("OUTER_CELL_TEXT");
        var nestedTable = new Table();
        nestedTable.Rows.Add(new TableRow { Cells = { new TableCell("NESTED_TABLE_SECRET_TEXT") } });
        outerCell.NestedTables.Add(nestedTable);
        var outerTable = new Table();
        outerTable.Rows.Add(new TableRow { Cells = { outerCell } });
        document.Blocks.Add(outerTable);

        var rtf = Encoding.ASCII.GetString(Save(document));

        rtf.Should().Contain("OUTER_CELL_TEXT");
        rtf.Should().Contain("NESTED_TABLE_SECRET_TEXT",
            because: "a table nested inside a cell must still reach the RTF output, not be silently dropped");
    }

    // Sibling no-regression case for the fix above: an ordinary table with NO nested tables must still
    // write exactly the same single \trowd..\row group it always did (no duplicate rows, no extra tables
    // accidentally introduced by the new post-\row nested-table emission loop).
    [Fact]
    public void Table_WithoutNestedTables_StillWritesExactlyOneRow()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        table.Rows.Add(new TableRow
        {
            Cells = { new TableCell("A1"), new TableCell("B1") },
        });
        document.Blocks.Add(table);

        var rtf = Encoding.ASCII.GetString(Save(document));

        CountOccurrences(rtf, @"\trowd").Should().Be(1, because: "a table with no nested tables must still emit exactly one row group");
        CountOccurrences(rtf, @"\row").Should().Be(1);

        var reloaded = Load(Save(document));
        var reloadedTable = reloaded.Blocks.OfType<Table>().Should().ContainSingle().Which;
        reloadedTable.Rows.Should().ContainSingle().Which.Cells.Select(cell => cell.PlainText).Should().Equal("A1", "B1");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    // P10 regression — \'XX hex-byte escapes inside \fonttbl font names must be decoded against the active
    // code page and stored in the font table, not discarded.  A font name containing \'e9 (= é in
    // Windows-1252) was previously truncated at the first escaped byte, so any run using that \fN got an
    // empty/wrong FontFamily.
    [Fact]
    public void FontTable_HexEscapedName_IsDecoded()
    {
        // {\fonttbl{\f0\froman\fcharset0 Caf\'e9;}} — font name "Café" in Windows-1252.
        // A following run uses \f0 and must resolve to "Café".
        const string rtf = @"{\rtf1\ansi\ansicpg1252{\fonttbl{\f0\froman\fcharset0 Caf\'e9;}}{\f0 text}}";
        var doc = Load(Encoding.ASCII.GetBytes(rtf));
        var run = doc.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.FontFamily.Should().Be("Café");
    }

    // P8 regression — \binN must skip exactly N raw bytes without emitting them as text, and must not let
    // stray { } bytes in the binary payload unbalance group nesting and corrupt following text.
    [Fact]
    public void BinaryData_IsSkippedAndSurroundingTextPreserved()
    {
        // The binary payload contains { and } bytes that would corrupt group nesting if parsed as RTF.
        // Surrounding plain text must still import correctly.
        var header = Encoding.ASCII.GetBytes(@"{\rtf1\ansi Before\bin5 ");
        var binary = new byte[] { 0x7B, 0xFF, 0x00, 0x7D, 0x01 }; // { + garbage + }
        var trailer = Encoding.ASCII.GetBytes(" After}");
        var rtf = header.Concat(binary).Concat(trailer).ToArray();

        var doc = Load(rtf);
        var text = string.Concat(doc.Blocks.OfType<Paragraph>().Select(p => p.PlainText));
        text.Should().Contain("Before");
        text.Should().Contain("After");
        // None of the binary payload bytes should appear as text characters.
        text.Should().NotContain("{");
        text.Should().NotContain("}");
    }

    // ---- CC1: list round-trip -----------------------------------------------------------------------

    // CC1 — bullet list: ListKind.Bullet + ListLevel survive RTF save→load; no "•" marker in paragraph text.
    [Fact]
    public void RoundTrip_BulletList_RestoresListKindAndLevel_NoMarkerTextLeaked()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        // Three-item bullet list, two at level 0 and one at level 1.
        var items = new[]
        {
            (Text: "Alpha",   Kind: ListKind.Bullet, Level: 0),
            (Text: "Beta",    Kind: ListKind.Bullet, Level: 1),
            (Text: "Gamma",   Kind: ListKind.Bullet, Level: 0),
        };
        foreach (var (text, kind, level) in items)
            document.Blocks.Add(new Paragraph(text)
            {
                Formatting = new ParagraphFormatting { ListKind = kind, ListLevel = level }
            });

        var reloaded = Load(Save(document));

        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(3, "all three list items must survive the round-trip");

        for (var i = 0; i < items.Length; i++)
        {
            var p = paragraphs[i];
            p.Formatting.ListKind.Should().Be(items[i].Kind,
                $"paragraph {i} must have ListKind.Bullet after round-trip");
            p.Formatting.ListLevel.Should().Be(items[i].Level,
                $"paragraph {i} must have ListLevel {items[i].Level} after round-trip");

            // Ensure no bullet marker text (•, ·, or literal "bullet") leaked into paragraph text.
            var plain = p.PlainText;
            plain.Should().Contain(items[i].Text,
                $"paragraph {i} plain text must contain original content");
            plain.Should().NotContain("•",
                $"bullet marker must not leak into paragraph {i} text");
            plain.Should().NotContain("·",
                $"middle dot must not leak into paragraph {i} text");
        }
    }

    // CC1 — number list: ListKind.Number + ListLevel survive RTF save→load; no "1." marker in text.
    [Fact]
    public void RoundTrip_NumberList_RestoresListKindAndLevel_NoMarkerTextLeaked()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var items = new[]
        {
            (Text: "First",   Kind: ListKind.Number, Level: 0),
            (Text: "Second",  Kind: ListKind.Number, Level: 1),
            (Text: "Third",   Kind: ListKind.Number, Level: 0),
        };
        foreach (var (text, kind, level) in items)
            document.Blocks.Add(new Paragraph(text)
            {
                Formatting = new ParagraphFormatting { ListKind = kind, ListLevel = level }
            });

        var reloaded = Load(Save(document));

        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(3);

        for (var i = 0; i < items.Length; i++)
        {
            var p = paragraphs[i];
            p.Formatting.ListKind.Should().Be(ListKind.Number,
                $"paragraph {i} must have ListKind.Number after round-trip");
            p.Formatting.ListLevel.Should().Be(items[i].Level,
                $"paragraph {i} must have ListLevel {items[i].Level} after round-trip");

            var plain = p.PlainText;
            plain.Should().Contain(items[i].Text);
            // Verify no "1." / "2." numeric markers leaked as literal text.
            plain.Should().NotMatchRegex(@"^\d+\.",
                $"numeric list marker must not prefix paragraph {i} text");
        }
    }

    // CC1 — multi-level list: ListKind.MultiLevel survives RTF round-trip.
    [Fact]
    public void RoundTrip_MultiLevelList_RestoresListKind()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        document.Blocks.Add(new Paragraph("Level0")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.MultiLevel, ListLevel = 0 }
        });
        document.Blocks.Add(new Paragraph("Level1")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.MultiLevel, ListLevel = 1 }
        });
        document.Blocks.Add(new Paragraph("Level2")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.MultiLevel, ListLevel = 2 }
        });

        var reloaded = Load(Save(document));
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(3);

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[0].Formatting.ListLevel.Should().Be(0);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[1].Formatting.ListLevel.Should().Be(1);
        paragraphs[2].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[2].Formatting.ListLevel.Should().Be(2);
    }

    // CC1 — mixed document: bullet + number lists + plain paragraphs interleaved; list identity is correct.
    [Fact]
    public void RoundTrip_MixedListAndPlain_ListIdentityCorrect()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Intro") { Formatting = ParagraphFormatting.Default });
        document.Blocks.Add(new Paragraph("BulletA")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 0 }
        });
        document.Blocks.Add(new Paragraph("NumberA")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Number, ListLevel = 0 }
        });
        document.Blocks.Add(new Paragraph("Outro") { Formatting = ParagraphFormatting.Default });

        var reloaded = Load(Save(document));
        var paragraphs = reloaded.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(4);

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.None, "plain paragraph must not be a list");
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Bullet, "bullet paragraph must round-trip as Bullet");
        paragraphs[2].Formatting.ListKind.Should().Be(ListKind.Number, "number paragraph must round-trip as Number");
        paragraphs[3].Formatting.ListKind.Should().Be(ListKind.None, "trailing plain paragraph must not be a list");
    }

    // ---- CC2: highlight + caps + rtl round-trip -----------------------------------------------------

    // CC2 — yellow highlight survives RTF round-trip.
    [Fact]
    public void RoundTrip_YellowHighlight_Preserved()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("hello", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        document.Blocks.Add(para);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.HighlightColorHex.Should().Be("#FFFF00",
            "yellow highlight must survive RTF round-trip");
    }

    // CC2 — SmallCaps survives RTF round-trip.
    [Fact]
    public void RoundTrip_SmallCaps_Preserved()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("hello", new RunFormatting { SmallCaps = true }));
        document.Blocks.Add(para);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.SmallCaps.Should().BeTrue("SmallCaps must survive RTF round-trip");
        run.Formatting.AllCaps.Should().BeFalse("AllCaps must remain false");
    }

    // CC2 — AllCaps survives RTF round-trip.
    [Fact]
    public void RoundTrip_AllCaps_Preserved()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("hello", new RunFormatting { AllCaps = true }));
        document.Blocks.Add(para);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.AllCaps.Should().BeTrue("AllCaps must survive RTF round-trip");
        run.Formatting.SmallCaps.Should().BeFalse("SmallCaps must remain false");
    }

    // CC2 — RTL run survives RTF round-trip.
    [Fact]
    public void RoundTrip_RunRtl_Preserved()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("مرحبا", new RunFormatting { Rtl = true }));
        document.Blocks.Add(para);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.Rtl.Should().BeTrue("Rtl must survive RTF round-trip");
    }

    // CC2 — yellow highlight + small caps together survive round-trip.
    [Fact]
    public void RoundTrip_HighlightAndSmallCaps_BothPreserved()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("world", new RunFormatting
        {
            HighlightColorHex = "#FFFF00",
            SmallCaps = true
        }));
        document.Blocks.Add(para);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.First();
        run.Formatting.HighlightColorHex.Should().Be("#FFFF00",
            "highlight must survive round-trip alongside SmallCaps");
        run.Formatting.SmallCaps.Should().BeTrue(
            "SmallCaps must survive round-trip alongside highlight");
    }

    // CC2 — run with no highlight/caps must not have those flags set after round-trip (no false positives).
    [Fact]
    public void RoundTrip_PlainRun_NoHighlightOrCaps()
    {
        var reloaded = Load(Save(DocOf("plain text")));
        var run = reloaded.Blocks.OfType<Paragraph>().First().Runs.FirstOrDefault();
        if (run is null) return; // empty paragraph is fine
        run.Formatting.HighlightColorHex.Should().BeNull("plain run must have no highlight");
        run.Formatting.SmallCaps.Should().BeFalse("plain run must have SmallCaps=false");
        run.Formatting.AllCaps.Should().BeFalse("plain run must have AllCaps=false");
        run.Formatting.Rtl.Should().BeFalse("plain run must have Rtl=false");
    }
}
