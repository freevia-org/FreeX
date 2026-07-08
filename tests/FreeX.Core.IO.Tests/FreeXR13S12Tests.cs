using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-13 fix-bucket S12 regression tests.
///
/// R13-other-format-adapters-3: DBF 'I'/'+' binary integer fields were reconstructed from an
/// already code-page-decoded string via `raw[i] &amp; 0xFF`, which is only a lossless byte&lt;-&gt;char
/// map for Latin-1 — CP1252 remaps 0x80-0x9F to different code points (and OEM code pages remap
/// the whole high range), silently corrupting the recovered integer. Separately, 'T'/'@' datetime
/// fields had no switch case and fell through to raw text.
/// </summary>
public sealed class FreeXR13S12Tests
{
    [Fact]
    public void Dbf_IntegerField_HighByteValue_IsNotCorruptedByCodePageDecoding()
    {
        // 'I' field storing the 4-byte little-endian integer 133 (0x00000085). Byte 0x85 decodes
        // under the default CP1252 language driver to U+2026 (HORIZONTAL ELLIPSIS); the old code
        // rebuilt the byte from that *decoded* char via `& 0xFF`, yielding 0x26 (38) instead of the
        // real 0x85 (133).
        var dbf = BuildDbf(
            fields: [("IVAL", 'I', 4)],
            recordFieldBytes: [[0x85, 0x00, 0x00, 0x00]]);

        var sheet = LoadSheet(dbf);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(133),
            "the raw field bytes must be read directly, not recovered from a lossy code-page-decoded string");
    }

    [Fact]
    public void Dbf_DatetimeField_ParsesJulianDayAndMilliseconds_InsteadOfRawText()
    {
        // 'T'/'@' datetime fields: first 4 bytes = Julian Day Number, next 4 = milliseconds since
        // midnight (both little-endian). JDN 2460385 = 2024-03-15 under the civil-day convention
        // where JDN 2440588 = 1970-01-01 (the FoxPro DBF epoch). Before the fix there was no switch
        // case for 'T'/'@', so this fell through to `default` and imported as raw garbage text.
        var dbf = BuildDbf(
            fields: [("TVAL", 'T', 8)],
            recordFieldBytes: [[0xE1, 0x8A, 0x25, 0x00, 0x00, 0x00, 0x00, 0x00]]);

        var sheet = LoadSheet(dbf);
        var value = sheet.GetValue(new CellAddress(sheet.Id, 2, 1));
        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Should().Be(new DateTime(2024, 3, 15, 0, 0, 0));
    }

    private static Sheet LoadSheet(byte[] dbf)
    {
        using var stream = new MemoryStream(dbf);
        return new DbfFileAdapter().Load(stream).Sheets.Single();
    }

    /// <summary>
    /// Minimal in-test dBASE III file builder writing raw binary field bytes (as opposed to the
    /// ASCII-text record builder in <c>DbfFileAdapterTests.DbfFixtureWriter</c>), needed to exercise
    /// the binary 'I'/'+'/'T'/'@' field types byte-for-byte.
    /// </summary>
    private static byte[] BuildDbf((string Name, char Type, int Length)[] fields, byte[][] recordFieldBytes)
    {
        int headerLength = 32 + fields.Length * 32 + 1; // header + descriptors + 0x0D terminator
        int recordLength = 1 + fields.Sum(f => f.Length); // deletion flag + fields
        using var ms = new MemoryStream();

        // ---- 32-byte file header ----
        ms.WriteByte(0x03); // dBASE III, no memo
        ms.WriteByte(124); ms.WriteByte(1); ms.WriteByte(15); // last-update Y/M/D (irrelevant here)
        WriteUInt32(ms, 1); // one record
        WriteUInt16(ms, (ushort)headerLength);
        WriteUInt16(ms, (ushort)recordLength);
        for (int i = 0; i < 17; i++) ms.WriteByte(0); // reserved (12..28)
        ms.WriteByte(0x03); // language driver = Windows ANSI (CP1252)
        ms.WriteByte(0);
        ms.WriteByte(0);

        // ---- field descriptors ----
        foreach (var f in fields)
        {
            var nameBytes = Encoding.ASCII.GetBytes(f.Name);
            for (int i = 0; i < 11; i++)
                ms.WriteByte(i < nameBytes.Length ? nameBytes[i] : (byte)0);
            ms.WriteByte((byte)f.Type);
            WriteUInt32(ms, 0); // field data address (reserved)
            ms.WriteByte((byte)f.Length);
            ms.WriteByte(0); // decimal count
            for (int i = 0; i < 14; i++) ms.WriteByte(0); // reserved (18..31)
        }
        ms.WriteByte(0x0D); // field-descriptor terminator

        // ---- one live record ----
        ms.WriteByte((byte)' ');
        foreach (var fieldBytes in recordFieldBytes)
            ms.Write(fieldBytes);

        ms.WriteByte(0x1A); // EOF marker
        return ms.ToArray();
    }

    private static void WriteUInt16(Stream s, ushort v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    private static void WriteUInt32(Stream s, uint v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 24) & 0xFF));
    }
}
