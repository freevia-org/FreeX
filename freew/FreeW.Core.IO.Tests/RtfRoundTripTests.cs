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
    public void Adapter_ExposesRtfOpenSaveFormat()
    {
        IDocumentFileAdapter adapter = new RtfFileAdapter();
        adapter.Formats.Should().ContainSingle();
        adapter.Formats[0].Extension.Should().Be(".rtf");
        adapter.Formats[0].CanOpen.Should().BeTrue();
        adapter.Formats[0].CanSave.Should().BeTrue();
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
}
