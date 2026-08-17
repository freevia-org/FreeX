using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// DIF (Data Interchange Format, .dif) file adapter — a line-oriented spreadsheet interchange format
/// Excel reads and writes. A DIF file is a header section followed by a data section:
/// <list type="bullet">
///   <item>Header chunks each span three lines: a topic keyword (<c>TABLE</c>/<c>VECTORS</c>/
///   <c>TUPLES</c>/<c>DATA</c>), a <c>&lt;vectorNumber&gt;,&lt;value&gt;</c> pair, and a quoted string.</item>
///   <item>Data: each row is introduced by a special <c>-1,0</c> / <c>BOT</c> (beginning-of-tuple) pair,
///   then one value pair per cell — <c>0,&lt;number&gt;</c> + a <c>V</c>/<c>NA</c>/<c>ERROR</c>/<c>TRUE</c>/
///   <c>FALSE</c> indicator line for numerics, or <c>1,0</c> + a quoted string line for text. The file
///   ends with <c>-1,0</c> / <c>EOD</c>.</item>
/// </list>
/// Single sheet, values only (numbers, text, booleans, errors). No formulas, formats, or structure.
/// </summary>
public sealed class DifFileAdapter : IFileAdapter
{
    private const int BeginningOfTuple = -1; // "-1,0" with a BOT/EOD indicator
    private const int NumericTypeId = 0;     // "0,<value>" numeric data
    private const int StringTypeId = 1;      // "1,0" followed by a quoted string

    public string Extension => ".dif";
    public string FormatName => "DIF (Data Interchange Format)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".dif", "DIF (Data Interchange Format)", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var lines = ReadAllLines(reader);

        // Skip the header: advance to the line after the "DATA" topic's 3-line chunk.
        var i = SkipToDataSection(lines);
        if (i < 0)
            return workbook;

        uint row = 0;
        uint col = 0;
        var inTuple = false;

        while (i + 1 < lines.Count)
        {
            var typeLine = lines[i];

            if (!TryParsePair(typeLine, out var typeId, out var number))
            {
                i += 2; // malformed pair — skip the chunk
                continue;
            }

            // The content line is normally a single physical line, but a quoted TextValue chunk may
            // contain an embedded '\n'/'\r' — Escape() only doubles embedded double-quotes, so that raw
            // line break survives into the written file and StreamReader.ReadLine splits it into two (or
            // more) physical lines. Stay quote-aware: if the value opens with '"' but does not close on
            // this line, keep folding subsequent physical lines in (re-inserting the '\n' between them)
            // until the closing unescaped quote is found. This both recovers the embedded line break and
            // keeps the index in sync so later records don't desync by one line.
            var (contentLine, nextIndex) = ReadQuotedAwareContent(lines, i + 1);
            i = nextIndex;

            if (typeId == BeginningOfTuple)
            {
                var indicator = contentLine.Trim().Trim('"');
                if (indicator.Equals("EOD", StringComparison.OrdinalIgnoreCase))
                    break;
                if (indicator.Equals("BOT", StringComparison.OrdinalIgnoreCase))
                {
                    row++;
                    col = 0;
                    inTuple = true;
                }
                continue;
            }

            if (!inTuple || row < 1)
                continue;

            col++;
            if (!IsValidPosition(row, col))
                continue;

            var addr = new CellAddress(sheet.Id, row, col);
            var indicatorValue = contentLine.Trim();

            if (typeId == NumericTypeId)
            {
                var value = ParseNumericChunk(number, indicatorValue);
                if (value is not null)
                    sheet.SetCell(addr, Cell.FromValue(value));
            }
            else if (typeId == StringTypeId)
            {
                // WriteEmpty emits the exact same "1,0" / "\"\"" chunk as a genuine empty-string cell
                // (DIF has no distinct blank marker for a text vector), so — matching the writer's own
                // documented convention that an empty string vector is a gap — leave the address
                // unoccupied here instead of materializing a spurious TextValue("") cell.
                var text = Unquote(contentLine);
                if (text.Length > 0)
                    sheet.SetCell(addr, Cell.FromValue(new TextValue(text)));
            }
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\r\n",
        };

        uint maxRow = 0, maxCol = 0;
        var cellsByRow = new SortedDictionary<uint, SortedDictionary<uint, Cell>>();
        if (workbook.Sheets.Count > 0)
        {
            // Real Excel's DIF Save-As exports the active (currently selected) sheet, not the
            // first sheet in tab order — matching DelimitedTextWorkbookWriter/PrnFileAdapter's
            // identical active-sheet rule for CSV/TXT/PRN.
            var activeSheetIndex = workbook.ActiveSheetIndex is { } index && index >= 0 && index < workbook.Sheets.Count
                ? index
                : 0;
            foreach (var (key, cell) in workbook.Sheets[activeSheetIndex].GetOccupiedCellMap())
            {
                if (!IsValidPosition(key.Row, key.Col))
                    continue;
                if (cell.Value is BlankValue && !cell.HasFormula)
                    continue;
                if (!cellsByRow.TryGetValue(key.Row, out var rowCells))
                    cellsByRow[key.Row] = rowCells = new SortedDictionary<uint, Cell>();
                rowCells[key.Col] = cell;
                maxRow = Math.Max(maxRow, key.Row);
                maxCol = Math.Max(maxCol, key.Col);
            }
        }

        // Header: TABLE, VECTORS (columns), TUPLES (rows), DATA.
        WriteHeaderChunk(writer, "TABLE", 1, "\"FreeX\"");
        WriteHeaderChunk(writer, "VECTORS", (long)maxCol, "\"\"");
        WriteHeaderChunk(writer, "TUPLES", (long)maxRow, "\"\"");
        WriteHeaderChunk(writer, "DATA", 0, "\"\"");

        for (uint r = 1; r <= maxRow; r++)
        {
            writer.WriteLine("-1,0");
            writer.WriteLine("BOT");
            cellsByRow.TryGetValue(r, out var rowCells);
            for (uint c = 1; c <= maxCol; c++)
            {
                if (rowCells is not null && rowCells.TryGetValue(c, out var cell))
                    WriteCell(writer, cell.Value);
                else
                    WriteEmpty(writer);
            }
        }

        writer.WriteLine("-1,0");
        writer.WriteLine("EOD");
    }

    // ---- write helpers ----------------------------------------------------------------------------

    private static void WriteHeaderChunk(TextWriter writer, string topic, long vectorNumber, string text)
    {
        writer.WriteLine(topic);
        writer.WriteLine($"0,{vectorNumber.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine(text);
    }

    private static void WriteCell(TextWriter writer, ScalarValue value)
    {
        switch (value)
        {
            case NumberValue n when double.IsFinite(n.Value):
                writer.WriteLine($"0,{n.Value.ToString("R", CultureInfo.InvariantCulture)}");
                writer.WriteLine("V");
                break;
            case DateTimeValue d when double.IsFinite(d.Value):
                writer.WriteLine($"0,{d.Value.ToString("R", CultureInfo.InvariantCulture)}");
                writer.WriteLine("V");
                break;
            case BoolValue b:
                writer.WriteLine("0,0");
                writer.WriteLine(b.Value ? "TRUE" : "FALSE");
                break;
            case ErrorValue error:
                // A single numeric chunk with the ERROR indicator (one chunk = one cell; emitting a
                // second string chunk here would be read back as an extra cell and shift the row).
                // DIF stores the specific error code as a quoted string alongside the ERROR flag so
                // it round-trips instead of degrading to the generic #VALUE! on reload.
                writer.WriteLine("0,0");
                writer.WriteLine($"ERROR:\"{Escape(error.Code)}\"");
                break;
            case TextValue t:
                writer.WriteLine("1,0");
                writer.WriteLine($"\"{Escape(t.Value)}\"");
                break;
            default:
                WriteEmpty(writer);
                break;
        }
    }

    private static void WriteEmpty(TextWriter writer)
    {
        // A blank cell: "1,0" with an empty special value "" — but DIF uses a numeric blank chunk
        // ("0,0" + "V" would be a zero). The conventional empty cell is "1,0" / "". Readers treat an
        // empty string vector as a gap.
        writer.WriteLine("1,0");
        writer.WriteLine("\"\"");
    }

    // ---- read helpers -----------------------------------------------------------------------------

    private static int SkipToDataSection(List<string> lines)
    {
        for (var i = 0; i + 2 < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("DATA", StringComparison.OrdinalIgnoreCase))
                return i + 3; // skip the DATA topic's 3-line chunk
        }

        return -1;
    }

    private static ScalarValue? ParseNumericChunk(double number, string indicator)
    {
        // "ERROR:<quoted code>" (our own extension for round-tripping the specific error) has a
        // quoted string tail that Trim('"') below would mangle (it only strips leading/trailing
        // quotes, not the pair surrounding the embedded code), so check for it against the raw,
        // whitespace-trimmed indicator before the generic '"'-trim token below.
        if (indicator.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return ParseErrorCode(Unquote(indicator[6..]));

        // The indicator line qualifies the "0,<number>" pair.
        var token = indicator.Trim('"');
        if (token.Equals("V", StringComparison.OrdinalIgnoreCase))
            return double.IsFinite(number) ? new NumberValue(number) : null;
        if (token.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (token.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (token.Equals("NA", StringComparison.OrdinalIgnoreCase))
            return ErrorValue.NA;
        if (token.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            return ErrorValue.Value;
        // Unknown indicator → treat as a plain numeric if finite.
        return double.IsFinite(number) ? new NumberValue(number) : null;
    }

    private static readonly Dictionary<string, ErrorValue> KnownErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        [ErrorValue.DivByZero.Code] = ErrorValue.DivByZero,
        [ErrorValue.Value.Code] = ErrorValue.Value,
        [ErrorValue.Ref.Code] = ErrorValue.Ref,
        [ErrorValue.Name.Code] = ErrorValue.Name,
        [ErrorValue.Null.Code] = ErrorValue.Null,
        [ErrorValue.NA.Code] = ErrorValue.NA,
        [ErrorValue.Num.Code] = ErrorValue.Num,
        [ErrorValue.Circular.Code] = ErrorValue.Circular,
        [ErrorValue.Spill.Code] = ErrorValue.Spill,
        [ErrorValue.Calc.Code] = ErrorValue.Calc,
    };

    private static ErrorValue ParseErrorCode(string code) =>
        KnownErrorCodes.TryGetValue(code, out var known) ? known : new ErrorValue(code);

    private static bool TryParsePair(string line, out int typeId, out double number)
    {
        typeId = 0;
        number = 0;
        var comma = line.IndexOf(',');
        if (comma <= 0)
            return false;

        if (!int.TryParse(line.AsSpan(0, comma), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out typeId))
            return false;

        var rest = line.AsSpan(comma + 1).Trim();
        // The value field is not always a finite number (e.g. the BOT/EOD pair uses 0); parse leniently.
        double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        return true;
    }

    private static string Unquote(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        return trimmed;
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    /// <summary>
    /// Reads the content line starting at <paramref name="start"/>, folding in subsequent physical
    /// lines (rejoined with '\n') while the value is an unterminated quoted string — i.e. it opens with
    /// '"' but the matching unescaped closing '"' has not yet been seen. Returns the (possibly
    /// multi-line) content and the index of the next unread line, so the caller's cursor stays in sync
    /// even when a single logical value spanned several physical lines.
    /// </summary>
    private static (string Content, int NextIndex) ReadQuotedAwareContent(List<string> lines, int start)
    {
        if (start >= lines.Count)
            return (string.Empty, start);

        var value = lines[start];
        var index = start + 1;
        while (!IsQuoteClosed(value) && index < lines.Count)
        {
            value = string.Concat(value, "\n", lines[index]);
            index++;
        }

        return (value, index);
    }

    /// <summary>
    /// True when <paramref name="text"/> does not open with an unescaped double-quote, or it does and
    /// that quote is already matched by a later unescaped closing quote (embedded "" escapes are
    /// skipped in pairs, matching <see cref="Escape"/>/<see cref="Unquote"/>).
    /// </summary>
    private static bool IsQuoteClosed(string text)
    {
        if (text.Length == 0 || text[0] != '"')
            return true; // not a quoted value — nothing to close, single line stands as-is

        var i = 1;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i += 2; // escaped quote — doubled per Escape()
                    continue;
                }

                return true; // unescaped closing quote found
            }

            i++;
        }

        return false;
    }

    private static List<string> ReadAllLines(TextReader reader)
    {
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            lines.Add(line);
        return lines;
    }

    private static bool IsValidPosition(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow && col is >= 1 and <= CellAddress.MaxCol;
}
