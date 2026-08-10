using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class DelimitedTextWorkbookReader
{
    private static readonly Dictionary<string, ErrorValue> ErrorValues = new(StringComparer.OrdinalIgnoreCase)
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
        ["#CONNECT!"] = new ErrorValue("#CONNECT!"),
        ["#UNKNOWN!"] = new ErrorValue("#UNKNOWN!"),
        ["#FIELD!"] = new ErrorValue("#FIELD!"),
        ["#BLOCKED!"] = new ErrorValue("#BLOCKED!"),
        ["#GETTING_DATA"] = new ErrorValue("#GETTING_DATA")
    };

    public static Workbook Load(
        Stream stream,
        char delimiter,
        bool allowSeparatorDirective = false,
        bool collapseConsecutiveDelimiters = false)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        var text = ReadText(stream);
        var position = 0;
        uint row = 1;
        var canReadSeparatorDirective = allowSeparatorDirective;
        var fields = new DelimitedTextRecord();
        var quotedFieldBuilder = new StringBuilder();
        while (TryReadRecord(text, ref position, delimiter, fields, quotedFieldBuilder, collapseConsecutiveDelimiters))
        {
            if (row > CellAddress.MaxRow)
                break;

            if (canReadSeparatorDirective && TryReadSeparatorDirective(fields, delimiter, out var directiveDelimiter))
            {
                delimiter = directiveDelimiter;
                canReadSeparatorDirective = false;
                continue;
            }
            canReadSeparatorDirective = false;

            for (var i = 0; i < fields.Count; i++)
            {
                if (i >= CellAddress.MaxCol)
                    break;

                var field = fields[i];
                var fieldSpan = field.AsSpan();
                if (fieldSpan.Length == 0)
                {
                    if (field.WasQuoted)
                        sheet.SetCell(new CellAddress(sheet.Id, row, (uint)(i + 1)), new TextValue(""));
                    continue;
                }

                var address = new CellAddress(sheet.Id, row, (uint)(i + 1));
                if (!field.WasQuoted && TryReadFormula(fieldSpan, out var formulaText))
                    sheet.SetCell(address, Cell.FromFormula(formulaText));
                else if (TryReadQuotedTextMarker(field, out var markedText))
                    sheet.SetCell(address, new TextValue(markedText));
                else if (ShouldPreserveQuotedFormulaLikeText(field))
                    sheet.SetCell(address, new TextValue(field.Value));
                else
                    sheet.SetCell(address, CoerceValue(field));
            }

            row++;
        }

        return workbook;
    }

    private static bool TryReadSeparatorDirective(
        DelimitedTextRecord fields,
        char currentDelimiter,
        out char delimiter)
    {
        delimiter = default;

        if (fields.Count == 2 &&
            !fields[0].WasQuoted &&
            !fields[1].WasQuoted &&
            fields[0].AsSpan().Equals("sep=".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            fields[1].AsSpan().Length == 0)
        {
            delimiter = currentDelimiter;
            return true;
        }

        if (fields.Count != 1 || fields[0].WasQuoted)
            return false;

        var directive = fields[0].AsSpan();
        if (directive.Length != 5 || !directive.StartsWith("sep=".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        delimiter = directive[4];
        return delimiter is not '\r' and not '\n' and not '"';
    }

    internal static bool TryReadRecord(TextReader reader, char delimiter, out List<DelimitedTextField> fields)
    {
        fields = [];
        var record = new DelimitedTextRecord();
        var text = reader.ReadToEnd();
        var position = 0;
        if (!TryReadRecord(text, ref position, delimiter, record, new StringBuilder(), collapseConsecutiveDelimiters: false))
            return false;

        fields.Capacity = record.Count;
        for (var i = 0; i < record.Count; i++)
            fields.Add(record[i]);
        return true;
    }

    private static bool TryReadRecord(
        string source,
        ref int position,
        char delimiter,
        DelimitedTextRecord fields,
        StringBuilder quotedFieldBuilder,
        bool collapseConsecutiveDelimiters = false)
    {
        fields.Clear();
        quotedFieldBuilder.Clear();
        var fieldStart = position;
        var inQuotes = false;
        var atFieldStart = true;
        var currentWasQuoted = false;

        while (position < source.Length)
        {
            var c = source[position++];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (position < source.Length && source[position] == '"')
                    {
                        position++;
                        quotedFieldBuilder.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    quotedFieldBuilder.Append(c);
                }

                continue;
            }

            if (c == '"' && atFieldStart)
            {
                inQuotes = true;
                atFieldStart = false;
                currentWasQuoted = true;
            }
            else if (c == delimiter)
            {
                AddField(fields, source, fieldStart, position - fieldStart - 1, currentWasQuoted, quotedFieldBuilder);
                quotedFieldBuilder.Clear();
                currentWasQuoted = false;
                atFieldStart = true;

                // R88-io-text-import-wizard-5-2: mirror TextToColumnsSplitter's "treat consecutive
                // delimiters as one" so a whitespace-aligned import matches what the wizard's preview
                // (built on that same splitter) already showed the user.
                if (collapseConsecutiveDelimiters)
                {
                    while (position < source.Length && source[position] == delimiter)
                        position++;
                }

                fieldStart = position;
            }
            else if (c == '\r')
            {
                var terminatorLength = 1;
                if (position < source.Length && source[position] == '\n')
                {
                    position++;
                    terminatorLength = 2;
                }

                AddField(fields, source, fieldStart, position - fieldStart - terminatorLength, currentWasQuoted, quotedFieldBuilder);
                return true;
            }
            else if (c == '\n')
            {
                AddField(fields, source, fieldStart, position - fieldStart - 1, currentWasQuoted, quotedFieldBuilder);
                return true;
            }
            else
            {
                if (currentWasQuoted)
                    quotedFieldBuilder.Append(c);
                atFieldStart = false;
            }
        }

        if (position > fieldStart || fields.Count > 0 || currentWasQuoted)
        {
            AddField(fields, source, fieldStart, position - fieldStart, currentWasQuoted, quotedFieldBuilder);
            return true;
        }

        return false;
    }

    private static void AddField(
        DelimitedTextRecord fields,
        string source,
        int start,
        int length,
        bool wasQuoted,
        StringBuilder quotedFieldBuilder)
    {
        fields.Add(wasQuoted
            ? new DelimitedTextField(quotedFieldBuilder.ToString(), wasQuoted)
            : DelimitedTextField.FromSource(source, start, length));
    }

    internal readonly struct DelimitedTextField
    {
        private readonly string? materializedValue;
        private readonly string? source;
        private readonly int start;
        private readonly int length;

        public DelimitedTextField(string value, bool wasQuoted)
        {
            materializedValue = value;
            source = null;
            start = 0;
            length = value.Length;
            WasQuoted = wasQuoted;
        }

        private DelimitedTextField(string source, int start, int length)
        {
            materializedValue = null;
            this.source = source;
            this.start = start;
            this.length = length;
            WasQuoted = false;
        }

        public bool WasQuoted { get; }

        public string Value => materializedValue ?? source!.Substring(start, length);

        public ReadOnlySpan<char> AsSpan() =>
            materializedValue is not null
                ? materializedValue.AsSpan()
                : source!.AsSpan(start, length);

        public static DelimitedTextField FromSource(string source, int start, int length) =>
            new(source, start, length);
    }

    private sealed class DelimitedTextRecord
    {
        // Fields beyond the sheet column limit can never be stored (consumers break at MaxCol), so
        // cap growth here too: a single line with millions of delimiters would otherwise drive
        // unbounded array growth — a cheap denial-of-service vector for an untrusted file.
        private static readonly int MaxFields = (int)CellAddress.MaxCol;

        private DelimitedTextField[] fields = new DelimitedTextField[16];

        public int Count { get; private set; }

        public DelimitedTextField this[int index] => fields[index];

        public void Clear() => Count = 0;

        public void Add(DelimitedTextField field)
        {
            if (Count >= MaxFields)
                return; // columns past the sheet limit are discarded by consumers anyway

            if (Count == fields.Length)
                Array.Resize(ref fields, Math.Min(fields.Length * 2, MaxFields));

            fields[Count++] = field;
        }
    }

    private static bool ShouldPreserveQuotedFormulaLikeText(DelimitedTextField field)
    {
        var value = field.AsSpan();
        if (!field.WasQuoted || value.Length == 0)
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return false;

        if (trimmed[0] == '#' && TryReadError(trimmed, out _))
            return true;

        return trimmed[0] switch
        {
            '=' or '@' => true,
            >= '0' and <= '9' => TryParsePercentage(trimmed, out _),
            '+' or '-' =>
                TryParseFiniteNumber(trimmed, NumberStyles.Any, out _) ||
                TryParsePercentage(trimmed, out _) ||
                TryParseCurrency(trimmed, out _),
            '(' => TryParseCurrency(trimmed, out _),
            _ => false
        };
    }

    private static bool TryReadQuotedTextMarker(DelimitedTextField field, out string text)
    {
        text = "";
        var value = field.AsSpan();
        if (!field.WasQuoted || value.Length < 2 || value[0] != '\'')
            return false;

        var candidate = value[1..];
        var trimmedCandidate = candidate.Trim();
        if (!IsBooleanLikeText(candidate) &&
            !TryReadErrorLike(trimmedCandidate, out _) &&
            !TryParseDateTime(candidate, out _) &&
            !TryParseTime(candidate, out _) &&
            !TryParsePercentage(trimmedCandidate, out _) &&
            !TryParseCurrency(candidate, out _) &&
            !IsFormulaInjectionMarkerText(candidate) &&
            !TryParseFiniteNumber(candidate, NumberStyles.Any, out _))
        {
            return false;
        }

        text = candidate.ToString();
        return true;
    }

    private static bool IsFormulaInjectionMarkerText(ReadOnlySpan<char> value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@';

    private static bool IsBooleanLikeText(ReadOnlySpan<char> value)
    {
        var trimmed = value.Trim();
        return trimmed.Equals("TRUE".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("FALSE".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadText(Stream stream)
    {
        if (stream is MemoryStream sourceMemoryStream &&
            sourceMemoryStream.TryGetBuffer(out var sourceBytes))
        {
            var position = Math.Min(sourceMemoryStream.Position, sourceMemoryStream.Length);
            var remainingLength = checked((int)(sourceMemoryStream.Length - position));
            sourceMemoryStream.Position = sourceMemoryStream.Length;
            return DecodeText(sourceBytes.AsSpan(checked((int)position), remainingLength));
        }

        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        if (!buffered.TryGetBuffer(out var bytes))
            throw new InvalidOperationException("Buffered delimited text stream is not accessible.");

        return DecodeText(bytes.AsSpan(0, checked((int)buffered.Length)));
    }

    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE &&
            bytes[2] == 0x00 &&
            bytes[3] == 0x00)
        {
            return Encoding.UTF32.GetString(bytes[4..]);
        }

        if (bytes.Length >= 4 &&
            bytes[0] == 0x00 &&
            bytes[1] == 0x00 &&
            bytes[2] == 0xFE &&
            bytes[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true)
                .GetString(bytes[4..]);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 &&
            bytes[0] == 0xFE &&
            bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        // A binary file (most commonly a ZIP/OOXML package such as .xlsx renamed to .csv/.txt -- the
        // mirror image of the .xlsx-renamed-from-CSV case WorkbookOpenTargetPlanner already guards
        // against) must never be silently decoded as if it were text. Without this guard, the strict
        // UTF-8 decode below throws a DecoderFallbackException on the binary bytes, gets caught, and
        // falls back to Windows-1252 -- which can decode ANY byte sequence without error -- so the
        // workbook "opens" full of mojibake/garbage cells split on stray delimiter-looking bytes
        // instead of surfacing a clear error. Reject up front instead.
        if (LooksLikeBinaryContent(bytes))
        {
            throw new InvalidDataException(
                "The file does not look like a text/CSV file. It may have been renamed from a " +
                "different, non-text file type (e.g. a ZIP/.xlsx package).");
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Mirror the writer's ResolveAnsiEncoding (DelimitedTextWorkbookWriter): a plain,
            // BOM-less .csv/.txt that isn't valid UTF-8 was almost certainly produced -- by this
            // app or by real Excel's plain "CSV (Comma delimited)" Save-As -- using the OS's
            // current-culture ANSI code page (e.g. 1252 on English Windows, 932/Shift-JIS on
            // Japanese, 1251/Cyrillic on Russian, 936/GBK on Chinese). Decoding with a hard-coded
            // Windows-1252 fallback regardless of locale would mojibake any non-Western-European
            // Windows install's own round-tripped files. Share the exact same resolution the
            // writer uses so Save-then-Open of a plain CSV/TXT is symmetric per-locale, just like
            // Excel's.
            return DelimitedTextWorkbookWriter.ResolveAnsiEncoding().GetString(bytes);
        }
    }

    /// <summary>
    /// Positively identifies binary (non-text) content via two cheap signals: the ZIP local-file-header
    /// magic ("PK", the same signature <c>WorkbookOpenTargetPlanner.LooksLikeZipPackage</c> checks for
    /// every OOXML package), and an embedded NUL byte, which never legitimately appears in delimited
    /// text but is common in other binary formats (e.g. OLE2 .xls). Only samples a bounded prefix for
    /// the NUL check so a huge misnamed binary file doesn't force a full-content scan.
    /// </summary>
    private static bool LooksLikeBinaryContent(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x50 && bytes[1] == 0x4B) // ZIP local-file-header "PK"
            return true;

        var sample = bytes.Length > 8000 ? bytes[..8000] : bytes;
        foreach (var b in sample)
        {
            if (b == 0)
                return true;
        }

        return false;
    }

    private static ScalarValue CoerceValue(DelimitedTextField field)
    {
        var value = field.AsSpan();
        var trimmed = value.Trim();
        if (trimmed.Equals("TRUE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (trimmed.Equals("FALSE".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (TryReadErrorLike(trimmed, out var error))
            return error;
        if (TryParseSimpleInteger(trimmed, out var integer))
            return new NumberValue(integer);
        if (TryParsePercentage(trimmed, out var percentage))
            return new NumberValue(percentage);
        if (TryParseCurrency(trimmed, out var currency))
            return new NumberValue(currency);
        // ISO-8601 datetimes (…T…Z / offset) must use the canonical UTC-normalized parse; handle
        // them here so the current-culture date check below never intercepts them and mis-normalizes
        // the time zone (DateTime.TryParse would adjust "Z" to local time). ISO datetimes never look
        // like a plain number, so taking them before the number parse changes nothing else.
        if (TryParseIsoDateTimeOffset(trimmed, out var isoDateTime))
            return DateTimeValue.FromDateTime(isoDateTime);
        // On locales where "." (or another number-group separator) doubles as the date separator
        // (e.g. de-DE, it-IT: group='.', date separator='.'), a dotted date like "31.12.2024" is
        // also a syntactically valid grouped number ("31,122,024") under NumberStyles.Any. Excel
        // treats such text as a date, so check for a genuine current-culture date first — the
        // digit-group heuristic plus a real DateTime.TryParse keeps unambiguous grouped numbers
        // (e.g. "1.234.567", "1.234,56") from being misparsed as dates.
        if (TryParseCurrentCultureDateTime(trimmed, out var cultureDateTime))
            return DateTimeValue.FromDateTime(cultureDateTime);
        if (TryParseFiniteNumber(trimmed, NumberStyles.Any, out var number))
        {
            return new NumberValue(ExcelNumericPrecision.CapSignificantDigits(number));
        }
        if (TryParseDateTime(trimmed, out var dateTime))
            return DateTimeValue.FromDateTime(dateTime);
        if (TryParseTime(trimmed, out var time))
            return new DateTimeValue(time.TotalDays);

        return new TextValue(field.Value);
    }

    private static bool TryReadErrorLike(ReadOnlySpan<char> field, out ErrorValue error)
    {
        if (field.Length > 0 && field[0] == '#')
            return TryReadError(field, out error);

        error = default!;
        return false;
    }

    private static bool TryReadError(ReadOnlySpan<char> field, out ErrorValue error)
    {
        foreach (var errorValue in ErrorValues)
        {
            if (field.Equals(errorValue.Key.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                error = errorValue.Value;
                return true;
            }
        }

        error = default!;
        return false;
    }

    private static bool TryParseDateTime(ReadOnlySpan<char> field, out DateTime dateTime)
    {
        var trimmed = field.Trim();
        if (TryParseIsoDateTimeOffset(trimmed, out dateTime))
        {
            return true;
        }

        if (TryParseCurrentCultureDateTime(trimmed, out dateTime))
        {
            return true;
        }

        return DateTime.TryParseExact(
            trimmed,
            DateTimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out dateTime);
    }

    private static bool TryParseCurrentCultureDateTime(ReadOnlySpan<char> field, out DateTime dateTime)
    {
        dateTime = default;
        if (field.Length == 0 ||
            string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name) ||
            !LooksLikeCurrentCultureDateCandidate(field))
        {
            return false;
        }

        // Clone so the two-digit-year window can be overridden to Excel's documented 1930-2029
        // rule (30-99 -> 19xx, 00-29 -> 20xx). .NET's default Calendar.TwoDigitYearMax is 2049,
        // which would misdate e.g. "6/15/45" to 2045 instead of Excel's 1945.
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;

        return DateTime.TryParse(
            field,
            culture,
            DateTimeStyles.NoCurrentDateDefault,
            out dateTime) &&
            dateTime.Date != DateTime.MinValue.Date;
    }

    // CSV/TXT import unconditionally treats '.' as a date separator (dotCountsAsDateSeparator:
    // true) and routes a standalone time literal through the separate TryParseTime step below
    // instead of through this date candidate, so colon never qualifies here on its own
    // (colonAlwaysQualifies: false). See DateEntryShapeRecognizer for the shared, single-source
    // implementation of this heuristic (also used by CellEntryParser and
    // TextToColumnsValueConverter for the typed-cell-entry and Text-to-Columns paths).
    private static bool LooksLikeCurrentCultureDateCandidate(ReadOnlySpan<char> field) =>
        DateEntryShapeRecognizer.LooksLikeDateCandidate(field, dotCountsAsDateSeparator: true, colonAlwaysQualifies: false);

    private static bool TryParseIsoDateTimeOffset(ReadOnlySpan<char> field, out DateTime dateTime)
    {
        if (DateTimeOffset.TryParseExact(
            field,
            DateTimeOffsetFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var offset))
        {
            dateTime = offset.UtcDateTime;
            return true;
        }

        dateTime = default;
        return false;
    }

    private static bool TryParseTime(ReadOnlySpan<char> field, out TimeSpan time)
    {
        if (TimeSpan.TryParseExact(
            field,
            TimeSpanFormats,
            CultureInfo.InvariantCulture,
            out time))
        {
            return true;
        }

        if (DateTime.TryParseExact(
            field,
            TimeOfDayFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.NoCurrentDateDefault,
            out var dateTime))
        {
            time = dateTime.TimeOfDay;
            return true;
        }

        return false;
    }

    private static bool TryReadFormula(ReadOnlySpan<char> field, out string formulaText)
    {
        formulaText = "";
        if (field.Length <= 1 || field[0] != '=')
            return false;

        formulaText = field[1..].ToString();
        return true;
    }

    private static bool TryParseSimpleInteger(ReadOnlySpan<char> field, out double value)
    {
        value = default;
        if (field.Length == 0)
            return false;

        var index = 0;
        var isNegative = false;
        if (field[index] is '+' or '-')
        {
            isNegative = field[index] == '-';
            index++;
            if (index == field.Length)
                return false;
        }

        var digitCount = field.Length - index;
        if (digitCount > 15)
            return false;

        long integer = 0;
        for (; index < field.Length; index++)
        {
            var digit = field[index] - '0';
            if ((uint)digit > 9)
                return false;

            integer = (integer * 10) + digit;
        }

        value = isNegative ? -integer : integer;
        return true;
    }

    private static bool TryParsePercentage(ReadOnlySpan<char> field, out double value)
    {
        value = default;
        if (field.Length < 2 || field[^1] != '%')
            return false;

        if (!TryParseFiniteNumber(field[..^1], NumberStyles.Any, out var number))
        {
            return false;
        }

        value = number / 100d;
        return true;
    }

    private static bool TryParseFiniteNumber(ReadOnlySpan<char> field, NumberStyles styles, out double value) =>
        TryParseFiniteNumber(field, styles, CultureInfo.CurrentCulture, out value) ||
        TryParseFiniteNumber(field, styles, CultureInfo.InvariantCulture, out value);

    private static bool TryParseFiniteNumber(
        ReadOnlySpan<char> field,
        NumberStyles styles,
        IFormatProvider formatProvider,
        out double value)
    {
        if (double.TryParse(field, styles, formatProvider, out value) &&
            double.IsFinite(value) &&
            HasValidGroupingShape(field, styles, formatProvider))
        {
            return true;
        }

        value = default;
        return false;
    }

    // .NET's NumberStyles.AllowThousands parsing does not validate that group separators actually
    // fall on 3-digit boundaries — e.g. under en-US, double.TryParse("1234,56", NumberStyles.Any, ...)
    // happily returns 123456, silently treating the fractional "56" as a malformed trailing group.
    // Under a culture whose decimal separator differs from '.', a genuine decimal-comma value like
    // "1234,56" would otherwise be misread as the grouped integer 123456 — a silent, severe data
    // corruption. Reject that shape here so the caller falls through to try the next culture/format
    // (see the two-culture TryParseFiniteNumber overload above) instead of accepting a bogus parse.
    private static bool HasValidGroupingShape(ReadOnlySpan<char> field, NumberStyles styles, IFormatProvider formatProvider)
    {
        if ((styles & NumberStyles.AllowThousands) == 0)
            return true;

        var numberFormat = NumberFormatInfo.GetInstance(formatProvider);
        var groupSeparator = numberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator))
            return true;

        var groupIndex = field.IndexOf(groupSeparator, StringComparison.Ordinal);
        if (groupIndex < 0)
            return true; // No grouping separator present — nothing to validate.

        var decimalSeparator = numberFormat.NumberDecimalSeparator;
        var decimalIndex = string.IsNullOrEmpty(decimalSeparator)
            ? -1
            : field.IndexOf(decimalSeparator, StringComparison.Ordinal);

        var integerPart = decimalIndex >= 0 ? field[..decimalIndex] : field;

        // Strip a parenthesized-negative wrapper, a leading sign, and (when the style allows one) a
        // leading currency symbol before scanning for digit groups. Without this, the symbol/paren is
        // the very first character the loop below sees, which is neither a digit nor the group
        // separator and so hits the "let styles decide" bailout further down — silently skipping
        // grouping validation for every currency string (e.g. "$1,2" would otherwise never be
        // checked and would parse as 12).
        integerPart = integerPart.Trim();
        if (integerPart.Length >= 2 && integerPart[0] == '(' && integerPart[^1] == ')')
            integerPart = integerPart[1..^1].Trim();
        if (integerPart.Length > 0 && (integerPart[0] == '+' || integerPart[0] == '-'))
            integerPart = integerPart[1..];

        if ((styles & NumberStyles.AllowCurrencySymbol) != 0)
        {
            var currencySymbol = numberFormat.CurrencySymbol;
            if (!string.IsNullOrEmpty(currencySymbol))
            {
                var symbolIndex = integerPart.IndexOf(currencySymbol, StringComparison.Ordinal);
                if (symbolIndex >= 0 && integerPart[..symbolIndex].Trim().Length == 0)
                    integerPart = integerPart[(symbolIndex + currencySymbol.Length)..].TrimStart();
            }
        }

        var groups = new List<int>();
        var currentGroupDigits = 0;
        var index = 0;
        while (index < integerPart.Length)
        {
            if (integerPart[index..].StartsWith(groupSeparator, StringComparison.Ordinal))
            {
                groups.Add(currentGroupDigits);
                currentGroupDigits = 0;
                index += groupSeparator.Length;
                continue;
            }

            if (!char.IsDigit(integerPart[index]))
                return true; // Not a plain grouped-digit shape (e.g. currency symbols) — let styles decide.

            currentGroupDigits++;
            index++;
        }

        groups.Add(currentGroupDigits);

        // Valid Excel/`.NET`-style grouping: every group except the first has exactly 3 digits, and
        // the first group has 1-3 digits.
        if (groups[0] is < 1 or > 3)
            return false;

        for (var i = 1; i < groups.Count; i++)
        {
            if (groups[i] != 3)
                return false;
        }

        return true;
    }

    private static bool TryParseCurrency(ReadOnlySpan<char> field, out double value)
    {
        value = default;
        if (field.IndexOf('$') < 0)
            return false;

        var currencyCulture = CultureInfo.GetCultureInfo("en-US");
        return double.TryParse(
            field,
            NumberStyles.Currency,
            currencyCulture,
            out value) &&
            double.IsFinite(value) &&
            // Same shape check the plain-number path applies (HasValidGroupingShape strips the
            // currency symbol/parens itself before scanning), so a malformed grouping like "$1,2"
            // is rejected here instead of silently parsing as 12.
            HasValidGroupingShape(field, NumberStyles.Currency, currencyCulture);
    }
}
