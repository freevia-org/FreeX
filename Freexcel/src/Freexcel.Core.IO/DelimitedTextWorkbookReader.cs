using System.Globalization;
using System.Text;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class DelimitedTextWorkbookReader
{
    public static Workbook Load(Stream stream, char delimiter, bool allowSeparatorDirective = false)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        using var reader = CreateTextReader(stream);
        uint row = 1;
        var canReadSeparatorDirective = allowSeparatorDirective;
        while (TryReadRecord(reader, delimiter, out var fields))
        {
            if (row > CellAddress.MaxRow)
                break;

            if (canReadSeparatorDirective && TryReadSeparatorDirective(fields, out var directiveDelimiter))
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

                var field = fields[i].Value;
                if (field.Length == 0)
                    continue;

                var address = new CellAddress(sheet.Id, row, (uint)(i + 1));
                if (!fields[i].WasQuoted && TryReadFormula(field, out var formulaText))
                    sheet.SetCell(address, Cell.FromFormula(formulaText));
                else if (ShouldPreserveQuotedFormulaLikeText(fields[i]))
                    sheet.SetCell(address, new TextValue(field));
                else
                    sheet.SetCell(address, CoerceValue(field));
            }

            row++;
        }

        return workbook;
    }

    private static bool TryReadSeparatorDirective(IReadOnlyList<DelimitedTextField> fields, out char delimiter)
    {
        delimiter = default;

        if (fields.Count == 2 &&
            string.Equals(fields[0].Value, "sep=", StringComparison.OrdinalIgnoreCase) &&
            fields[1].Value.Length == 0)
        {
            delimiter = ',';
            return true;
        }

        if (fields.Count != 1)
            return false;

        var directive = fields[0].Value;
        if (!directive.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) || directive.Length != 5)
            return false;

        delimiter = directive[4];
        return delimiter is not '\r' and not '\n';
    }

    internal static bool TryReadRecord(TextReader reader, char delimiter, out List<DelimitedTextField> fields)
    {
        fields = [];
        var current = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;
        var currentWasQuoted = false;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            var c = (char)ch;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
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
                fields.Add(new DelimitedTextField(current.ToString(), currentWasQuoted));
                current.Clear();
                currentWasQuoted = false;
                atFieldStart = true;
            }
            else if (c == '\r')
            {
                if (reader.Peek() == '\n')
                    reader.Read();
                fields.Add(new DelimitedTextField(current.ToString(), currentWasQuoted));
                return true;
            }
            else if (c == '\n')
            {
                fields.Add(new DelimitedTextField(current.ToString(), currentWasQuoted));
                return true;
            }
            else
            {
                current.Append(c);
                atFieldStart = false;
            }
        }

        if (current.Length > 0 || fields.Count > 0)
        {
            fields.Add(new DelimitedTextField(current.ToString(), currentWasQuoted));
            return true;
        }

        return false;
    }

    internal readonly record struct DelimitedTextField(string Value, bool WasQuoted);

    private static bool ShouldPreserveQuotedFormulaLikeText(DelimitedTextField field)
    {
        if (!field.WasQuoted || field.Value.Length == 0)
            return false;

        return field.Value[0] switch
        {
            '=' or '@' => true,
            '+' or '-' => double.TryParse(field.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }

    private static TextReader CreateTextReader(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        if (!memory.TryGetBuffer(out var bytes))
            throw new InvalidOperationException("Buffered delimited text stream is not accessible.");

        return new StringReader(DecodeText(bytes.AsSpan()));
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

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static ScalarValue CoerceValue(string field)
    {
        if (string.Equals(field, "TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(true);
        if (string.Equals(field, "FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolValue(false);
        if (TryReadError(field, out var error))
            return error;
        if (TryParsePercentage(field, out var percentage))
            return new NumberValue(percentage);
        if (TryParseIsoDateTime(field, out var dateTime))
            return DateTimeValue.FromDateTime(dateTime);
        if (TryParseTime(field, out var time))
            return new DateTimeValue(time.TotalDays);
        if (TryParseCurrency(field, out var currency))
            return new NumberValue(currency);
        if (double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return new NumberValue(number);

        return new TextValue(field);
    }

    private static bool TryReadError(string field, out ErrorValue error)
    {
        error = field.Trim().ToUpperInvariant() switch
        {
            "#DIV/0!" => ErrorValue.DivByZero,
            "#VALUE!" => ErrorValue.Value,
            "#REF!" => ErrorValue.Ref,
            "#NAME?" => ErrorValue.Name,
            "#NULL!" => ErrorValue.Null,
            "#N/A" => ErrorValue.NA,
            "#NUM!" => ErrorValue.Num,
            "#SPILL!" => ErrorValue.Spill,
            "#CALC!" => ErrorValue.Calc,
            _ => null!
        };

        return error is not null;
    }

    private static bool TryParseIsoDateTime(string field, out DateTime dateTime)
    {
        var trimmed = field.Trim();
        return DateTime.TryParseExact(
            trimmed,
            DateTimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out dateTime);
    }

    private static bool TryParseTime(string field, out TimeSpan time)
    {
        var trimmed = field.Trim();
        if (TimeSpan.TryParseExact(
            trimmed,
            TimeSpanFormats,
            CultureInfo.InvariantCulture,
            out time))
        {
            return true;
        }

        if (DateTime.TryParseExact(
            trimmed,
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

    private static bool TryReadFormula(string field, out string formulaText)
    {
        formulaText = "";
        if (field.Length <= 1 || field[0] != '=')
            return false;

        formulaText = field[1..];
        return true;
    }

    private static bool TryParsePercentage(string field, out double value)
    {
        value = default;
        var trimmed = field.Trim();
        if (trimmed.Length < 2 || trimmed[^1] != '%')
            return false;

        if (!double.TryParse(trimmed[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            return false;

        value = number / 100d;
        return true;
    }

    private static bool TryParseCurrency(string field, out double value)
    {
        value = default;
        var trimmed = field.Trim();
        if (!trimmed.Contains('$', StringComparison.Ordinal))
            return false;

        return double.TryParse(
            trimmed,
            NumberStyles.Currency,
            CultureInfo.GetCultureInfo("en-US"),
            out value);
    }
}
