using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonSchemaTests
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
    public void MetadataMapping_StaysInDedicatedPartial()
    {
        var loadSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.cs"));
        var saveSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.Save.cs"));
        var mapperSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.MetadataMapping.cs"));
        var workbookFileMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookFileMetadata.cs"));

        loadSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        saveSource.Should().NotContain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().Contain("private static NativeXmlPreserveBag? ToWorksheetPageSetupMetadata");
        mapperSource.Should().Contain("private static WorksheetPageSetupMetadataDto? FromWorksheetPageSetupMetadata");
        mapperSource.Should().NotContain("private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags");
        mapperSource.Should().NotContain("private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups");

        var workbookViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookViewMetadata.cs"));
        workbookViewSource.Should().Contain("private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups");
        workbookViewSource.Should().Contain("private static WorkbookAdditionalViewsDto? FromWorkbookAdditionalViews");

        var smartTagSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookSmartTags.cs"));
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

    [Theory]
    [InlineData("""{ "Name": "LegacyWithoutSheets" }""")]
    [InlineData("""{ "Name": "LegacyWithNoValidSheets", "Sheets": [ { "Name": "" }, null ] }""")]
    public void Load_AddsDefaultSheetWhenNativeJsonHasNoValidSheets(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Sheets.Should().ContainSingle();
        workbook.GetSheetAt(0).Name.Should().Be("Sheet1");
    }

    [Fact]
    public void Load_NormalizesInvalidBlankDuplicateAndLongNativeJsonSheetNames()
    {
        const string json = """
            {
              "Name": "MalformedSheetNames",
              "Sheets": [
                { "Name": "'Bad:/?*[]Name'" },
                { "Name": "bad:/?*[]name" },
                { "Name": "   " },
                { "Name": "''" },
                { "Name": "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890" }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal(
            "Bad______Name",
            "bad______name (1)",
            "Sheet3",
            "Sheet",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ12345");
        workbook.Sheets.Select(sheet => sheet.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Load_ResolvesMetadataReferencesToNormalizedNativeJsonSheetNames()
    {
        const string json = """
            {
              "Name": "MalformedSheetReferences",
              "Sheets": [
                {
                  "Name": "'Bad:/?*[]Name'",
                  "Cells": [
                    { "Address": "A1", "Value": "42", "ValueType": "n" }
                  ]
                }
              ],
              "NamedRanges": [
                { "Name": "Input", "SheetName": "'Bad:/?*[]Name'", "Range": "A1:A1" }
              ],
              "WatchedCells": [
                { "SheetName": "'Bad:/?*[]Name'", "Address": "A1" }
              ],
              "Scenarios": [
                {
                  "Name": "Scenario 1",
                  "ChangingCells": [
                    { "SheetName": "'Bad:/?*[]Name'", "Address": "A1", "Value": "99", "ValueType": "n" }
                  ]
                }
              ],
              "CustomViews": [
                {
                  "Name": "View 1",
                  "Sheets": [
                    { "SheetName": "'Bad:/?*[]Name'", "ZoomPercent": 125 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Bad______Name");
        workbook.NamedRanges["Input"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(new CellAddress(sheet.Id, 1, 1));
        workbook.Scenarios.Should().ContainSingle()
            .Which.ChangingCells.Should().ContainSingle()
            .Which.Address.Should().Be(new CellAddress(sheet.Id, 1, 1));
        workbook.CustomViews.Should().ContainSingle()
            .Which.Sheets.Should().ContainSingle()
            .Which.SheetName.Should().Be("Bad______Name");
    }

    [Fact]
    public void Load_RevalidatesWorkbookViewSheetIndexesAfterSkippingNullNativeJsonSheets()
    {
        const string json = """
            {
              "Name": "MalformedWorkbookView",
              "ActiveSheetIndex": 1,
              "FirstVisibleSheetIndex": 1,
              "Sheets": [
                { "Name": "Sheet1" },
                null
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Sheets.Should().ContainSingle();
        workbook.ActiveSheetIndex.Should().BeNull();
        workbook.FirstVisibleSheetIndex.Should().BeNull();
    }

    [Fact]
    public void Load_DropsUnresolvableNativeJsonCustomViewSheetReferences()
    {
        const string json = """
            {
              "Name": "CustomViewSheetReferences",
              "Sheets": [
                { "Name": "Loaded" }
              ],
              "CustomViews": [
                {
                  "Name": "Mixed",
                  "Sheets": [
                    { "SheetName": "Loaded", "ZoomPercent": 110 },
                    { "SheetName": "Missing", "ZoomPercent": 125 }
                  ]
                },
                {
                  "Name": "OnlyMissing",
                  "Sheets": [
                    { "SheetName": "Missing", "ZoomPercent": 140 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var view = workbook.CustomViews.Should().ContainSingle().Which;
        view.Name.Should().Be("Mixed");
        var sheetState = view.Sheets.Should().ContainSingle().Which;
        sheetState.SheetName.Should().Be("Loaded");
        sheetState.ZoomPercent.Should().Be(110);
    }

    [Fact]
    public void Load_DropsInvalidNativeJsonPrintTitleRanges()
    {
        const string json = """
            {
              "Name": "PrintTitleRanges",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "PrintTitleRows": { "Start": 0, "End": 2 },
                  "PrintTitleColumns": { "Start": 2, "End": 1 }
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.PrintTitleRows.Should().BeNull();
        sheet.PrintTitleColumns.Should().BeNull();
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

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Load_TreatsNonFiniteNativeJsonNumbersAsText(string value)
    {
        var json = $$"""
            {
              "Name": "NonFinite",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "{{value}}", "ValueType": "n" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue(value));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Load_ParsesNativeJsonBooleanCellsCaseInsensitively(string value, bool expected)
    {
        var json = $$"""
            {
              "Name": "BooleanCell",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "{{value}}", "ValueType": "b" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new BoolValue(expected));
    }

    [Fact]
    public void Load_TreatsMalformedNativeJsonBooleanCellsAsText()
    {
        const string json = """
            {
              "Name": "BooleanCell",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "not-bool", "ValueType": "b" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("not-bool"));
    }

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
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
