using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> as Rich Text Format (<c>.rtf</c>). Emits a
/// <c>\rtf1\ansi\ansicpg1252</c> header with a deterministic, SORTED <c>\fonttbl</c> and <c>\colortbl</c>,
/// then walks the model body (paragraphs and tables) mapping run/paragraph formatting to the corresponding
/// control words. The output is deterministic — two writes of the same model produce byte-identical RTF —
/// so it round-trips cleanly with <see cref="RtfReader"/>.
///
/// <para>
/// Scope is intentionally the FreeW-modelled subset (per the file-format design doc §5.3): character
/// formatting (<c>\b \i \ul \strike \fsN \cfN \fN</c>, super/subscript), paragraph formatting
/// (alignment, indents, spacing) and tables (<c>\trowd \cellxN \cell \row</c>, incl. nested tables).
/// Non-ASCII characters are written byte-exact via <c>\uN</c> with a single ASCII fallback char and the
/// default <c>\uc1</c> skip count. Exotic constructs FreeW does not model are not emitted.
/// </para>
/// </summary>
public static class RtfWriter
{
    public static void Write(TextDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        // Collect the font and colour tables first so the header can be emitted with stable, sorted indexes.
        var fonts = new FontTable();
        var colors = new ColorTable();
        foreach (var block in document.Blocks)
            CollectBlock(block, fonts, colors);

        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0");
        WriteFontTable(sb, fonts);
        WriteColorTable(sb, colors);

        // \uc1: every \uN Unicode escape is followed by exactly one ASCII fallback byte.
        sb.Append(@"\uc1");

        foreach (var block in document.Blocks)
            WriteBlock(sb, block, fonts, colors);

        sb.Append('}');

        // RTF is a 7-bit ASCII container; all non-ASCII has already been escaped to \uN / \'XX.
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(bytes, 0, bytes.Length);
    }

    // ---- table collection -------------------------------------------------------------------------------

    private static void CollectBlock(Block block, FontTable fonts, ColorTable colors)
    {
        switch (block)
        {
            case Paragraph paragraph:
                CollectParagraph(paragraph, fonts, colors);
                break;
            case Table table:
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var p in cell.Paragraphs)
                            CollectParagraph(p, fonts, colors);
                break;
        }
    }

    private static void CollectParagraph(Paragraph paragraph, FontTable fonts, ColorTable colors)
    {
        foreach (var run in paragraph.Runs)
        {
            var f = run.Formatting;
            if (!string.IsNullOrEmpty(f.FontFamily))
                fonts.Intern(f.FontFamily);
            if (!string.IsNullOrEmpty(f.ColorHex))
                colors.Intern(f.ColorHex);
        }
    }

    // ---- header tables ----------------------------------------------------------------------------------

    private static void WriteFontTable(StringBuilder sb, FontTable fonts)
    {
        sb.Append(@"{\fonttbl");
        foreach (var (name, index) in fonts.Ordered())
        {
            sb.Append(@"{\f").Append(index.ToString(CultureInfo.InvariantCulture));
            sb.Append(@"\fnil ");
            AppendEscaped(sb, name);
            sb.Append(";}");
        }
        sb.Append('}');
    }

    private static void WriteColorTable(StringBuilder sb, ColorTable colors)
    {
        // Index 0 is always the implicit "auto" colour (an empty entry: ";").
        sb.Append(@"{\colortbl;");
        foreach (var (r, g, b) in colors.Ordered())
        {
            sb.Append(@"\red").Append(r.ToString(CultureInfo.InvariantCulture));
            sb.Append(@"\green").Append(g.ToString(CultureInfo.InvariantCulture));
            sb.Append(@"\blue").Append(b.ToString(CultureInfo.InvariantCulture));
            sb.Append(';');
        }
        sb.Append('}');
    }

    // ---- body -------------------------------------------------------------------------------------------

    private static void WriteBlock(StringBuilder sb, Block block, FontTable fonts, ColorTable colors)
    {
        switch (block)
        {
            case Paragraph paragraph:
                WriteParagraph(sb, paragraph, fonts, colors);
                break;
            case Table table:
                WriteTable(sb, table, fonts, colors);
                break;
        }
    }

    private static void WriteParagraph(StringBuilder sb, Paragraph paragraph, FontTable fonts, ColorTable colors)
    {
        sb.Append(@"\pard");
        WriteParagraphProperties(sb, paragraph.Formatting);
        WriteRuns(sb, paragraph.Runs, fonts, colors);
        sb.Append(@"\par");
        sb.Append('\n');
    }

    private static void WriteParagraphProperties(StringBuilder sb, ParagraphFormatting f)
    {
        switch (f.Alignment)
        {
            case TextAlignment.Center: sb.Append(@"\qc"); break;
            case TextAlignment.Right: sb.Append(@"\qr"); break;
            case TextAlignment.Justify: sb.Append(@"\qj"); break;
            default: sb.Append(@"\ql"); break;
        }

        // Indents and spacing in twips (points x 20).
        AppendTwipControl(sb, @"\li", f.IndentLeftPt);
        AppendTwipControl(sb, @"\ri", f.IndentRightPt);
        AppendTwipControl(sb, @"\fi", f.FirstLineIndentPt);
        AppendTwipControl(sb, @"\sb", f.SpaceBeforePt);
        AppendTwipControl(sb, @"\sa", f.SpaceAfterPt);
    }

    private static void WriteRuns(StringBuilder sb, IReadOnlyList<Run> runs, FontTable fonts, ColorTable colors)
    {
        foreach (var run in runs)
        {
            if (run.IsPageBreak)
            {
                sb.Append(@"\page ");
                continue;
            }
            if (run.Text.Length == 0)
                continue;

            sb.Append('{');
            var beforeProps = sb.Length;
            WriteRunProperties(sb, run.Formatting, fonts, colors);
            // A trailing space ends the last control word so the text is not glued onto it — but ONLY when a
            // run-property control word was actually emitted; otherwise the space would be literal leading text.
            if (sb.Length > beforeProps)
                sb.Append(' ');
            AppendEscaped(sb, run.Text);
            sb.Append('}');
        }
    }

    private static void WriteRunProperties(StringBuilder sb, RunFormatting f, FontTable fonts, ColorTable colors)
    {
        if (!string.IsNullOrEmpty(f.FontFamily))
            sb.Append(@"\f").Append(fonts.IndexOf(f.FontFamily).ToString(CultureInfo.InvariantCulture));
        if (f.FontSizePt.HasValue)
            // Half-points: \fsN where N = pt x 2.
            sb.Append(@"\fs").Append(((int)Math.Round(f.FontSizePt.Value * 2)).ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(f.ColorHex))
            sb.Append(@"\cf").Append(colors.IndexOf(f.ColorHex).ToString(CultureInfo.InvariantCulture));
        if (f.Bold)
            sb.Append(@"\b");
        if (f.Italic)
            sb.Append(@"\i");
        if (f.Underline)
            sb.Append(@"\ul");
        if (f.Strikethrough)
            sb.Append(@"\strike");
        switch (f.VerticalAlign)
        {
            case VerticalAlign.Superscript: sb.Append(@"\super"); break;
            case VerticalAlign.Subscript: sb.Append(@"\sub"); break;
        }
    }

    // ---- tables ----------------------------------------------------------------------------------------

    private static void WriteTable(StringBuilder sb, Table table, FontTable fonts, ColorTable colors)
    {
        foreach (var row in table.Rows)
            WriteTableRow(sb, table, row, fonts, colors);
    }

    private static void WriteTableRow(StringBuilder sb, Table table, TableRow row, FontTable fonts, ColorTable colors)
    {
        // Compute cumulative cell boundaries (\cellxN) in twips. Use the explicit grid when present,
        // otherwise fall back to a uniform division of a default 6-inch (8640 twip) text width.
        var boundaries = ComputeCellBoundaries(table, row);

        sb.Append(@"\trowd");
        for (var i = 0; i < row.Cells.Count; i++)
        {
            sb.Append(@"\cellx").Append(boundaries[i].ToString(CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < row.Cells.Count; i++)
        {
            var cell = row.Cells[i];
            WriteCellContent(sb, cell, fonts, colors);
            sb.Append(@"\cell");
        }

        sb.Append(@"\row");
        sb.Append('\n');
    }

    private static int[] ComputeCellBoundaries(Table table, TableRow row)
    {
        var count = row.Cells.Count;
        var boundaries = new int[count];

        // Prefer explicit per-cell widths, then the table grid, then a uniform fallback.
        const int defaultRowWidthTwips = 8640; // 6 inches
        var cumulative = 0;
        for (var i = 0; i < count; i++)
        {
            double widthPt;
            if (row.Cells[i].WidthPt is { } w && w > 0)
                widthPt = w;
            else if (i < table.ColumnWidthsPt.Count && table.ColumnWidthsPt[i] > 0)
                widthPt = table.ColumnWidthsPt[i];
            else
                widthPt = defaultRowWidthTwips / 20.0 / count;

            cumulative += (int)Math.Round(widthPt * 20);
            boundaries[i] = cumulative;
        }
        return boundaries;
    }

    private static void WriteCellContent(StringBuilder sb, TableCell cell, FontTable fonts, ColorTable colors)
    {
        if (cell.Paragraphs.Count == 0)
            return;

        for (var i = 0; i < cell.Paragraphs.Count; i++)
        {
            var paragraph = cell.Paragraphs[i];

            // A nested table inside a cell is modelled here as paragraphs only; FreeW's TableCell holds
            // paragraphs (not blocks), so nested tables live in the body and are written as their own rows.
            sb.Append(@"\pard\intbl");
            WriteParagraphProperties(sb, paragraph.Formatting);
            sb.Append(' ');
            WriteRuns(sb, paragraph.Runs, fonts, colors);
            // Paragraphs inside a cell are separated by \par; the final one is terminated by \cell.
            if (i < cell.Paragraphs.Count - 1)
                sb.Append(@"\par");
        }
    }

    // ---- escaping --------------------------------------------------------------------------------------

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="sb"/> with RTF escaping: the special characters
    /// <c>\ { }</c> are backslash-escaped, ASCII is emitted verbatim, and any non-ASCII character is emitted
    /// as <c>\uN</c> (signed 16-bit) followed by a single <c>?</c> ASCII fallback byte (matching <c>\uc1</c>).
    /// Surrogate pairs are emitted as two <c>\uN</c> escapes so astral characters survive.
    /// </summary>
    private static void AppendEscaped(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                case '\t': sb.Append(@"\tab "); break;
                case '\n': sb.Append(@"\line "); break;
                case '\r': break; // normalise CRLF -> single \line via the \n case
                default:
                    if (ch < 0x80)
                    {
                        sb.Append(ch);
                    }
                    else
                    {
                        // \uN takes a SIGNED 16-bit code unit; values > 32767 are written as negative.
                        int code = ch;
                        if (code > 32767)
                            code -= 65536;
                        sb.Append(@"\u").Append(code.ToString(CultureInfo.InvariantCulture)).Append('?');
                    }
                    break;
            }
        }
    }

    private static void AppendTwipControl(StringBuilder sb, string control, double valuePt)
    {
        if (valuePt == 0)
            return;
        sb.Append(control).Append(((int)Math.Round(valuePt * 20)).ToString(CultureInfo.InvariantCulture));
    }

    // ---- header table helpers --------------------------------------------------------------------------

    /// <summary>Interns font-family names and assigns deterministic, sorted <c>\fN</c> indexes.</summary>
    private sealed class FontTable
    {
        private readonly SortedSet<string> _names = new(StringComparer.Ordinal);
        private Dictionary<string, int>? _indexes;

        public void Intern(string name) => _names.Add(name);

        public IEnumerable<(string Name, int Index)> Ordered()
        {
            Build();
            foreach (var kvp in _indexes!.OrderBy(p => p.Value))
                yield return (kvp.Key, kvp.Value);
        }

        public int IndexOf(string name)
        {
            Build();
            return _indexes!.TryGetValue(name, out var idx) ? idx : 0;
        }

        private void Build()
        {
            if (_indexes is not null)
                return;
            _indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var i = 0;
            foreach (var name in _names)
                _indexes[name] = i++;
            // Guarantee \f0 exists even when no run named a font (deff0 references it).
            if (_indexes.Count == 0)
                _indexes["Calibri"] = 0;
        }
    }

    /// <summary>Interns RRGGBB colours and assigns deterministic, sorted <c>\cfN</c> indexes (index 0 = auto).</summary>
    private sealed class ColorTable
    {
        private readonly SortedSet<(byte R, byte G, byte B)> _colors = new();
        private Dictionary<(byte, byte, byte), int>? _indexes;

        public void Intern(string hex)
        {
            if (TryParse(hex, out var rgb))
                _colors.Add(rgb);
        }

        public IEnumerable<(byte R, byte G, byte B)> Ordered()
        {
            Build();
            // Entries are written after the auto entry (index 0), in sorted order = index 1..N.
            return _colors;
        }

        public int IndexOf(string hex)
        {
            Build();
            return TryParse(hex, out var rgb) && _indexes!.TryGetValue(rgb, out var idx) ? idx : 0;
        }

        private void Build()
        {
            if (_indexes is not null)
                return;
            _indexes = new Dictionary<(byte, byte, byte), int>();
            var i = 1; // index 0 is the implicit auto colour
            foreach (var c in _colors)
                _indexes[c] = i++;
        }

        private static bool TryParse(string hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = default;
            if (string.IsNullOrEmpty(hex))
                return false;
            var s = hex.TrimStart('#');
            if (s.Length != 6)
                return false;
            if (byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                rgb = (r, g, b);
                return true;
            }
            return false;
        }
    }
}
