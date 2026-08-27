using System.Globalization;
using System.Text;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Adapter for Excel's "Formatted Text (Space delimited)" format (<c>.prn</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Save (export):</b> Writes a fixed-width space-padded layout that matches Excel's .prn output
/// semantics as closely as is practical given that FreeX cells carry raw values rather than
/// formatted display strings:
/// <list type="bullet">
///   <item>Only the first/active sheet is exported (Excel .prn is always single-sheet).</item>
///   <item>
///     Column widths are determined by a two-pass algorithm: first pass collects the display text
///     for every cell in the used range and computes the maximum display-text length per column;
///     second pass writes each row with every cell padded (or truncated) to that column's width,
///     columns separated by a single space character.
///     <br/>
///     <b>Simplification note:</b> Excel's own .prn column widths are driven by the worksheet
///     column-width settings rather than content length. Because FreeX's delimited-text pipeline
///     works with raw scalar values (not formatted cells with numeric formats / column widths), we
///     use max-content-width instead. This is the correct fallback documented by the task spec.
///   </item>
///   <item>
///     Numbers and booleans are right-aligned within their column; text and error values are
///     left-aligned — matching Excel's general alignment convention.
///   </item>
///   <item>Line endings are CRLF (matching Excel and the rest of the delimited-text pipeline).</item>
///   <item>Trailing spaces on each line are trimmed (matching Excel .prn behaviour).</item>
///   <item>
///     Cell display strings are produced by the same logic used by
///     <see cref="DelimitedTextWorkbookWriter"/>: a cell with an explicit (non-"General") applied
///     number format is rendered through <c>NumberFormatter</c> (so "0%"/currency/custom date
///     formats export their displayed text, e.g. "15%"), otherwise numbers use
///     <c>InvariantCulture</c> round-trip formatting and date/time values format as ISO
///     dates/times; booleans render as TRUE/FALSE, errors as their code string (e.g. #VALUE!).
///     Formula cells write their calculated <see cref="Cell.Value"/> (never the formula source
///     text), matching Excel's plain-text Save-As behaviour.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Load (open):</b> Parses .prn by splitting each line on runs of whitespace, then coercing
/// each token with the same value-coercion logic the delimited-text reader uses. This is the
/// minimal correct interpretation: a .prn file is fundamentally a space-aligned text dump, and
/// re-separating on whitespace is how Excel re-imports one.
/// </para>
/// <para>
/// Encoding: the OS's current-culture ANSI code page, no byte-order mark (matching Excel's plain
/// "Text" Save-As types, which predate Unicode -- see
/// <see cref="DelimitedTextWorkbookWriter.ResolveAnsiEncoding"/>). Loading mirrors this: a strict
/// UTF-8 decode is tried first (so genuinely UTF-8/BOM-marked files still round-trip), falling
/// back to the same current-culture ANSI resolution on decode failure.
/// </para>
/// </remarks>
public sealed class PrnFileAdapter : IFileAdapter
{
    public string Extension => ".prn";
    public string FormatName => "Formatted Text (Space delimited)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".prn", "Formatted Text (Space delimited)", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) => PrnWorkbookReader.Load(stream);

    public void Save(Workbook workbook, Stream stream) => PrnWorkbookWriter.Save(workbook, stream);
}

// ---------------------------------------------------------------------------
// Writer
// ---------------------------------------------------------------------------

internal static class PrnWorkbookWriter
{
    public static void Save(Workbook workbook, Stream stream)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        if (workbook.Sheets.Count == 0) return;

        // Real Excel's plain-text Save-As types (including .prn) export the active (currently
        // selected) sheet, not the first sheet in tab order — matching
        // DelimitedTextWorkbookWriter.Save's identical rule for CSV/TXT.
        var activeSheetIndex = workbook.ActiveSheetIndex is { } index && index >= 0 && index < workbook.Sheets.Count
            ? index
            : 0;
        var sheet = workbook.Sheets[activeSheetIndex];

        // --- Pass 1: collect display text for every cell in the used range ---
        if (sheet.GetUsedRange() is not { } usedRange) return;

        var minRow = usedRange.Start.Row;
        var maxRow = usedRange.End.Row;
        var minCol = usedRange.Start.Col;
        var maxCol = usedRange.End.Col;

        if (minRow < 1) minRow = 1;
        if (minCol < 1) minCol = 1;
        if (maxRow > CellAddress.MaxRow) maxRow = CellAddress.MaxRow;
        if (maxCol > CellAddress.MaxCol) maxCol = CellAddress.MaxCol;

        var rowCount = (int)(maxRow - minRow + 1);
        var colCount = (int)(maxCol - minCol + 1);

        // rowTexts[r] — sparse map of column -> display text for row r (relative to minRow/minCol).
        // A sheet's used-range bounding box can be enormous (e.g. a value in A1 and another in
        // XFD1048576 gives a 1,048,576 x 16,384 box) even though only a handful of cells are
        // actually populated, so we must NOT materialize a dense rowCount*colCount matrix — that
        // both overflows practical array-size limits and would OOM. Instead we only keep entries
        // for cells that actually produced non-empty display text; every other cell defaults to
        // an empty string when read back in pass 2, exactly as the dense array's null-coalescing
        // fallback did.
        var rowTexts = new Dictionary<int, Dictionary<int, string>>();
        // isNumeric[c] — true if column c contains at least one numeric/boolean cell and no
        //               non-numeric non-empty cells; used for right-alignment decision.
        // We track per-column: has any cell? has any non-right-align cell?
        var colHasContent = new bool[colCount];
        var colHasLeftAlignContent = new bool[colCount];
        var colMaxWidth = new int[colCount];

        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            var r = (int)(address.Row - minRow);
            var c = (int)(address.Col - minCol);
            if (r < 0 || r >= rowCount || c < 0 || c >= colCount) continue;

            var text = GetCellDisplayText(cell, workbook);
            if (text.Length == 0) continue;

            if (!rowTexts.TryGetValue(r, out var rowMap))
            {
                rowMap = new Dictionary<int, string>();
                rowTexts[r] = rowMap;
            }
            rowMap[c] = text;

            colHasContent[c] = true;
            if (!IsRightAlignValue(cell))
                colHasLeftAlignContent[c] = true;

            if (text.Length > colMaxWidth[c])
                colMaxWidth[c] = text.Length;
        }

        // A column is right-aligned only if it has content and NO left-align content cells.
        var colRightAlign = new bool[colCount];
        for (var c = 0; c < colCount; c++)
            colRightAlign[c] = colHasContent[c] && !colHasLeftAlignContent[c];

        // --- Pass 2: write rows ---
        // Excel's "Formatted Text (Space delimited)" Save-As is part of the same pre-Unicode
        // legacy-text family as its plain CSV/TXT Save-As types, which write the OS's current-
        // culture ANSI code page rather than UTF-8 (see DelimitedTextWorkbookWriter.ResolveAnsiEncoding).
        // Writing UTF-8 here would mojibake non-ASCII text when the .prn is later reopened by real
        // Excel, which assumes ANSI for this format rather than sniffing UTF-8.
        using var writer = new StreamWriter(stream, DelimitedTextWorkbookWriter.ResolveAnsiEncoding(), leaveOpen: true);

        for (var r = 0; r < rowCount; r++)
        {
            // A row with no populated cells always renders as an entirely blank (trimmed) line,
            // regardless of other rows' column widths — every position is either width-0 (skipped)
            // or padding spaces for an empty cell, so the whole line is spaces and gets trimmed to
            // "". Short-circuit it directly instead of materializing/looping over `colCount`
            // columns for rows that contributed nothing to the sparse map.
            if (!rowTexts.TryGetValue(r, out var rowMap))
            {
                writer.Write("\r\n");
                continue;
            }

            // Build the line character-by-character into a StringBuilder so we can right-trim.
            var sb = new StringBuilder();

            for (var c = 0; c < colCount; c++)
            {
                // Single-space column separator (except before first column)
                if (c > 0)
                    sb.Append(' ');

                var width = colMaxWidth[c];
                if (width == 0)
                {
                    // Empty column — write nothing (the separator space above is sufficient)
                    continue;
                }

                var text = rowMap.TryGetValue(c, out var cellText) ? cellText : string.Empty;
                var rightAlign = colRightAlign[c];

                if (text.Length >= width)
                {
                    // Cell content fills or exceeds the column width — write as-is (no padding).
                    // Excel truncates overflowing content; we write full text here because our
                    // column widths are already max-content-width so overflow never happens in
                    // practice, but guard anyway.
                    sb.Append(text.Length > width ? text.AsSpan(0, width) : text.AsSpan());
                }
                else if (rightAlign)
                {
                    // Right-align: pad with spaces on the left
                    sb.Append(' ', width - text.Length);
                    sb.Append(text);
                }
                else
                {
                    // Left-align: pad with spaces on the right
                    sb.Append(text);
                    sb.Append(' ', width - text.Length);
                }
            }

            // Trim trailing spaces (Excel .prn behaviour)
            var lineLength = sb.Length;
            while (lineLength > 0 && sb[lineLength - 1] == ' ')
                lineLength--;

            writer.Write(sb.ToString(0, lineLength));
            writer.Write("\r\n");
        }
    }

    /// <summary>
    /// Produces the plain-text display string for a cell, using the same serialisation logic as
    /// <see cref="DelimitedTextWorkbookWriter"/> (invariant-culture numbers, ISO dates, etc.).
    /// </summary>
    /// <remarks>
    /// A formula cell's calculated <see cref="Cell.Value"/> is written here, never its formula
    /// source text — matching <c>DelimitedTextWorkbookWriter.WriteCellField</c> (CSV), whose
    /// comment documents this exact rule: real Excel's plain-text Save-As formats always write a
    /// formula cell's calculated result, not the formula itself.
    /// </remarks>
    private static string GetCellDisplayText(Cell cell, Workbook workbook)
    {
        // Real Excel's .prn Save-As, like its CSV/TXT siblings, writes the cell's DISPLAYED text —
        // a cell explicitly formatted "0%"/"$#,##0.00"/a custom date pattern exports "15%",
        // "$1,234.50", "Wednesday, July 22, 2026", not the bare stored value — matching
        // DelimitedTextWorkbookWriter's TryGetAppliedNumberFormat rule. A cell left at the default
        // "General" format has no explicit numeric/date shape to honor and keeps the raw fallback.
        if (cell.Value is NumberValue or DateTimeValue)
        {
            var numberFormat = workbook.GetStyle(cell.StyleId).NumberFormat;
            if (!string.IsNullOrEmpty(numberFormat) &&
                !string.Equals(numberFormat, "General", StringComparison.OrdinalIgnoreCase))
            {
                return NumberFormatter.FormatWithColor(
                    cell.Value, numberFormat, workbook.IndexedColors, workbook.Theme, workbook.Uses1904DateSystem).Text;
            }
        }

        return cell.Value switch
        {
            NumberValue number => FormatNumber(number.Value),
            DateTimeValue dateTime => FormatDateTime(dateTime),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            TextValue text => text.Value,
            ErrorValue error => error.Code,
            _ => string.Empty
        };
    }

    /// <summary>Returns true for value types that Excel right-aligns in cells by default.</summary>
    private static bool IsRightAlignValue(Cell cell) =>
        cell.Value is NumberValue or DateTimeValue or BoolValue;

    private static string FormatNumber(double value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
            return buffer[..written].ToString();

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatDateTime(DateTimeValue value)
    {
        if (!double.IsFinite(value.Value))
            return value.Value.ToString("R", CultureInfo.InvariantCulture);

        DateTime dt;
        try { dt = value.ToDateTime(); }
        catch (ArgumentException) { return value.Value.ToString("R", CultureInfo.InvariantCulture); }

        var hasFractional = dt.Ticks % TimeSpan.TicksPerSecond != 0;
        if (dt.Date == new DateTime(1899, 12, 30) && dt.TimeOfDay != TimeSpan.Zero)
            return dt.ToString(hasFractional ? "HH:mm:ss.FFFFFFF" : "HH:mm:ss", CultureInfo.InvariantCulture);

        return dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString(hasFractional ? "yyyy-MM-dd HH:mm:ss.FFFFFFF" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}

// ---------------------------------------------------------------------------
// Reader
// ---------------------------------------------------------------------------

internal static class PrnWorkbookReader
{
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static Workbook Load(Stream stream)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        // Read all text, honouring the same BOM-detection the delimited reader uses.
        var text = ReadText(stream);

        uint row = 1;
        var position = 0;

        while (position <= text.Length && row <= CellAddress.MaxRow)
        {
            // Find line end
            var lineStart = position;
            while (position < text.Length && text[position] is not '\r' and not '\n')
                position++;

            var lineEnd = position;

            // Advance past \r\n or \n
            if (position < text.Length)
            {
                if (text[position] == '\r')
                {
                    position++;
                    if (position < text.Length && text[position] == '\n')
                        position++;
                }
                else
                {
                    position++; // '\n'
                }
            }
            else
            {
                // End of string — only process if there is actual content
                if (lineEnd == lineStart)
                    break;
            }

            // Split line on runs of whitespace
            uint col = 1;
            var fieldStart = lineStart;
            var inField = false;

            for (var i = lineStart; i <= lineEnd && col <= CellAddress.MaxCol; i++)
            {
                var atEnd = i == lineEnd;
                var isSpace = !atEnd && char.IsWhiteSpace(text[i]);

                if (!isSpace && !atEnd)
                {
                    if (!inField)
                    {
                        fieldStart = i;
                        inField = true;
                    }
                }
                else if (inField)
                {
                    // Field ended at i (exclusive)
                    var fieldSpan = text.AsSpan(fieldStart, i - fieldStart);
                    var value = CoerceValue(fieldSpan);
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
                    col++;
                    inField = false;
                }
            }

            row++;
        }

        return workbook;
    }

    private static ScalarValue CoerceValue(ReadOnlySpan<char> field)
    {
        var trimmed = field.Trim();

        if (trimmed.Equals("TRUE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (trimmed.Equals("FALSE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);

        // Errors
        if (trimmed.Length > 0 && trimmed[0] == '#')
        {
            foreach (var (code, error) in ErrorValues)
            {
                if (trimmed.Equals(code.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return error;
            }
        }

        // Numbers
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number))
        {
            return new NumberValue(number);
        }

        // Date/time
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return DateTimeValue.FromDateTime(date);

        if (DateTime.TryParseExact(trimmed, "HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out var time))
            return new DateTimeValue(time.TimeOfDay.TotalDays);

        return new TextValue(field.ToString());
    }

    private static readonly Dictionary<string, ErrorValue> ErrorValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["#DIV/0!"] = ErrorValue.DivByZero,
            ["#VALUE!"] = ErrorValue.Value,
            ["#REF!"] = ErrorValue.Ref,
            ["#NAME?"] = ErrorValue.Name,
            ["#NULL!"] = ErrorValue.Null,
            ["#N/A"] = ErrorValue.NA,
            ["#NUM!"] = ErrorValue.Num,
            ["#CIRCULAR!"] = ErrorValue.Circular,
            ["#SPILL!"] = ErrorValue.Spill,
            ["#CALC!"] = ErrorValue.Calc,
        };

    private static string ReadText(Stream stream)
    {
        using var mem = new MemoryStream();
        stream.CopyTo(mem);
        var bytes = mem.ToArray();
        return DecodeText(bytes.AsSpan());
    }

    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        // Mirror the BOM-detection logic from DelimitedTextWorkbookReader
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Mirror DelimitedTextWorkbookReader.DecodeText's fallback (R111): a plain, BOM-less
            // .prn that isn't valid UTF-8 was almost certainly produced -- by this app or by real
            // Excel's "Formatted Text (Space delimited)" Save-As -- using the OS's current-culture
            // ANSI code page (e.g. 1252 on English Windows, 932/Shift-JIS on Japanese, 1251/Cyrillic
            // on Russian). A hard-coded Windows-1252 fallback regardless of locale would mojibake
            // any non-Western-European Windows install's own files. Share the exact same resolution
            // the CSV/TXT sibling uses.
            return DelimitedTextWorkbookWriter.ResolveAnsiEncoding().GetString(bytes);
        }
    }
}
