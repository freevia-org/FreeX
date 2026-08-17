using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// SYLK (Symbolic Link, .slk) file adapter — a line-based interchange format Excel still reads and
/// writes. Each line is a record whose first field is a one-letter type code followed by <c>;</c>-
/// separated fields, each itself a one-letter sub-code plus value:
/// <list type="bullet">
///   <item><c>ID;P…</c> — required header.</item>
///   <item><c>B;Y…;X…</c> — bounds (max row / col), informational.</item>
///   <item><c>C;Y…;X…;K…;E…</c> — a cell at row Y / col X with a constant value (<c>K</c>) and/or an
///   R1C1 expression (<c>E</c>). Y/X persist across records when omitted.</item>
///   <item><c>F;…</c> — a format record (we emit a coarse <c>P</c> format-index subset for number
///   formats; on read we ignore styling).</item>
///   <item><c>E</c> — end of file.</item>
/// </list>
/// Single sheet, values only (plus R1C1 formulas and a coarse number-format subset). A literal <c>;</c>
/// inside a value is escaped by doubling. Formulas are R1C1 and reuse the shared
/// <see cref="R1C1FormulaConverter"/>.
/// </summary>
public sealed class SlkFileAdapter : IFileAdapter
{
    public string Extension => ".slk";
    public string FormatName => "SYLK (Symbolic Link)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".slk", "SYLK (Symbolic Link)", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        using var reader = new StreamReader(stream, DetectEncoding(stream), detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        // Format records ("F;P<idx>") set the "current" format index; a subsequent "F;...;C;...;R;..."
        // or a "C" cell record at that position adopts it. SYLK's full format model is positional and
        // fiddly; we map only the small built-in P-format subset we emit on write, keyed by index.
        var formatCodesByIndex = new List<string>();
        uint curRow = 1, curCol = 1;

        string? line;
        while ((line = ReadLogicalLine(reader)) is not null)
        {
            if (line.Length == 0)
                continue;

            var fields = SplitFields(line);
            if (fields.Count == 0)
                continue;

            switch (fields[0])
            {
                case "E":
                    return workbook;
                case "P":
                    // A format-definition record: "P;P<format-code>" appends a code to the indexed table
                    // (referenced later by F;P<index>). Other P sub-codes (fonts etc.) are ignored.
                    foreach (var pf in fields.Skip(1))
                    {
                        if (pf.Length >= 1 && pf[0] == 'P')
                        {
                            formatCodesByIndex.Add(pf[1..]);
                            break;
                        }
                    }
                    break;
                case "F":
                    HandleFormatRecord(workbook, sheet, fields, formatCodesByIndex);
                    break;
                case "C":
                    HandleCellRecord(sheet, fields, ref curRow, ref curCol);
                    break;
                // ID / B / O / P / NU / NE and other records carry no cell data we model — skip.
            }
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(stream);
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        writer.WriteLine("ID;PFreeX");

        if (workbook.Sheets.Count == 0)
        {
            writer.WriteLine("E");
            return;
        }

        // Real Excel's SYLK Save-As exports the active (currently selected) sheet, not the
        // first sheet in tab order — matching DelimitedTextWorkbookWriter/PrnFileAdapter's
        // identical active-sheet rule for CSV/TXT/PRN.
        var activeSheetIndex = workbook.ActiveSheetIndex is { } activeIndex && activeIndex >= 0 && activeIndex < workbook.Sheets.Count
            ? activeIndex
            : 0;
        var sheet = workbook.Sheets[activeSheetIndex];
        var cells = sheet.GetOccupiedCellMap()
            .Where(kvp => IsValidPosition(kvp.Key.Row, kvp.Key.Col))
            .OrderBy(kvp => kvp.Key.Row).ThenBy(kvp => kvp.Key.Col)
            .ToList();

        uint maxRow = 0, maxCol = 0;
        foreach (var (key, _) in cells)
        {
            maxRow = Math.Max(maxRow, key.Row);
            maxCol = Math.Max(maxCol, key.Col);
        }
        if (maxRow > 0 && maxCol > 0)
            writer.WriteLine($"B;Y{maxRow};X{maxCol}");

        // Emit number formats as a small indexed P-table so they round-trip without per-cell format text.
        var formatIndexByCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, cell) in cells)
        {
            var code = workbook.GetStyle(cell.StyleId).NumberFormat;
            if (IsCustomNumberFormat(code) && !formatIndexByCode.ContainsKey(code))
            {
                var index = formatIndexByCode.Count;
                formatIndexByCode[code] = index;
                writer.WriteLine($"P;P{Escape(code)}");
            }
        }

        foreach (var (key, cell) in cells)
        {
            var sb = new StringBuilder();
            sb.Append("C;Y").Append(key.Row.ToString(CultureInfo.InvariantCulture))
              .Append(";X").Append(key.Col.ToString(CultureInfo.InvariantCulture));

            // K = constant value (always emitted so a reader without formula support still sees a value).
            sb.Append(";K").Append(FormatValue(cell.Value));

            if (cell.FormulaText is { Length: > 0 } formula)
            {
                var a1 = formula.StartsWith("=", StringComparison.Ordinal) ? formula[1..] : formula;
                var r1c1 = R1C1FormulaConverter.ToR1C1(a1, key.Row, key.Col);
                sb.Append(";E").Append(Escape(r1c1));
            }

            writer.WriteLine(sb.ToString());

            var formatCode = workbook.GetStyle(cell.StyleId).NumberFormat;
            if (formatIndexByCode.TryGetValue(formatCode, out var fmtIndex))
                writer.WriteLine($"F;P{fmtIndex.ToString(CultureInfo.InvariantCulture)};Y{key.Row};X{key.Col}");
        }

        writer.WriteLine("E");
    }

    // ---- read helpers -----------------------------------------------------------------------------

    /// <summary>
    /// An <c>F;P&lt;idx&gt;;Y&lt;row&gt;;X&lt;col&gt;</c> record applies a previously-defined number format
    /// (by index) to the cell already loaded at that explicit position. Cell records (<c>C</c>) are always
    /// emitted before their format record on write, so the target cell exists by the time we get here.
    /// </summary>
    private static void HandleFormatRecord(
        Workbook workbook,
        Sheet sheet,
        List<string> fields,
        List<string> formatCodesByIndex)
    {
        int? formatIndex = null;
        uint? row = null, col = null;
        foreach (var field in fields.Skip(1))
        {
            if (field.Length < 1) continue;
            switch (field[0])
            {
                case 'Y' when uint.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y >= 1:
                    row = y;
                    break;
                case 'X' when uint.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var x) && x >= 1:
                    col = x;
                    break;
                case 'P' when int.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var p) &&
                              p >= 0 && p < formatCodesByIndex.Count:
                    formatIndex = p;
                    break;
            }
        }

        if (formatIndex is not { } fmtIdx || row is not { } r || col is not { } c || !IsValidPosition(r, c))
            return;

        var cell = sheet.GetCell(new CellAddress(sheet.Id, r, c));
        if (cell is null)
            return;

        cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = formatCodesByIndex[fmtIdx] });
    }

    private static void HandleCellRecord(
        Sheet sheet,
        List<string> fields,
        ref uint curRow,
        ref uint curCol)
    {
        string? expression = null;
        string? constant = null;
        var hasConstant = false;

        foreach (var field in fields.Skip(1))
        {
            if (field.Length < 1) continue;
            switch (field[0])
            {
                case 'Y' when uint.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y >= 1:
                    curRow = y;
                    break;
                case 'X' when uint.TryParse(field.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var x) && x >= 1:
                    curCol = x;
                    break;
                case 'K':
                    constant = field[1..];
                    hasConstant = true;
                    break;
                case 'E':
                    expression = field[1..];
                    break;
            }
        }

        if (!hasConstant && expression is null)
            return;
        if (!IsValidPosition(curRow, curCol))
            return;

        var addr = new CellAddress(sheet.Id, curRow, curCol);
        Cell cell;
        if (expression is { Length: > 0 })
        {
            var a1 = R1C1FormulaConverter.ToA1(expression, curRow, curCol);
            cell = Cell.FromFormula(a1);
            if (hasConstant)
                cell.Value = ParseValue(constant!); // preserve the cached formula result
        }
        else
        {
            cell = Cell.FromValue(ParseValue(constant!));
        }

        sheet.SetCell(addr, cell);
    }

    private static ScalarValue ParseValue(string raw)
    {
        if (raw.Length == 0)
            return new TextValue("");

        // Quoted → string literal (SYLK quotes text values).
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return new TextValue(raw[1..^1]);

        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);

        if (raw[0] == '#' && DelimitedErrorLiterals.TryGetValue(raw, out var error))
            return error;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            return new NumberValue(number);

        return new TextValue(raw);
    }

    // ---- write helpers ----------------------------------------------------------------------------

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue n when double.IsFinite(n.Value) => n.Value.ToString("R", CultureInfo.InvariantCulture),
        NumberValue => "0",
        DateTimeValue d when double.IsFinite(d.Value) => d.Value.ToString("R", CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        ErrorValue e => Escape(e.Code),
        TextValue t => $"\"{Escape(t.Value)}\"",
        _ => "\"\"",
    };

    // ---- shared helpers ---------------------------------------------------------------------------

    private static bool IsValidPosition(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow && col is >= 1 and <= CellAddress.MaxCol;

    private static bool IsCustomNumberFormat(string code) =>
        !string.IsNullOrEmpty(code) && !code.Equals("General", StringComparison.OrdinalIgnoreCase);

    /// <summary>A literal field separator (<c>;</c>) inside a value is escaped by doubling it.</summary>
    private static string Escape(string value) => value.Replace(";", ";;", StringComparison.Ordinal);

    /// <summary>
    /// Splits a SYLK record into fields on single <c>;</c> separators, treating a doubled <c>;;</c> as an
    /// escaped literal semicolon within the current field.
    /// </summary>
    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == ';')
            {
                if (i + 1 < line.Length && line[i + 1] == ';')
                {
                    sb.Append(';');
                    i++;
                    continue;
                }

                fields.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>
    /// Reads one logical SYLK record, re-joining physical lines that were split by an embedded newline
    /// inside a quoted <c>K"..."</c> constant. On write, a <see cref="TextValue"/> containing '\n'/'\r'
    /// is quoted but the newline itself is never escaped, so <see cref="StreamWriter.WriteLine"/> ends up
    /// emitting it as a genuine line break — the quote opens on one physical line and closes on a later
    /// one. A record's quotes always balance in pairs, so an odd running count of <c>"</c> means the
    /// value's closing quote (and the newline it swallowed) is still ahead; keep pulling physical lines,
    /// rejoining with the '\n' that was in the original value, until the quote closes or the file ends.
    /// </summary>
    private static string? ReadLogicalLine(TextReader reader)
    {
        var line = reader.ReadLine();
        if (line is null)
            return null;

        while (CountQuotes(line) % 2 != 0)
        {
            var next = reader.ReadLine();
            if (next is null)
                break;
            line += "\n" + next;
        }

        return line;
    }

    private static int CountQuotes(string s)
    {
        var count = 0;
        foreach (var c in s)
        {
            if (c == '"')
                count++;
        }
        return count;
    }

    private static Encoding DetectEncoding(Stream stream) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly Dictionary<string, ErrorValue> DelimitedErrorLiterals = new(StringComparer.OrdinalIgnoreCase)
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
        // Extended Excel-365 error codes: the write side (FormatValue above) already emits these
        // verbatim via ErrorValue.Code, but without an entry here ParseValue's lookup misses and
        // falls through to the final `return new TextValue(raw)` -- silently reclassifying a
        // reloaded error cell as text (ISERROR/ISNA would then wrongly return FALSE).
        ["#FIELD!"] = ErrorValue.Field,
        ["#CONNECT!"] = new ErrorValue("#CONNECT!"),
        ["#UNKNOWN!"] = new ErrorValue("#UNKNOWN!"),
        ["#BLOCKED!"] = new ErrorValue("#BLOCKED!"),
        ["#GETTING_DATA"] = new ErrorValue("#GETTING_DATA"),
    };
}
