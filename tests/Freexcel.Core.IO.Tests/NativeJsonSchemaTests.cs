using System.Text;
using System.Text.Json;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class NativeJsonSchemaTests
{
    [Fact]
    public void Save_ScansCellsWithoutCopyingUsedCellDictionary()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.Save.cs"));

        source.Should().NotContain(
            "GetUsedCells()",
            "native JSON save should stream occupied cells directly into DTOs");
    }

    [Fact]
    public void MetadataMapping_StaysInDedicatedPartial()
    {
        var loadSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.cs"));
        var saveSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.Save.cs"));
        var mapperSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.MetadataMapping.cs"));
        var workbookFileMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.WorkbookFileMetadata.cs"));

        loadSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        saveSource.Should().NotContain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().Contain("private static WorksheetPageSetupMetadataModel? ToWorksheetPageSetupMetadata");
        mapperSource.Should().Contain("private static WorksheetPageSetupMetadataDto? FromWorksheetPageSetupMetadata");
        mapperSource.Should().NotContain("private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags");

        var smartTagSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "NativeJsonAdapter.WorkbookSmartTags.cs"));
        smartTagSource.Should().Contain("private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags");
        smartTagSource.Should().Contain("private static WorkbookSmartTagMetadataDto? FromWorkbookSmartTags");
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
        root.GetProperty("FileFormat").GetString().Should().Be("Freexcel.NativeJsonWorkbook");
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
        migratedDocument.RootElement.GetProperty("FileFormat").GetString().Should().Be("Freexcel.NativeJsonWorkbook");
    }

    [Fact]
    public void Load_RejectsUnsupportedFutureNativeJsonSchema()
    {
        const string futureJson = """
            {
              "FileFormat": "Freexcel.NativeJsonWorkbook",
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

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
    }
}
