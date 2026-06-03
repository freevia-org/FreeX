using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    [JsonConverter(typeof(CellDtoJsonConverter))]
    private class CellDto
    {
        public string Address { get; set; } = "";
        [JsonIgnore]
        public ulong ParsedAddress { get; set; }
        [JsonIgnore]
        public char ParsedValueType { get; set; }
        public string? Value { get; set; }
        public string? ValueType { get; set; }
        public string? Formula { get; set; }
        public bool IgnoreFormulaError { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? StyleId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellStyleDto? Style { get; set; }
    }

    [JsonConverter(typeof(StyleOnlyCellDtoJsonConverter))]
    private class StyleOnlyCellDto
    {
        public string? Address { get; set; }
        [JsonIgnore]
        public ulong ParsedAddress { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? StyleId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CellStyleDto? Style { get; set; }
    }

    [JsonConverter(typeof(CellDtoSequenceJsonConverter))]
    private sealed class CellDtoSequence : IEnumerable<CellDto?>
    {
        public static CellDtoSequence Empty { get; } = new([]);

        private readonly IReadOnlyList<CellDto?>? _items;

        public CellDtoSequence(Sheet sourceSheet)
        {
            SourceSheet = sourceSheet;
        }

        private CellDtoSequence(IReadOnlyList<CellDto?> items)
        {
            _items = items;
        }

        public Sheet? SourceSheet { get; }

        public int Count => _items?.Count ?? SourceSheet?.CellCount ?? 0;

        public static CellDtoSequence FromItems(List<CellDto?> items)
            => items.Count == 0 ? Empty : new CellDtoSequence(items);

        public IEnumerator<CellDto?> GetEnumerator()
            => (_items ?? []).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    [JsonConverter(typeof(StyleOnlyCellDtoSequenceJsonConverter))]
    private sealed class StyleOnlyCellDtoSequence : IEnumerable<StyleOnlyCellDto?>
    {
        public static StyleOnlyCellDtoSequence Empty { get; } = new([]);

        private readonly IReadOnlyList<StyleOnlyCellDto?>? _items;

        public StyleOnlyCellDtoSequence(Sheet sourceSheet)
        {
            SourceSheet = sourceSheet;
        }

        private StyleOnlyCellDtoSequence(IReadOnlyList<StyleOnlyCellDto?> items)
        {
            _items = items;
        }

        public Sheet? SourceSheet { get; }

        public static StyleOnlyCellDtoSequence FromItems(List<StyleOnlyCellDto?> items)
            => items.Count == 0 ? Empty : new StyleOnlyCellDtoSequence(items);

        public IEnumerator<StyleOnlyCellDto?> GetEnumerator()
            => (_items ?? []).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private static bool TryReadCellAddressToken(ref Utf8JsonReader reader, out uint row, out uint col)
    {
        if (reader.HasValueSequence)
        {
            row = 0;
            col = 0;
            return false;
        }

        return TryParseCellAddressUtf8(reader.ValueSpan, out row, out col);
    }

    private static ulong PackCellAddress(uint row, uint col) =>
        ((ulong)row << 32) | col;

    private static bool TryParseCellAddressUtf8(ReadOnlySpan<byte> value, out uint row, out uint col)
    {
        row = 0;
        col = 0;
        var index = 0;
        var columnStart = index;
        while (index < value.Length)
        {
            var c = value[index];
            var columnDigit = (uint)(c - 'A');
            if (columnDigit > 25)
            {
                columnDigit = (uint)(c - 'a');
                if (columnDigit > 25)
                    break;
            }

            col = col * 26 + columnDigit + 1;
            if (col > CellAddress.MaxCol)
                return false;
            index++;
        }

        if (index == columnStart)
            return false;

        var rowStart = index;
        while (index < value.Length)
        {
            var c = value[index];
            var digit = (uint)(c - '0');
            if (digit > 9)
                return false;

            if (row > CellAddress.MaxRow / 10 || row == CellAddress.MaxRow / 10 && digit > CellAddress.MaxRow % 10)
                return false;

            row = row * 10 + digit;
            index++;
        }

        return index > rowStart && row > 0;
    }

    private sealed class CellDtoSequenceJsonConverter : JsonConverter<CellDtoSequence>
    {
        public override CellDtoSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return CellDtoSequence.Empty;
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            var cells = new List<CellDto?>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return CellDtoSequence.FromItems(cells);
                if (reader.TokenType == JsonTokenType.Null)
                {
                    cells.Add(null);
                    continue;
                }

                cells.Add(JsonSerializer.Deserialize<CellDto>(ref reader, options));
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, CellDtoSequence value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            if (value.SourceSheet is { } sheet)
                WriteCellDtos(writer, sheet, options);
            else
            {
                foreach (var cell in value)
                {
                    if (cell is null)
                        writer.WriteNullValue();
                    else
                        CellDtoJsonConverter.WriteCell(writer, cell, options);
                }
            }

            writer.WriteEndArray();
        }
    }

    private sealed class StyleOnlyCellDtoSequenceJsonConverter : JsonConverter<StyleOnlyCellDtoSequence>
    {
        public override StyleOnlyCellDtoSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return StyleOnlyCellDtoSequence.Empty;
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException();

            var cells = new List<StyleOnlyCellDto?>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return StyleOnlyCellDtoSequence.FromItems(cells);
                if (reader.TokenType == JsonTokenType.Null)
                {
                    cells.Add(null);
                    continue;
                }

                cells.Add(JsonSerializer.Deserialize<StyleOnlyCellDto>(ref reader, options));
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, StyleOnlyCellDtoSequence value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            if (value.SourceSheet is { } sheet)
                WriteStyleOnlyCellDtos(writer, sheet, options);
            else
            {
                foreach (var cell in value)
                {
                    if (cell is null)
                        writer.WriteNullValue();
                    else
                        StyleOnlyCellDtoJsonConverter.WriteCell(writer, cell, options);
                }
            }

            writer.WriteEndArray();
        }
    }

    private sealed class CellDtoJsonConverter : JsonConverter<CellDto>
    {
        private static ReadOnlySpan<byte> AddressProperty => "Address"u8;
        private static ReadOnlySpan<byte> ValueProperty => "Value"u8;
        private static ReadOnlySpan<byte> ValueTypeProperty => "ValueType"u8;
        private static ReadOnlySpan<byte> FormulaProperty => "Formula"u8;
        private static ReadOnlySpan<byte> IgnoreFormulaErrorProperty => "IgnoreFormulaError"u8;
        private static ReadOnlySpan<byte> StyleIdProperty => "StyleId"u8;
        private static ReadOnlySpan<byte> StyleProperty => "Style"u8;
        private static ReadOnlySpan<byte> NumberValueType => "n"u8;
        private static ReadOnlySpan<byte> DateTimeValueType => "d"u8;
        private static ReadOnlySpan<byte> BooleanValueType => "b"u8;
        private static ReadOnlySpan<byte> TextValueType => "t"u8;
        private static ReadOnlySpan<byte> ErrorValueType => "e"u8;
        private static readonly JsonEncodedText AddressName = JsonEncodedText.Encode(nameof(CellDto.Address));
        private static readonly JsonEncodedText ValueName = JsonEncodedText.Encode(nameof(CellDto.Value));
        private static readonly JsonEncodedText ValueTypeName = JsonEncodedText.Encode(nameof(CellDto.ValueType));
        private static readonly JsonEncodedText FormulaName = JsonEncodedText.Encode(nameof(CellDto.Formula));
        private static readonly JsonEncodedText IgnoreFormulaErrorName = JsonEncodedText.Encode(nameof(CellDto.IgnoreFormulaError));
        private static readonly JsonEncodedText StyleIdName = JsonEncodedText.Encode(nameof(CellDto.StyleId));
        private static readonly JsonEncodedText StyleName = JsonEncodedText.Encode(nameof(CellDto.Style));
        private static readonly StandardFormat GeneralNumberFormat = StandardFormat.Parse("G");
        public const int MaxCellAddressTextLength = 10;

        public override CellDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = new CellDto();
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return dto;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                if (reader.ValueTextEquals(AddressProperty))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        dto.Address = "";
                    }
                    else if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException();
                    }
                    else if (TryReadCellAddressToken(ref reader, out var row, out var col))
                    {
                        dto.ParsedAddress = PackCellAddress(row, col);
                    }
                    else
                    {
                        dto.Address = reader.GetString() ?? "";
                    }
                }
                else if (reader.ValueTextEquals(ValueProperty))
                {
                    reader.Read();
                    dto.Value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                }
                else if (reader.ValueTextEquals(ValueTypeProperty))
                {
                    reader.Read();
                    dto.ParsedValueType = ReadValueTypeToken(ref reader, out var valueType);
                    dto.ValueType = valueType;
                }
                else if (reader.ValueTextEquals(FormulaProperty))
                {
                    reader.Read();
                    dto.Formula = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                }
                else if (reader.ValueTextEquals(IgnoreFormulaErrorProperty))
                {
                    reader.Read();
                    if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                        dto.IgnoreFormulaError = reader.GetBoolean();
                    else
                        reader.Skip();
                }
                else if (reader.ValueTextEquals(StyleIdProperty))
                {
                    reader.Read();
                    dto.StyleId = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var styleId)
                        ? styleId
                        : null;
                    if (reader.TokenType is not (JsonTokenType.Number or JsonTokenType.Null))
                        reader.Skip();
                }
                else if (reader.ValueTextEquals(StyleProperty))
                {
                    reader.Read();
                    dto.Style = reader.TokenType == JsonTokenType.Null
                        ? null
                        : JsonSerializer.Deserialize<CellStyleDto>(ref reader, options);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            throw new JsonException();
        }

        private static char ReadValueTypeToken(ref Utf8JsonReader reader, out string? valueType)
        {
            valueType = null;
            if (reader.TokenType == JsonTokenType.Null)
                return '\0';
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return '\0';
            }

            if (reader.ValueTextEquals(NumberValueType))
                return 'n';
            if (reader.ValueTextEquals(DateTimeValueType))
                return 'd';
            if (reader.ValueTextEquals(BooleanValueType))
                return 'b';
            if (reader.ValueTextEquals(TextValueType))
                return 't';
            if (reader.ValueTextEquals(ErrorValueType))
                return 'e';

            valueType = reader.GetString();
            return '\0';
        }

        public override void Write(Utf8JsonWriter writer, CellDto value, JsonSerializerOptions options)
            => WriteCell(writer, value, options);

        public static void WriteCell(Utf8JsonWriter writer, CellDto value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(AddressName, value.Address);
            WriteCellPayload(writer, value, options);
            writer.WriteEndObject();
        }

        public static void WriteCell(Utf8JsonWriter writer, CellDto value, JsonSerializerOptions options, uint row, uint col)
        {
            writer.WriteStartObject();
            WriteAddress(writer, row, col);
            WriteCellPayload(writer, value, options);
            writer.WriteEndObject();
        }

        public static void WriteCell(
            Utf8JsonWriter writer,
            ScalarValue value,
            string? formula,
            bool ignoreFormulaError,
            int? styleId,
            CellStyleDto? style,
            JsonSerializerOptions options,
            uint row,
            uint col)
        {
            writer.WriteStartObject();
            WriteAddress(writer, row, col);
            WriteScalarValuePayload(writer, value);
            if (formula is not null)
                writer.WriteString(FormulaName, formula);
            if (ignoreFormulaError)
                writer.WriteBoolean(IgnoreFormulaErrorName, ignoreFormulaError);
            if (styleId is { } nativeStyleId)
                writer.WriteNumber(StyleIdName, nativeStyleId);
            if (style is not null)
            {
                writer.WritePropertyName(StyleName);
                JsonSerializer.Serialize(writer, style, options);
            }
            writer.WriteEndObject();
        }

        private static void WriteCellPayload(Utf8JsonWriter writer, CellDto value, JsonSerializerOptions options)
        {
            if (value.Value is not null)
                writer.WriteString(ValueName, value.Value);
            if (value.ValueType is not null)
                writer.WriteString(ValueTypeName, value.ValueType);
            if (value.Formula is not null)
                writer.WriteString(FormulaName, value.Formula);
            if (value.IgnoreFormulaError)
                writer.WriteBoolean(IgnoreFormulaErrorName, value.IgnoreFormulaError);
            if (value.StyleId is { } styleId)
                writer.WriteNumber(StyleIdName, styleId);
            if (value.Style is not null)
            {
                writer.WritePropertyName(StyleName);
                JsonSerializer.Serialize(writer, value.Style, options);
            }
        }

        private static void WriteScalarValuePayload(Utf8JsonWriter writer, ScalarValue value)
        {
            switch (value)
            {
                case BlankValue:
                    return;
                case NumberValue number:
                    writer.WritePropertyName(ValueName);
                    WriteNumberStringValue(writer, number.Value);
                    writer.WriteString(ValueTypeName, double.IsFinite(number.Value) ? "n" : "t");
                    return;
                case DateTimeValue dateTime:
                    writer.WritePropertyName(ValueName);
                    WriteNumberStringValue(writer, dateTime.Value);
                    writer.WriteString(ValueTypeName, double.IsFinite(dateTime.Value) ? "d" : "t");
                    return;
                case BoolValue boolean:
                    writer.WriteString(ValueName, boolean.Value ? "TRUE" : "FALSE");
                    writer.WriteString(ValueTypeName, "b");
                    return;
                case TextValue text:
                    writer.WriteString(ValueName, text.Value);
                    writer.WriteString(ValueTypeName, "t");
                    return;
                case ErrorValue error:
                    writer.WriteString(ValueName, error.Code);
                    writer.WriteString(ValueTypeName, "e");
                    return;
            }
        }

        private static void WriteNumberStringValue(Utf8JsonWriter writer, double value)
        {
            if (value is >= -999_999_999 and <= 999_999_999 &&
                value == Math.Truncate(value))
            {
                WriteSmallIntegerStringValue(writer, (int)value);
                return;
            }

            Span<byte> buffer = stackalloc byte[34];
            buffer[0] = (byte)'"';
            if (Utf8Formatter.TryFormat(value, buffer[1..^1], out var bytesWritten, GeneralNumberFormat))
            {
                buffer[bytesWritten + 1] = (byte)'"';
                writer.WriteRawValue(buffer[..(bytesWritten + 2)], skipInputValidation: true);
            }
            else
            {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void WriteSmallIntegerStringValue(Utf8JsonWriter writer, int value)
        {
            Span<byte> buffer = stackalloc byte[13];
            buffer[0] = (byte)'"';
            var index = buffer.Length - 1;
            var magnitude = value < 0 ? (uint)-value : (uint)value;
            do
            {
                buffer[--index] = (byte)('0' + magnitude % 10);
                magnitude /= 10;
            }
            while (magnitude > 0);

            if (value < 0)
                buffer[--index] = (byte)'-';

            var valueLength = buffer.Length - 1 - index;
            buffer[index..^1].CopyTo(buffer[1..]);
            buffer[valueLength + 1] = (byte)'"';
            writer.WriteRawValue(buffer[..(valueLength + 2)], skipInputValidation: true);
        }

        private static void WriteAddress(Utf8JsonWriter writer, uint row, uint col)
        {
            Span<char> address = stackalloc char[MaxCellAddressTextLength];
            var length = FormatAddress(address, row, col);
            writer.WritePropertyName(AddressName);
            writer.WriteStringValue(address[..length]);
        }

        public static int FormatAddress(Span<char> destination, uint row, uint col)
        {
            var columnLength = GetColumnNameLength(col);
            WriteColumnName(col, destination[..columnLength]);

            var rowLength = GetRowDigitCount(row);
            var rowIndex = columnLength + rowLength;
            do
            {
                destination[--rowIndex] = (char)('0' + row % 10);
                row /= 10;
            }
            while (row > 0);

            return columnLength + rowLength;
        }

        private static int GetColumnNameLength(uint col) =>
            col <= 26 ? 1 :
            col <= 702 ? 2 : 3;

        private static void WriteColumnName(uint col, Span<char> destination)
        {
            for (var index = destination.Length - 1; index >= 0; index--)
            {
                col--;
                destination[index] = (char)('A' + col % 26);
                col /= 26;
            }
        }

        private static int GetRowDigitCount(uint row) =>
            row < 10 ? 1 :
            row < 100 ? 2 :
            row < 1_000 ? 3 :
            row < 10_000 ? 4 :
            row < 100_000 ? 5 :
            row < 1_000_000 ? 6 : 7;
    }

    private sealed class StyleOnlyCellDtoJsonConverter : JsonConverter<StyleOnlyCellDto>
    {
        private static ReadOnlySpan<byte> AddressProperty => "Address"u8;
        private static ReadOnlySpan<byte> StyleIdProperty => "StyleId"u8;
        private static ReadOnlySpan<byte> StyleProperty => "Style"u8;
        private static readonly JsonEncodedText AddressName = JsonEncodedText.Encode(nameof(StyleOnlyCellDto.Address));
        private static readonly JsonEncodedText StyleIdName = JsonEncodedText.Encode(nameof(StyleOnlyCellDto.StyleId));
        private static readonly JsonEncodedText StyleName = JsonEncodedText.Encode(nameof(StyleOnlyCellDto.Style));

        public override StyleOnlyCellDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = new StyleOnlyCellDto();
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return dto;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                if (reader.ValueTextEquals(AddressProperty))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        dto.Address = null;
                    }
                    else if (reader.TokenType != JsonTokenType.String)
                    {
                        throw new JsonException();
                    }
                    else if (TryReadCellAddressToken(ref reader, out var row, out var col))
                    {
                        dto.ParsedAddress = PackCellAddress(row, col);
                    }
                    else
                    {
                        dto.Address = reader.GetString();
                    }
                }
                else if (reader.ValueTextEquals(StyleIdProperty))
                {
                    reader.Read();
                    dto.StyleId = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var styleId)
                        ? styleId
                        : null;
                    if (reader.TokenType is not (JsonTokenType.Number or JsonTokenType.Null))
                        reader.Skip();
                }
                else if (reader.ValueTextEquals(StyleProperty))
                {
                    reader.Read();
                    dto.Style = reader.TokenType == JsonTokenType.Null
                        ? null
                        : JsonSerializer.Deserialize<CellStyleDto>(ref reader, options);
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, StyleOnlyCellDto value, JsonSerializerOptions options)
            => WriteCell(writer, value, options);

        public static void WriteCell(Utf8JsonWriter writer, StyleOnlyCellDto value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString(AddressName, value.Address);
            WriteCellPayload(writer, value, options);
            writer.WriteEndObject();
        }

        public static void WriteCell(Utf8JsonWriter writer, StyleOnlyCellDto value, JsonSerializerOptions options, uint row, uint col)
        {
            writer.WriteStartObject();
            WriteAddress(writer, row, col);
            WriteCellPayload(writer, value, options);
            writer.WriteEndObject();
        }

        private static void WriteCellPayload(Utf8JsonWriter writer, StyleOnlyCellDto value, JsonSerializerOptions options)
        {
            if (value.StyleId is { } styleId)
                writer.WriteNumber(StyleIdName, styleId);
            if (value.Style is not null)
            {
                writer.WritePropertyName(StyleName);
                JsonSerializer.Serialize(writer, value.Style, options);
            }
        }

        private static void WriteAddress(Utf8JsonWriter writer, uint row, uint col)
        {
            Span<char> address = stackalloc char[CellDtoJsonConverter.MaxCellAddressTextLength];
            var length = CellDtoJsonConverter.FormatAddress(address, row, col);
            writer.WritePropertyName(AddressName);
            writer.WriteStringValue(address[..length]);
        }
    }
}
