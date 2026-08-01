using System.Globalization;
using System.Text;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public static class PagePrintTextPlanner
{
    // -----------------------------------------------------------------------
    // Public API: tokenizer
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses an Excel header/footer section string into a sequence of formatted runs.
    /// <para>
    /// Recognised format codes:
    /// <list type="bullet">
    ///   <item><c>&amp;B</c> – toggle bold</item>
    ///   <item><c>&amp;I</c> – toggle italic</item>
    ///   <item><c>&amp;U</c> – toggle underline</item>
    ///   <item><c>&amp;E</c> – toggle double-underline</item>
    ///   <item><c>&amp;S</c> – toggle strikethrough</item>
    ///   <item><c>&amp;"fontname,style"</c> – set font family and optionally bold/italic from style word</item>
    ///   <item><c>&amp;nnn</c> – set font size (1–3 digit number; Excel supports sizes up to 409)</item>
    ///   <item><c>&amp;KRRGGBB</c> – set RGB color (6 hex digits)</item>
    ///   <item><c>&amp;&amp;</c> – literal <c>&amp;</c></item>
    ///   <item><c>&amp;+</c> / <c>&amp;-</c> / <c>&amp;X</c> / <c>&amp;Y</c> – super/subscript (state tracked; no geometry change here)</item>
    ///   <item><c>&amp;P</c> / <c>&amp;[Page]</c> – current page number</item>
    ///   <item><c>&amp;N</c> / <c>&amp;[Pages]</c> – total pages</item>
    ///   <item><c>&amp;D</c> / <c>&amp;[Date]</c> – short date</item>
    ///   <item><c>&amp;T</c> / <c>&amp;[Time]</c> – short time</item>
    ///   <item><c>&amp;F</c> / <c>&amp;[File]</c> – workbook filename</item>
    ///   <item><c>&amp;Z</c> / <c>&amp;[Path]</c> – workbook directory path (trailing backslash / slash)</item>
    ///   <item><c>&amp;A</c> / <c>&amp;[Tab]</c> – sheet name</item>
    ///   <item><c>&amp;G</c> / <c>&amp;[Picture]</c> – picture placeholder (stripped)</item>
    /// </list>
    /// </para>
    /// <para>
    /// If <paramref name="text"/> contains no format codes the result is a single plain run.
    /// The <paramref name="workbookDirectory"/> should be the folder that contains the workbook file
    /// (with a trailing separator), or an empty string when the workbook is unsaved.
    /// </para>
    /// </summary>
    public static IReadOnlyList<HeaderFooterFormattedRun> TokenizeSectionText(
        string? text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        DateTime now)
    {
        var runs = new List<HeaderFooterFormattedRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        // Current formatting state (toggled by codes)
        var bold = false;
        var italic = false;
        var underline = false;
        var doubleUnderline = false;
        var strikethrough = false;
        string? fontName = null;
        double? fontSize = null;
        PresentationRgb? color = null;

        var sb = new StringBuilder();

        void FlushRun()
        {
            if (sb.Length == 0) return;
            runs.Add(new HeaderFooterFormattedRun(
                sb.ToString(),
                bold, italic, underline, doubleUnderline, strikethrough,
                fontName, fontSize, color));
            sb.Clear();
        }

        var i = 0;
        var span = text.AsSpan();

        while (i < span.Length)
        {
            if (span[i] != '&')
            {
                sb.Append(span[i]);
                i++;
                continue;
            }

            // Peek at the character(s) after '&'
            if (i + 1 >= span.Length)
            {
                // Trailing lone '&' — treat as literal
                sb.Append('&');
                i++;
                continue;
            }

            var next = span[i + 1];

            // --- Bracketed tokens: &[...] ---
            if (next == '[')
            {
                var close = span[(i + 2)..].IndexOf(']');
                if (close < 0)
                {
                    // Malformed — pass through as literal
                    sb.Append('&');
                    i++;
                    continue;
                }

                var tokenLen = close; // length of name inside brackets
                var token = span.Slice(i + 2, tokenLen).ToString();
                i += 3 + tokenLen; // skip &, [, name, ]

                // Value tokens don't change formatting state — append directly to current run.
                var expanded = ExpandBracketedToken(token, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
                if (expanded is not null)
                    sb.Append(expanded);

                continue;
            }

            // --- Doubled ampersand ---
            if (next == '&')
            {
                sb.Append('&');
                i += 2;
                continue;
            }

            // --- Font-name code: &"fontname[,style]" ---
            if (next == '"')
            {
                var close = span[(i + 2)..].IndexOf('"');
                if (close < 0)
                {
                    sb.Append('&');
                    i++;
                    continue;
                }

                FlushRun();
                var inner = span.Slice(i + 2, close).ToString();
                ParseFontCode(inner, ref fontName, ref bold, ref italic);
                i += 3 + close; // &, ", inner, "
                continue;
            }

            // --- Color code: &KRRGGBB (6 hex digits) ---
            if (next is 'K' or 'k')
            {
                if (i + 7 < span.Length && IsHex6(span.Slice(i + 2, 6)))
                {
                    FlushRun();
                    var hex = span.Slice(i + 2, 6).ToString();
                    var r = Convert.ToByte(hex[..2], 16);
                    var g = Convert.ToByte(hex.Substring(2, 2), 16);
                    var b = Convert.ToByte(hex.Substring(4, 2), 16);
                    color = new PresentationRgb(r, g, b);
                    i += 8;
                    continue;
                }
                // Malformed — pass through
                sb.Append('&');
                i++;
                continue;
            }

            // --- Single-letter toggle/value codes (case-insensitive) ---
            var code = char.ToUpperInvariant(next);

            switch (code)
            {
                case 'B':
                    FlushRun();
                    bold = !bold;
                    i += 2;
                    continue;
                case 'I':
                    FlushRun();
                    italic = !italic;
                    i += 2;
                    continue;
                case 'U':
                    FlushRun();
                    underline = !underline;
                    i += 2;
                    continue;
                case 'E':
                    FlushRun();
                    doubleUnderline = !doubleUnderline;
                    i += 2;
                    continue;
                case 'S':
                    FlushRun();
                    strikethrough = !strikethrough;
                    i += 2;
                    continue;

                // Value placeholders — do NOT flush; just append to the current run's text.
                case 'P':
                    sb.Append(pageNumber.ToString(CultureInfo.InvariantCulture));
                    i += 2;
                    continue;
                case 'N':
                    sb.Append(totalPages.ToString(CultureInfo.InvariantCulture));
                    i += 2;
                    continue;
                case 'D':
                    sb.Append(now.ToString("d", CultureInfo.CurrentCulture));
                    i += 2;
                    continue;
                case 'T':
                    sb.Append(now.ToString("t", CultureInfo.CurrentCulture));
                    i += 2;
                    continue;
                case 'F':
                    sb.Append(workbookName);
                    i += 2;
                    continue;
                case 'Z':
                    sb.Append(workbookDirectory);
                    i += 2;
                    continue;
                case 'A':
                    sb.Append(sheetName);
                    i += 2;
                    continue;

                // Picture/superscript/subscript — suppress or no-op
                case 'G':
                    i += 2; // strip picture placeholder
                    continue;
                case '+':
                case '-':
                case 'X':
                case 'Y':
                    i += 2; // super/subscript — state not tracked geometrically here
                    continue;

                // Numeric font size: &nnn (Excel allows 1-3 decimal digits, sizes up to 409)
                default:
                    if (char.IsAsciiDigit(next))
                    {
                        // Collect the full contiguous run of digits after '&'.
                        var digitEnd = i + 1;
                        while (digitEnd < span.Length && char.IsAsciiDigit(span[digitEnd]))
                            digitEnd++;

                        var digitStr = span.Slice(i + 1, digitEnd - i - 1).ToString();
                        if (int.TryParse(digitStr, out var size) && size > 0)
                        {
                            FlushRun();
                            fontSize = size;
                        }
                        else
                        {
                            sb.Append('&');
                        }

                        i = digitEnd;
                        continue;
                    }

                    // Unknown code — pass through literally
                    sb.Append('&');
                    i++;
                    continue;
            }
        }

        FlushRun();
        return runs;
    }

    // -----------------------------------------------------------------------
    // Public API: legacy flat-string expansion (backward compatibility)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Expands placeholder tokens in a header/footer section string to their values, stripping
    /// all format codes and picture tokens.  This is the legacy path used by callers that render
    /// the whole section as a single flat string.
    /// <para>
    /// For the new per-run rendering path use <see cref="TokenizeSectionText"/> instead.
    /// </para>
    /// </summary>
    public static string ExpandHeaderFooterText(
        string? text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        DateTime now) =>
        ExpandHeaderFooterText(text, pageNumber, totalPages, workbookName, workbookDirectory: "", sheetName, now);

    /// <summary>
    /// Expands placeholder tokens in a header/footer section string to their values, stripping
    /// all format codes and picture tokens.  <paramref name="workbookDirectory"/> is the
    /// directory that contains the workbook file (trailing separator) and is substituted for the
    /// <c>&amp;Z</c> / <c>&amp;[Path]</c> code; pass an empty string when the workbook is unsaved.
    /// </summary>
    public static string ExpandHeaderFooterText(
        string? text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        DateTime now)
    {
        // Delegate through the tokenizer and concatenate text from all runs.
        // This correctly strips format codes and expands value placeholders.
        var runs = TokenizeSectionText(text, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
        if (runs.Count == 0) return "";
        if (runs.Count == 1) return runs[0].Text;
        var sb = new StringBuilder();
        foreach (var run in runs)
            sb.Append(run.Text);
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Cell text helpers
    // -----------------------------------------------------------------------

    public static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
    {
        if (!IsErrorDisplayText(displayText))
            return displayText;

        return printErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => "",
            WorksheetPrintErrorValue.Dash => "--",
            WorksheetPrintErrorValue.NotAvailable => "#N/A",
            _ => displayText
        };
    }

    public static bool IsErrorDisplayText(string text) =>
        text is "#DIV/0!" or "#VALUE!" or "#REF!" or "#NAME?" or "#NULL!" or "#N/A" or "#NUM!";

    // -----------------------------------------------------------------------
    // Multi-line header/footer sections
    // -----------------------------------------------------------------------

    /// <summary>
    /// Counts how many printed lines a raw header/footer section string produces once a literal line
    /// break (Alt+Enter in Excel's Header/Footer editor, which round-trips as an embedded '\n' —
    /// see XlsxWorksheetPageSetupMapper/XlsxFileAdapter) is taken into account. <see
    /// cref="TokenizeSectionText"/> treats '\n' as an ordinary character and appends it into the
    /// current run's text verbatim, so this counts newlines directly on the raw string rather than
    /// re-tokenizing. Always returns at least 1 (an empty/whitespace-only section still occupies one
    /// printed line, matching Excel and the fixed single-line band height this replaces).
    /// </summary>
    public static int CountSectionLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var count = 1;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\r')
            {
                count++;
                i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
            }
            else if (text[i] == '\n')
            {
                count++;
                i++;
            }
            else
            {
                i++;
            }
        }

        return count;
    }

    /// <summary>
    /// Splits a tokenized run sequence into separate printed lines wherever a run's <see
    /// cref="HeaderFooterFormattedRun.Text"/> contains an embedded line break. <see
    /// cref="TokenizeSectionText"/> never splits on '\n' itself (it treats it as an ordinary
    /// character), so a single run may span several printed lines; callers that draw one line at a
    /// time (PrintRenderer.HeaderFooterDrawing in FreeX.App.Host, WorkbookPdfContentBuilder in
    /// FreeX.App.Services) must call this before laying out baselines, or every line after the first
    /// silently disappears (WPF's <c>FormattedText.MaxLineCount</c> and the portable PDF tier's single
    /// fixed baseline both only ever show the first line — see the R111 multi-line header/footer fix).
    /// A run's formatting (bold/italic/font/color/etc.) carries over unchanged to every line it
    /// produces. "\r\n" and bare "\r" are normalized to "\n" first so every line-break convention
    /// splits identically. Always returns at least one (possibly empty) line.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<HeaderFooterFormattedRun>> SplitRunsIntoLines(
        IReadOnlyList<HeaderFooterFormattedRun> runs)
    {
        var lines = new List<IReadOnlyList<HeaderFooterFormattedRun>>();
        var currentLine = new List<HeaderFooterFormattedRun>();

        foreach (var run in runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            if (run.Text.IndexOf('\n') < 0 && run.Text.IndexOf('\r') < 0)
            {
                currentLine.Add(run);
                continue;
            }

            var normalized = run.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            var segments = normalized.Split('\n');
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length > 0)
                    currentLine.Add(run with { Text = segments[i] });

                if (i < segments.Length - 1)
                {
                    lines.Add(currentLine);
                    currentLine = new List<HeaderFooterFormattedRun>();
                }
            }
        }

        lines.Add(currentLine);
        return lines;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static string? ExpandBracketedToken(
        string token,
        int pageNumber,
        int totalPages,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        DateTime now) =>
        token.ToUpperInvariant() switch
        {
            "PAGE" => pageNumber.ToString(CultureInfo.InvariantCulture),
            "PAGES" => totalPages.ToString(CultureInfo.InvariantCulture),
            "DATE" => now.ToString("d", CultureInfo.CurrentCulture),
            "TIME" => now.ToString("t", CultureInfo.CurrentCulture),
            "FILE" => workbookName,
            "PATH" => workbookDirectory,
            "TAB" => sheetName,
            "PICTURE" => null, // stripped
            _ => null
        };

    private static void ParseFontCode(string inner, ref string? fontName, ref bool bold, ref bool italic)
    {
        // Format: "fontname[,style]" where style may contain Bold, Italic, Regular, etc.
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            fontName = inner.Trim();
            return;
        }

        fontName = inner[..comma].Trim();
        var style = inner[(comma + 1)..].Trim();
        if (string.Equals(style, "Regular", StringComparison.OrdinalIgnoreCase))
        {
            bold = false;
            italic = false;
        }
        else
        {
            if (style.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                bold = true;
            if (style.Contains("Italic", StringComparison.OrdinalIgnoreCase))
                italic = true;
        }
    }

    private static bool IsHex6(ReadOnlySpan<char> s)
    {
        if (s.Length < 6) return false;
        foreach (var c in s[..6])
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
