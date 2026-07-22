using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class NativeJsonSchemaTests
{
    [Fact]
    public void Save_ScansCellsWithoutCopyingUsedCellDictionary()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("NativeJsonAdapter.Save.cs");

        source.Should().NotContain(
            "GetUsedCells()",
            "native JSON save should stream occupied cells directly into DTOs");
    }

    [Fact]
    public void Save_WritesCurrentNativeJsonSchemaHeader()
    {
        var workbook = new Workbook("Schema");
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement;
        root.GetProperty("FileFormat").GetString().Should().Be("FreeX.NativeJsonWorkbook");
        // Bumped 1 -> 2 in R72-meta-1 to gate the legacy data-bar Min/Max -> AutoMin/AutoMax
        // migration so it applies only to genuinely pre-r70 files, not to an explicit user choice.
        root.GetProperty("SchemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("MinimumReaderVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Load_AcceptsLegacyUnversionedNativeJsonAndMigratesOnSave()
    {
        const string legacyJson = """
            {
              "Name": "Legacy",
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """;

        using var legacyStream = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson));
        var adapter = new NativeJsonAdapter();

        var workbook = adapter.Load(legacyStream);

        workbook.Name.Should().Be("Legacy");
        workbook.GetSheetAt(0).Name.Should().Be("Sheet1");

        using var migratedStream = new MemoryStream();
        adapter.Save(workbook, migratedStream);
        using var migratedDocument = JsonDocument.Parse(migratedStream.ToArray());

        migratedDocument.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(2);
        migratedDocument.RootElement.GetProperty("FileFormat").GetString().Should().Be("FreeX.NativeJsonWorkbook");
    }

    [Fact]
    public void Load_UsesCurrentStreamPositionAndLeavesInputStreamOpen()
    {
        using var stream = PositionedStreamFromString("ignored", """
            {
              "Name": "Offset",
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """);

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Name.Should().Be("Offset");
        workbook.GetSheetAt(0).Name.Should().Be("Sheet1");
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Load_CellsAcceptValueTypeBeforeOrAfterValue()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "Value order",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "123", "ValueType": "n" },
                    { "Address": "B1", "ValueType": "n", "Value": "456" },
                    { "Address": "C1", "ValueType": "d", "Value": "45292.5" },
                    { "Address": "D1", "ValueType": "t", "Value": "00123" },
                    { "Address": "E1", "ValueType": "n", "Value": "NaN" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(1, 1)!.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(123);
        sheet.GetCell(1, 2)!.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(456);
        sheet.GetCell(1, 3)!.Value.Should().BeOfType<DateTimeValue>().Which.Value.Should().Be(45292.5);
        sheet.GetCell(1, 4)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("00123");
        sheet.GetCell(1, 5)!.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("NaN");
    }

    [Fact]
    public void Save_UsesCurrentStreamPositionAndLeavesOutputStreamOpen()
    {
        var workbook = new Workbook("OffsetSave");
        workbook.AddSheet("Sheet1");
        var prefixBytes = Encoding.UTF8.GetBytes("ignored");
        using var stream = new MemoryStream();
        stream.Write(prefixBytes);

        new NativeJsonAdapter().Save(workbook, stream);

        stream.CanWrite.Should().BeTrue();
        stream.ToArray().Take(prefixBytes.Length).Should().Equal(prefixBytes);
        using var document = JsonDocument.Parse(stream.ToArray().AsMemory(prefixBytes.Length));
        document.RootElement.GetProperty("Name").GetString().Should().Be("OffsetSave");
        document.RootElement.GetProperty("FileFormat").GetString().Should().Be("FreeX.NativeJsonWorkbook");
    }

    [Fact]
    public void Load_RejectsUnsupportedFutureNativeJsonSchema()
    {
        const string futureJson = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 999,
              "MinimumReaderVersion": 999,
              "Name": "Future",
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(futureJson));

        var act = () => new NativeJsonAdapter().Load(stream);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*schema version*999*");
    }
}
