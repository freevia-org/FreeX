using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// dBASE (.dbf) file adapter — READ-ONLY, matching Excel which opens but no longer writes DBF.
///
/// A DBF file is a fixed-layout binary table:
/// <list type="bullet">
///   <item>A 32-byte file header (version byte, last-update Y/M/D, record count, header length, record
///   length, plus a language-driver/code-page byte at offset 29).</item>
///   <item>An array of 32-byte field descriptors (11-byte name, 1-byte type, 1-byte length, 1-byte
///   decimal count), terminated by a <c>0x0D</c> byte.</item>
///   <item>Fixed-width records, each prefixed with a 1-byte deletion flag (<c>0x20</c> = live,
///   <c>0x2A</c> '*' = deleted), then each field's bytes laid out left-to-right.</item>
/// </list>
/// Field types mapped: <c>C</c> text, <c>N</c>/<c>F</c>/<c>B</c>/<c>O</c> numeric, <c>D</c> date
/// (<c>yyyymmdd</c>), <c>L</c> logical (<c>T/Y</c> / <c>F/N</c>), <c>I</c>/<c>+</c> 32-bit integer,
/// <c>T</c>/<c>@</c> datetime (Julian day + ms). <c>M</c>/<c>G</c>/<c>P</c> (memo/general/picture)
/// reference an external .dbt/.fpt block and are emitted blank. The field names form a header row;
/// each record becomes a data row beneath it.
/// </summary>
public sealed class DbfFileAdapter : IFileAdapter
{
    private const byte FieldDescriptorTerminator = 0x0D;
    private const byte HeaderTerminatorFallback = 0x1A; // EOF marker some writers place mid-stream
    private const byte RecordDeleted = (byte)'*';

    public string Extension => ".dbf";
    public string FormatName => "dBASE (DBF)";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        // Read-only: Excel opens .dbf but its DBF writer is deprecated, so we mirror that (CanSave:false).
        new FileFormatDescriptor(".dbf", "dBASE (DBF)", CanOpen: true, CanSave: false)
    ];

    public Workbook Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");

        var bytes = ReadAllBytes(stream);
        if (bytes.Length < 32)
            return workbook; // not a valid DBF header — yield an empty sheet rather than throwing

        ushort headerLength = (ushort)(bytes[8] | (bytes[9] << 8));
        ushort recordLength = (ushort)(bytes[10] | (bytes[11] << 8));
        uint recordCount = (uint)(bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24));
        var encoding = ResolveEncoding(bytes[29]);

        var fields = ReadFieldDescriptors(bytes, headerLength, encoding);
        if (fields.Count == 0)
            return workbook;

        // Header row: field names.
        for (var c = 0; c < fields.Count; c++)
        {
            if (c >= CellAddress.MaxCol) break;
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(c + 1)), new TextValue(fields[c].Name));
        }

        if (headerLength <= 0 || recordLength <= 0 || headerLength >= bytes.Length)
            return workbook;

        // Bound the record count to what the file can actually hold (defends against a corrupt count).
        long maxRecordsByLength = (bytes.Length - headerLength) / recordLength;
        long records = Math.Min(recordCount, Math.Max(0, maxRecordsByLength));

        uint outRow = 1; // 1 = header; data rows start at 2
        for (long r = 0; r < records; r++)
        {
            int recordStart = headerLength + checked((int)(r * recordLength));
            if (recordStart + recordLength > bytes.Length)
                break;

            byte flag = bytes[recordStart];
            if (flag == RecordDeleted)
                continue; // skip records tombstoned with '*'

            outRow++;
            if (outRow > CellAddress.MaxRow)
                break;

            int fieldOffset = recordStart + 1; // +1 for the deletion flag
            for (var c = 0; c < fields.Count; c++)
            {
                var field = fields[c];
                // A crafted/corrupt file can declare field-descriptor widths whose sum exceeds the
                // header's own recordLength, so the record-boundary guard above (recordStart +
                // recordLength <= bytes.Length) doesn't protect this field-by-field walk. Bounds-check
                // each field read individually so a bad descriptor can't run past the buffer.
                if (fieldOffset + field.Length > bytes.Length)
                    break;

                if (c < CellAddress.MaxCol)
                {
                    var raw = encoding.GetString(bytes, fieldOffset, field.Length);
                    var value = ConvertField(field, raw);
                    if (value is not BlankValue)
                        sheet.SetCell(new CellAddress(sheet.Id, outRow, (uint)(c + 1)), Cell.FromValue(value));
                }

                fieldOffset += field.Length;
            }
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException("DBF is read-only in FreeX (Excel's DBF writer is deprecated).");

    // ---- header / field parsing -------------------------------------------------------------------

    private sealed record DbfField(string Name, char Type, int Length, int DecimalCount);

    private static List<DbfField> ReadFieldDescriptors(byte[] bytes, int headerLength, Encoding encoding)
    {
        var fields = new List<DbfField>();
        // Field descriptors begin at offset 32 and run to the 0x0D terminator (or the header length).
        int limit = headerLength > 0 ? Math.Min(headerLength, bytes.Length) : bytes.Length;
        int pos = 32;
        while (pos + 32 <= limit)
        {
            byte first = bytes[pos];
            if (first is FieldDescriptorTerminator or HeaderTerminatorFallback or 0x00)
                break;

            // Name: up to 11 bytes, NUL-terminated.
            int nameLen = 0;
            while (nameLen < 11 && bytes[pos + nameLen] != 0x00)
                nameLen++;
            var name = encoding.GetString(bytes, pos, nameLen).Trim();

            char type = (char)bytes[pos + 11];
            int length = bytes[pos + 16];
            int decimals = bytes[pos + 17];

            // For the wide numeric/character types the length field is a single byte; for 'C' some
            // dialects encode width >255 across bytes 16-17, but dBASE III/IV (our target) uses one byte.
            if (length <= 0)
            {
                pos += 32;
                continue;
            }

            fields.Add(new DbfField(name, char.ToUpperInvariant(type), length, decimals));
            pos += 32;
        }

        return fields;
    }

    private static ScalarValue ConvertField(DbfField field, string raw)
    {
        switch (field.Type)
        {
            case 'C': // character
            {
                var trimmed = raw.TrimEnd();
                return trimmed.Length == 0 ? BlankValue.Instance : new TextValue(trimmed);
            }
            case 'N': // numeric (ASCII-formatted, right-justified)
            case 'F': // float
            case 'B': // double (dBASE: ASCII; some store binary, but ASCII is the III/IV norm)
            case 'O': // legacy "ordinal" — treat as ASCII numeric
            {
                var token = raw.Trim();
                if (token.Length == 0)
                    return BlankValue.Instance;
                return double.TryParse(token, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var num) && double.IsFinite(num)
                    ? new NumberValue(num)
                    : new TextValue(token);
            }
            case 'I': // 4-byte little-endian integer (binary)
            case '+': // autoincrement (4-byte integer)
            {
                return ParseBinaryInt32(raw);
            }
            case 'D': // date: 8 chars yyyymmdd
            {
                var token = raw.Trim();
                if (token.Length == 8 &&
                    DateTime.TryParseExact(token, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    return DateTimeValue.FromDateTime(date);
                }
                return token.Length == 0 ? BlankValue.Instance : new TextValue(token);
            }
            case 'L': // logical: T/t/Y/y true, F/f/N/n false, ? unknown
            {
                var token = raw.Trim();
                if (token.Length == 0)
                    return BlankValue.Instance;
                char c = char.ToUpperInvariant(token[0]);
                return c switch
                {
                    'T' or 'Y' => new BoolValue(true),
                    'F' or 'N' => new BoolValue(false),
                    _ => BlankValue.Instance, // '?' = undefined
                };
            }
            case 'M': // memo
            case 'G': // general / OLE
            case 'P': // picture
                // The field stores a block reference into an external .dbt/.fpt; without that companion
                // file the text is unavailable, so emit blank (the spec's "memo → skip/blank").
                return BlankValue.Instance;
            default:
            {
                var trimmed = raw.TrimEnd();
                return trimmed.Length == 0 ? BlankValue.Instance : new TextValue(trimmed);
            }
        }
    }

    private static ScalarValue ParseBinaryInt32(string raw)
    {
        // The raw string was decoded from 4 bytes; recover them via the code-page-independent low bytes.
        // We re-extract the underlying bytes by char code (each char came from a single-byte code page).
        if (raw.Length < 4)
            return BlankValue.Instance;
        int v = (raw[0] & 0xFF) | ((raw[1] & 0xFF) << 8) | ((raw[2] & 0xFF) << 16) | ((raw[3] & 0xFF) << 24);
        return new NumberValue(v);
    }

    private static Encoding ResolveEncoding(byte languageDriver)
    {
        // Map the most common DBF language-driver bytes to code pages; default to Windows-1252.
        int codePage = languageDriver switch
        {
            0x01 => 437,   // U.S. MS-DOS
            0x02 => 850,   // International MS-DOS
            0x03 => 1252,  // Windows ANSI
            0x57 => 1252,  // ANSI
            0x64 => 852,   // Eastern European MS-DOS
            0x65 => 866,   // Russian MS-DOS
            0x6A => 737,   // Greek MS-DOS
            0x6B => 857,   // Turkish MS-DOS
            0xC8 => 1250,  // Eastern European Windows
            0xC9 => 1251,  // Russian Windows
            0xCB => 1253,  // Greek Windows
            0xCC => 1254,  // Turkish Windows
            _ => 1252,
        };

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (NotSupportedException)
        {
            return Encoding.GetEncoding(1252);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(1252);
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var buffer))
        {
            var pos = (int)Math.Min(ms.Position, ms.Length);
            var len = (int)(ms.Length - pos);
            var slice = new byte[len];
            Array.Copy(buffer.Array!, buffer.Offset + pos, slice, 0, len);
            ms.Position = ms.Length;
            return slice;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
