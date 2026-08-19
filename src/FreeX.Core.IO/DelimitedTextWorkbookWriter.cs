using System.Globalization;
using System.Text;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class DelimitedTextWorkbookWriter
{
    private const int DelimiterBufferLength = 256;
    private static readonly Dictionary<char, string> DelimiterBuffers = new();
    private static readonly HashSet<string> ErrorTextLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "#DIV/0!",
        "#VALUE!",
        "#REF!",
        "#NAME?",
        "#NULL!",
        "#N/A",
        "#NUM!",
        "#CIRCULAR!",
        "#SPILL!",
        "#CALC!",
        "#CONNECT!",
        "#UNKNOWN!",
        "#FIELD!",
        "#BLOCKED!",
        "#GETTING_DATA"
    };

    /// <summary>
    /// UTF-8 without a byte-order mark. Kept for callers that explicitly want UTF-8-no-BOM output;
    /// the plain-text Save-As default is <see cref="ResolveAnsiEncoding"/> below (Excel's actual
    /// plain "CSV (Comma delimited)" / "Text (Tab delimited)" encoding).
    /// </summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>UTF-8 with a BOM — Excel's "CSV UTF-8 (Comma delimited)" Save-As type.</summary>
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>UTF-16 little-endian with a BOM — Excel's "Unicode Text (*.txt)" Save-As type.</summary>
    public static readonly Encoding Utf16LeBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    public static void Save(Workbook workbook, Stream stream, char delimiter) =>
        Save(workbook, stream, delimiter, ResolveAnsiEncoding());

    /// <summary>
    /// Resolves the OS ANSI code page (e.g. Windows-1252 on an English system, Shift-JIS/932 on a
    /// Japanese one), no byte-order mark. Real Excel's plain "CSV (Comma delimited)" / "Text (Tab
    /// delimited)" Save-As types predate Unicode and still write this legacy ANSI encoding, not
    /// UTF-8 — that's precisely why Excel ships a separate "CSV UTF-8" menu entry (<see cref="Utf8Bom"/>)
    /// for the modern Unicode-safe alternative. Writing UTF-8 here instead would mojibake non-ASCII
    /// text when the file is later reopened by real Excel, which assumes ANSI for a BOM-less plain
    /// CSV/TXT file rather than sniffing UTF-8.
    /// </summary>
    internal static Encoding ResolveAnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(ResolveAnsiCodePage());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(1252);
        }
    }

    private static int ResolveAnsiCodePage() => CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

    /// <summary>
    /// Saves using <see cref="ResolveAnsiEncoding"/> (the plain, non-"UTF-8" CSV/TXT/TSV/TAB
    /// Save-As default) while tracking whether that legacy ANSI code page could represent every
    /// character written. csv-edge-cases-F1: the plain ANSI encoding has no way to represent
    /// characters outside its code page (CJK, Cyrillic on an en-US machine, emoji, many accented
    /// letters) — <see cref="EncoderReplacementFallback"/> silently swaps each one for a literal
    /// '?' byte instead of raising any signal, which is permanent data loss once the source
    /// workbook is closed. This keeps that exact on-disk byte output (still '?' — the file format
    /// itself has no escape for a character its code page can't hold, so replacement is
    /// unavoidable) but surfaces a warning the caller can show the user, mirroring how
    /// <see cref="XlsxFileAdapter"/> reports other non-fatal, partial-data-loss save outcomes via
    /// <see cref="IWarningCollectingFileAdapter"/>.
    /// </summary>
    public static XlsxSaveResult SaveWithWarnings(Workbook workbook, Stream stream, char delimiter)
    {
        var fallback = new LossTrackingEncoderFallback();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(ResolveAnsiCodePage(), fallback, DecoderFallback.ReplacementFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            encoding = Encoding.GetEncoding(1252, fallback, DecoderFallback.ReplacementFallback);
        }

        Save(workbook, stream, delimiter, encoding);

        if (!fallback.LossDetected)
            return XlsxSaveResult.Clean;

        return new XlsxSaveResult(
        [
            $"Some text could not be represented in the {encoding.EncodingName} encoding used for this file " +
            "type and was replaced with '?'. Save as \"CSV UTF-8 (Comma delimited)\" or \"Unicode Text\" instead to preserve it exactly."
        ]);
    }

    /// <summary>
    /// <see cref="EncoderFallback"/> that preserves the existing '?'-replacement byte output (so
    /// the saved file is unchanged) while recording whether any character actually needed it, for
    /// <see cref="SaveWithWarnings"/>.
    /// </summary>
    private sealed class LossTrackingEncoderFallback : EncoderFallback
    {
        public bool LossDetected { get; private set; }

        public override int MaxCharCount => 1;

        public override EncoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);

        private sealed class Buffer(LossTrackingEncoderFallback owner) : EncoderFallbackBuffer
        {
            private bool _hasPending;

            public override bool Fallback(char charUnknown, int index)
            {
                owner.LossDetected = true;
                _hasPending = true;
                return true;
            }

            public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
            {
                owner.LossDetected = true;
                _hasPending = true;
                return true;
            }

            public override char GetNextChar()
            {
                if (!_hasPending)
                    return '\0';

                _hasPending = false;
                return '?';
            }

            public override bool MovePrevious() => false;

            public override int Remaining => _hasPending ? 1 : 0;
        }
    }

    public static void Save(Workbook workbook, Stream stream, char delimiter, Encoding encoding)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        if (workbook.Sheets.Count == 0) return;

        // Real Excel's CSV/TXT Save-As exports the active (currently selected) sheet, not the
        // first sheet in tab order — those can differ once the user has switched tabs.
        var activeSheetIndex = workbook.ActiveSheetIndex is { } index && index >= 0 && index < workbook.Sheets.Count
            ? index
            : 0;
        var sheet = workbook.Sheets[activeSheetIndex];
        // Materialize the cells before calculating output bounds. The save runs off the UI thread,
        // so retaining the live sheet enumeration would allow a concurrent edit to change the
        // values or row extent after the save has started. In particular, a new cell beyond the
        // range that was previously measured must be part of the same snapshot as the bounds.
        var cells = sheet.EnumerateCells()
            .Where(static pair => IsValidCellAddress(pair.Address.Row, pair.Address.Col))
            .Select(static pair => (pair.Address, Cell: pair.Cell.Clone()))
            .ToArray();
        if (cells.Length == 0) return;

        var rowCapacity = EstimateRowCapacity(cells);
        var cellsPerRowCapacity = EstimateCellsPerRowCapacity(cells.Length, rowCapacity);
        var rowLookup = new Dictionary<uint, DelimitedTextRowBucket>(rowCapacity);
        var rows = new List<DelimitedTextRowBucket>(rowCapacity);
        var endRow = 0u;
        var endCol = 0u;
        foreach (var (address, cell) in cells)
        {
            if (!rowLookup.TryGetValue(address.Row, out var row))
            {
                row = new DelimitedTextRowBucket(address.Row, cellsPerRowCapacity);
                rowLookup[address.Row] = row;
                rows.Add(row);
            }

            row.Cells.Add((address.Col, cell));
            endRow = Math.Max(endRow, address.Row);
            endCol = Math.Max(endCol, address.Col);
        }

        rows.Sort(static (left, right) => left.Row.CompareTo(right.Row));
        foreach (var row in rows)
            row.Cells.Sort(static (left, right) => left.Col.CompareTo(right.Col));

        using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        var nextRow = 1u;
        foreach (var row in rows)
        {
            while (nextRow < row.Row)
            {
                WriteBlankRow(writer, delimiter, endCol);
                nextRow++;
            }

            WriteRow(writer, delimiter, row.Cells, endCol, workbook);
            nextRow = row.Row + 1;
        }

        while (nextRow <= endRow)
        {
            WriteBlankRow(writer, delimiter, endCol);
            nextRow++;
        }
    }

    private static bool IsValidCellAddress(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        col is >= 1 and <= CellAddress.MaxCol;

    private static int EstimateRowCapacity(IReadOnlyList<(CellAddress Address, Cell Cell)> cells)
    {
        var rows = new HashSet<uint>();
        foreach (var (address, _) in cells)
            rows.Add(address.Row);

        return rows.Count;
    }

    private static int EstimateCellsPerRowCapacity(int cellCount, int rowCapacity)
    {
        if (rowCapacity <= 0)
            return 0;

        return Math.Max(1, (cellCount + rowCapacity - 1) / rowCapacity);
    }

    private sealed class DelimitedTextRowBucket(uint row, int cellCapacity)
    {
        public uint Row { get; } = row;

        public List<(uint Col, Cell Cell)> Cells { get; } = new(cellCapacity);
    }

    private static void WriteRow(TextWriter writer, char delimiter, List<(uint Col, Cell Cell)> cells, uint endCol, Workbook workbook)
    {
        var previousCol = 0u;
        foreach (var (col, cell) in cells)
        {
            WriteDelimiters(writer, delimiter, previousCol == 0 ? col - 1 : col - previousCol);
            WriteCellField(writer, delimiter, cell, workbook);
            previousCol = col;
        }

        WriteDelimiters(writer, delimiter, endCol - previousCol);
        writer.Write("\r\n");
    }

    private static void WriteBlankRow(TextWriter writer, char delimiter, uint endCol)
    {
        if (endCol > 0)
            WriteDelimiters(writer, delimiter, endCol - 1);

        writer.Write("\r\n");
    }

    private static void WriteDelimiters(TextWriter writer, char delimiter, uint count)
    {
        if (count == 1)
        {
            writer.Write(delimiter);
            return;
        }

        var delimiterBuffer = GetDelimiterBuffer(delimiter);
        while (count >= DelimiterBufferLength)
        {
            writer.Write(delimiterBuffer);
            count -= DelimiterBufferLength;
        }

        if (count > 0)
            writer.Write(delimiterBuffer.AsSpan(0, (int)count));
    }

    private static string GetDelimiterBuffer(char delimiter)
    {
        lock (DelimiterBuffers)
        {
            if (!DelimiterBuffers.TryGetValue(delimiter, out var buffer))
            {
                buffer = string.Create(
                    DelimiterBufferLength,
                    delimiter,
                    static (chars, value) => chars.Fill(value));
                DelimiterBuffers[delimiter] = buffer;
            }

            return buffer;
        }
    }

    private static void WriteField(TextWriter writer, char delimiter, string value, bool isTextValue)
    {
        if (value.Length == 0)
        {
            if (isTextValue)
                writer.Write("\"\"");
            return;
        }

        if (!ShouldQuoteField(value, delimiter, isTextValue))
        {
            writer.Write(value);
            return;
        }

        var fieldValue = isTextValue && ShouldWriteTextMarker(value)
            ? $"'{value}"
            : value;
        writer.Write('"');
        foreach (var ch in fieldValue)
        {
            if (ch == '"')
                writer.Write("\"\"");
            else
                writer.Write(ch);
        }

        writer.Write('"');
    }

    private static void WriteCellField(TextWriter writer, char delimiter, Cell cell, Workbook workbook)
    {
        // CSV has no formula syntax: real Excel's "CSV (Comma delimited)" / "CSV UTF-8" Save-As
        // always writes a formula cell's calculated result (Cell.Value), never the formula source
        // text — reopening such a file (in Excel or FreeX) must see the same value the workbook
        // showed, not a brand-new live formula re-evaluated against whatever now sits in its
        // references.
        switch (cell.Value)
        {
            case NumberValue number:
                WriteNumberValue(writer, delimiter, cell, number.Value, workbook);
                return;
            case DateTimeValue dateTime:
                WriteDateTimeValue(writer, delimiter, cell, dateTime, workbook);
                return;
            case BoolValue boolean:
                writer.Write(boolean.Value ? "TRUE" : "FALSE");
                return;
            case TextValue text:
                WriteField(writer, delimiter, text.Value, isTextValue: true);
                return;
            case ErrorValue error:
                WriteField(writer, delimiter, error.Code, isTextValue: false);
                return;
        }
    }

    // Real Excel's plain-text Save-As types (CSV/TSV/TXT/PRN) write the cell's DISPLAYED text, not
    // its raw stored value: a cell formatted "0%"/"$#,##0.00"/a custom date pattern exports "15%",
    // "$1,234.50", "Wednesday, July 22, 2026", etc. — matching what the grid shows and what the PDF
    // export path already does via NumberFormatter.FormatWithColor (see WorkbookPdfContentBuilder).
    // A cell left at the default "General" format has no explicit numeric/date shape to honor, so
    // it keeps the existing raw-value rendering below (round-trip invariant numbers, ISO dates).
    private static bool TryGetAppliedNumberFormat(Cell cell, Workbook workbook, out string numberFormat)
    {
        numberFormat = workbook.GetStyle(cell.StyleId).NumberFormat;
        return !string.IsNullOrEmpty(numberFormat) &&
               !string.Equals(numberFormat, "General", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteNumberValue(TextWriter writer, char delimiter, Cell cell, double value, Workbook workbook)
    {
        if (TryGetAppliedNumberFormat(cell, workbook, out var numberFormat))
        {
            var formatted = NumberFormatter.FormatWithColor(
                cell.Value, numberFormat, workbook.IndexedColors, workbook.Theme, workbook.Uses1904DateSystem).Text;
            WriteField(writer, delimiter, formatted, isTextValue: false);
            return;
        }

        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var charsWritten, provider: CultureInfo.InvariantCulture))
        {
            writer.Write(buffer[..charsWritten]);
            return;
        }

        writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteDateTimeValue(TextWriter writer, char delimiter, Cell cell, DateTimeValue value, Workbook workbook)
    {
        if (TryGetAppliedNumberFormat(cell, workbook, out var numberFormat))
        {
            var formatted = NumberFormatter.FormatWithColor(
                cell.Value, numberFormat, workbook.IndexedColors, workbook.Theme, workbook.Uses1904DateSystem).Text;
            WriteField(writer, delimiter, formatted, isTextValue: false);
            return;
        }

        if (TryFormatDateTimeValue(value, out var isoFormatted))
        {
            WriteField(writer, delimiter, isoFormatted, isTextValue: false);
            return;
        }

        WriteField(
            writer,
            delimiter,
            value.Value.ToString("R", CultureInfo.InvariantCulture),
            isTextValue: double.IsFinite(value.Value));
    }

    private static bool ShouldQuoteField(string value, char delimiter, bool isTextValue)
    {
        if (isTextValue && IsCoercionLikeText(value))
            return true;

        foreach (var ch in value)
        {
            if (ch == delimiter || ch is '"' or '\n' or '\r')
                return true;
        }

        return false;
    }

    private static bool IsCoercionLikeText(string value) =>
        value[0] is '=' or '+' or '-' or '@' ||
        IsSeparatorDirectiveLikeText(value) ||
        IsBooleanLikeText(value) ||
        IsDateTimeLikeText(value) ||
        IsUnsignedCurrencyText(value) ||
        IsSignedCurrencyText(value) ||
        IsPercentageText(value) ||
        IsNumericLikeText(value) ||
        IsParenthesizedCurrencyText(value) ||
        IsErrorLikeText(value);

    private static bool ShouldWriteTextMarker(string value) =>
        IsFormulaInjectionLikeText(value) ||
        IsBooleanLikeText(value) ||
        IsDateTimeLikeText(value) ||
        IsUnsignedCurrencyText(value) ||
        IsNumericLikeText(value);

    private static bool IsFormulaInjectionLikeText(string value) =>
        value[0] is '=' or '+' or '-' or '@';

    private static bool IsSeparatorDirectiveLikeText(string value) =>
        value is { Length: 4 } or { Length: 5 } &&
        value.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) &&
        (value.Length == 4 || value[4] is not '\r' and not '\n');

    private static bool IsBooleanLikeText(string value)
    {
        var trimmed = value.Trim();
        return string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateTimeLikeText(string value)
    {
        var trimmed = value.Trim();
        if (!HasSupportedDateTimeShape(trimmed))
            return false;

        return DateTime.TryParse(
            trimmed,
            CultureInfo.InvariantCulture,
            DateTimeStyles.NoCurrentDateDefault,
            out _);
    }

    private static bool HasSupportedDateTimeShape(string value)
    {
        var digitRun = 0;
        var digitGroups = 0;
        var inDigitGroup = false;
        var hasSlashOrDashSeparator = false;

        foreach (var ch in value)
        {
            if (ch == ':' || char.IsLetter(ch))
                return true;

            if (char.IsDigit(ch))
            {
                digitRun++;
                if (digitRun >= 4)
                    return true;

                if (!inDigitGroup)
                {
                    digitGroups++;
                    inDigitGroup = true;
                }
            }
            else
            {
                digitRun = 0;
                inDigitGroup = false;
                hasSlashOrDashSeparator |= ch is '/' or '-';
            }
        }

        // A year-less two-digit-group "M/d" or "M-d" shape (e.g. "1/2", "3-4") is also date-like on
        // the read side -- LooksLikeCurrentCultureDateCandidate in DelimitedTextWorkbookReader.cs
        // treats it as a date candidate because Excel's General-format auto-recognition converts
        // such a bare month/day token to a date, assuming the current year. A TextValue with this
        // shape must get the same leading-apostrophe marker as any other date-like text (below) so
        // reloading the saved file preserves it as text instead of silently turning it into a date.
        // "." is deliberately excluded here, mirroring the reader: it doubles as the decimal
        // separator in the common cultures (en-US, en-GB, fr-FR, ...), so a plain two-digit-group
        // decimal like "3.14" must not be treated as date-shaped.
        return digitGroups == 2 && hasSlashOrDashSeparator;
    }

    private static bool IsUnsignedCurrencyText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('$') &&
               double.TryParse(
                   trimmed,
                   NumberStyles.Currency,
                   CultureInfo.GetCultureInfo("en-US"),
                   out _);
    }

    private static bool IsNumericLikeText(string value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    private static bool IsSignedCurrencyText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 1 &&
               trimmed[0] is '+' or '-' &&
               double.TryParse(
                   trimmed,
                   NumberStyles.Currency,
                   CultureInfo.GetCultureInfo("en-US"),
                   out _);
    }

    private static bool IsPercentageText(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[^1] != '%')
            return false;

        return double.TryParse(trimmed[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsParenthesizedCurrencyText(string value) =>
        value.TrimStart().StartsWith('(') &&
        value.TrimEnd().EndsWith(')') &&
        double.TryParse(
            value,
            NumberStyles.Currency,
            CultureInfo.GetCultureInfo("en-US"),
            out _);

    private static bool IsErrorLikeText(string value) =>
        ErrorTextLiterals.Contains(value.Trim());

    private static bool TryFormatDateTimeValue(DateTimeValue value, out string text)
    {
        text = "";
        if (!double.IsFinite(value.Value))
            return false;

        DateTime dateTime;
        try
        {
            dateTime = value.ToDateTime();
        }
        catch (ArgumentException)
        {
            return false;
        }

        var hasFractionalSeconds = dateTime.Ticks % TimeSpan.TicksPerSecond != 0;
        if (dateTime.Date == new DateTime(1899, 12, 30) && dateTime.TimeOfDay != TimeSpan.Zero)
        {
            text = dateTime.ToString(hasFractionalSeconds ? "HH:mm:ss.FFFFFFF" : "HH:mm:ss", CultureInfo.InvariantCulture);
            return true;
        }

        text = dateTime.TimeOfDay == TimeSpan.Zero
            ? dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dateTime.ToString(hasFractionalSeconds ? "yyyy-MM-dd HH:mm:ss.FFFFFFF" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return true;
    }
}
