using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the read-only dBASE (.dbf) adapter. A small dBASE III file is byte-authored in-test (via the
/// <see cref="DbfFixtureWriter"/> helper) so the header + field-descriptor + fixed-width-record parsing is
/// exercised against a known layout: a header row of field names plus typed data rows (C text, N numeric,
/// D date yyyymmdd, L logical, M memo→blank), including a tombstoned ('*') record that must be skipped.
/// </summary>
public sealed class DbfFileAdapterTests
{
    private static Sheet LoadSheet(byte[] dbf)
    {
        using var stream = new MemoryStream(dbf);
        return new DbfFileAdapter().Load(stream).Sheets.Single();
    }

    [Fact]
    public void Load_EmitsFieldNamesAsHeaderRow()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("NAME", 'C', 10)
            .AddField("AGE", 'N', 4)
            .AddRecord("Alice", "  30")
            .Build();

        var sheet = LoadSheet(dbf);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("NAME"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("AGE"));
    }

    [Fact]
    public void Load_ParsesCharacterAndNumericFields()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("NAME", 'C', 10)
            .AddField("AGE", 'N', 4)
            .AddField("BAL", 'N', 8, decimals: 2)
            .AddRecord("Alice", "  30", " 125.50")
            .Build();

        var sheet = LoadSheet(dbf);
        // Data row is row 2 (row 1 = header).
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(30));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(125.5));
    }

    [Fact]
    public void Load_ParsesDateFieldAsDateTimeValue()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("WHEN", 'D', 8)
            .AddRecord("20240115")
            .Build();

        var sheet = LoadSheet(dbf);
        var value = sheet.GetValue(new CellAddress(sheet.Id, 2, 1));
        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Date.Should().Be(new DateTime(2024, 1, 15));
    }

    [Fact]
    public void Load_ParsesLogicalFieldAsBoolean()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("ACTIVE", 'L', 1)
            .AddRecord("T")
            .AddRecord("F")
            .AddRecord("?")
            .Build();

        var sheet = LoadSheet(dbf);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new BoolValue(false));
        // '?' = undefined logical → blank.
        sheet.GetValue(new CellAddress(sheet.Id, 4, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Load_MemoFieldIsEmittedBlank()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("NOTE", 'M', 10)
            .AddField("NAME", 'C', 5)
            .AddRecord("0000000001", "Bob  ")
            .Build();

        var sheet = LoadSheet(dbf);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("Bob"));
    }

    [Fact]
    public void Load_SkipsDeletedRecords()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("NAME", 'C', 6)
            .AddRecord("Keep")
            .AddDeletedRecord("Gone")
            .AddRecord("Also")
            .Build();

        var sheet = LoadSheet(dbf);
        // Header + 2 live records => 3 rows, with the deleted record dropped (not occupying a row).
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Keep"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("Also"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Load_TrimsTrailingSpacesFromCharacterFields()
    {
        var dbf = new DbfFixtureWriter()
            .AddField("NAME", 'C', 10)
            .AddRecord("Pat       ")
            .Build();

        var sheet = LoadSheet(dbf);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Pat"));
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new DbfFileAdapter();
        adapter.Formats.Single().CanSave.Should().BeFalse();
        adapter.Formats.Single().CanOpen.Should().BeTrue();

        using var stream = new MemoryStream();
        var act = () => adapter.Save(new Workbook("x"), stream);
        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// Minimal in-test dBASE III file builder: a 32-byte header, 32-byte field descriptors, a 0x0D
    /// terminator, then one byte (deletion flag) + space-padded ASCII field bytes per record. Field values
    /// are passed pre-padded to the field width by the caller (the records assert exact byte layout).
    /// </summary>
    private sealed class DbfFixtureWriter
    {
        private readonly List<(string Name, char Type, int Length, int Decimals)> _fields = new();
        private readonly List<(bool Deleted, string[] Values)> _records = new();

        public DbfFixtureWriter AddField(string name, char type, int length, int decimals = 0)
        {
            _fields.Add((name, type, length, decimals));
            return this;
        }

        public DbfFixtureWriter AddRecord(params string[] values)
        {
            _records.Add((false, values));
            return this;
        }

        public DbfFixtureWriter AddDeletedRecord(params string[] values)
        {
            _records.Add((true, values));
            return this;
        }

        public byte[] Build()
        {
            int headerLength = 32 + _fields.Count * 32 + 1; // header + descriptors + 0x0D terminator
            int recordLength = 1 + _fields.Sum(f => f.Length); // deletion flag + fields
            using var ms = new MemoryStream();

            // ---- 32-byte file header ----
            ms.WriteByte(0x03); // dBASE III, no memo
            ms.WriteByte(124);  // last-update YY (1924/2024 — value is irrelevant to parsing)
            ms.WriteByte(1);
            ms.WriteByte(15);
            WriteUInt32(ms, (uint)_records.Count);
            WriteUInt16(ms, (ushort)headerLength);
            WriteUInt16(ms, (ushort)recordLength);
            for (int i = 0; i < 17; i++) ms.WriteByte(0); // reserved (12..28)
            ms.WriteByte(0x03); // language driver = Windows ANSI (1252)
            ms.WriteByte(0);
            ms.WriteByte(0);

            // ---- field descriptors ----
            foreach (var f in _fields)
            {
                var nameBytes = Encoding.ASCII.GetBytes(f.Name);
                for (int i = 0; i < 11; i++)
                    ms.WriteByte(i < nameBytes.Length ? nameBytes[i] : (byte)0);
                ms.WriteByte((byte)f.Type);
                WriteUInt32(ms, 0);          // field data address (reserved)
                ms.WriteByte((byte)f.Length);
                ms.WriteByte((byte)f.Decimals);
                for (int i = 0; i < 14; i++) ms.WriteByte(0); // reserved (18..31)
            }

            ms.WriteByte(0x0D); // field-descriptor terminator

            // ---- records ----
            foreach (var (deleted, values) in _records)
            {
                ms.WriteByte(deleted ? (byte)'*' : (byte)' ');
                for (int i = 0; i < _fields.Count; i++)
                {
                    var raw = i < values.Length ? values[i] : "";
                    var padded = raw.Length >= _fields[i].Length
                        ? raw[.._fields[i].Length]
                        : raw.PadRight(_fields[i].Length, ' ');
                    ms.Write(Encoding.ASCII.GetBytes(padded));
                }
            }

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
}
