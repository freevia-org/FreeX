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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.Save.cs"));

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
        root.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
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

        migratedDocument.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
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
