using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for a crafted/corrupt .dbf whose header declares a <c>recordLength</c>
/// smaller than the sum of its field descriptors' widths. The adapter's record-boundary guard
/// (<c>recordStart + recordLength &lt;= bytes.Length</c>) only bounds the declared record length,
/// not the actual field-by-field walk driven by each field descriptor's independently-declared
/// <c>Length</c> byte — so a file with an internally-inconsistent field/record-length declaration
/// must not read past the end of the buffer while parsing the last record's fields.
/// </summary>
public sealed class DbfFileAdapterRecordLengthMismatchTests
{
    /// <summary>
    /// Hand-authors a raw dBASE III byte layout with an explicit, possibly-inconsistent
    /// <paramref name="recordLength"/> header value (unlike the sibling DbfFixtureWriter, which
    /// always derives recordLength from the true field widths, so it can never produce this case).
    /// </summary>
    private static byte[] BuildRawDbf(
        (string Name, char Type, int Length)[] fields,
        ushort recordLength,
        int totalByteCount)
    {
        using var ms = new MemoryStream();

        int headerLength = 32 + fields.Length * 32 + 1;

        // ---- 32-byte file header ----
        ms.WriteByte(0x03); // dBASE III, no memo
        ms.WriteByte(124);
        ms.WriteByte(1);
        ms.WriteByte(15);
        WriteUInt32(ms, 1); // recordCount = 1
        WriteUInt16(ms, (ushort)headerLength);
        WriteUInt16(ms, recordLength); // deliberately inconsistent with the true field widths
        for (int i = 0; i < 17; i++) ms.WriteByte(0);
        ms.WriteByte(0x03); // Windows ANSI (1252)
        ms.WriteByte(0);
        ms.WriteByte(0);

        // ---- field descriptors ----
        foreach (var f in fields)
        {
            var nameBytes = Encoding.ASCII.GetBytes(f.Name);
            for (int i = 0; i < 11; i++)
                ms.WriteByte(i < nameBytes.Length ? nameBytes[i] : (byte)0);
            ms.WriteByte((byte)f.Type);
            WriteUInt32(ms, 0);
            ms.WriteByte((byte)f.Length);
            ms.WriteByte(0);
            for (int i = 0; i < 14; i++) ms.WriteByte(0);
        }

        ms.WriteByte(0x0D); // field-descriptor terminator

        // ---- record bytes: deletion flag + as many field bytes as fit in totalByteCount ----
        var remaining = totalByteCount - (int)ms.Length;
        if (remaining > 0)
            ms.Write(new byte[remaining]); // zero-filled — content is irrelevant to the bounds check

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

    [Fact]
    public void Load_FieldWidthsExceedingDeclaredRecordLength_DoesNotThrow()
    {
        // 3 fields of width 50 each (true per-record width = 1 (flag) + 150 = 151), but the header
        // declares recordLength = 2 — grossly inconsistent. Total buffer is only just past the
        // header+descriptors, so the field-by-field walk for the one record would read far past the
        // end of the array unless each field read is individually bounds-checked.
        var fields = new[] { ("F1", 'C', 50), ("F2", 'C', 50), ("F3", 'C', 50) };
        int headerLength = 32 + fields.Length * 32 + 1; // 129
        var bytes = BuildRawDbf(fields, recordLength: 2, totalByteCount: headerLength + 7);

        using var stream = new MemoryStream(bytes);
        var act = () => new DbfFileAdapter().Load(stream);

        act.Should().NotThrow();
    }

    [Fact]
    public void Load_FieldWidthsExceedingDeclaredRecordLength_YieldsHeaderRowWithoutCrashing()
    {
        var fields = new[] { ("F1", 'C', 50), ("F2", 'C', 50), ("F3", 'C', 50) };
        int headerLength = 32 + fields.Length * 32 + 1;
        var bytes = BuildRawDbf(fields, recordLength: 2, totalByteCount: headerLength + 7);

        using var stream = new MemoryStream(bytes);
        var sheet = new DbfFileAdapter().Load(stream).Sheets.Single();

        // Header row (field names) is always emitted regardless of record-body corruption.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("F1"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("F2"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("F3"));
    }

    [Fact]
    public void Load_FieldReadingPastBuffer_StopsAtBufferEndInsteadOfThrowing()
    {
        // recordLength is declared just large enough that the record-boundary guard (recordStart +
        // recordLength <= bytes.Length) passes, but the true field widths (4 + 4 = 8) run past the
        // declared recordLength (2), so the second field's read would land beyond the buffer.
        var fields = new[] { ("F1", 'C', 4), ("F2", 'C', 4) };
        int headerLength = 32 + fields.Length * 32 + 1;
        var bytes = BuildRawDbf(fields, recordLength: 2, totalByteCount: headerLength + 1 + 4 + 2);

        using var stream = new MemoryStream(bytes);
        var act = () => new DbfFileAdapter().Load(stream);

        act.Should().NotThrow();
    }
}
