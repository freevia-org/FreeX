using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class NativeJsonSchemaTests
{
    [Fact]
    public void MetadataMapping_StaysInDedicatedPartial()
    {
        var loadSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.cs"));
        var saveSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.Save.cs"));
        var mapperSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.MetadataMapping.cs"));
        var workbookFileMetadataSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookFileMetadata.cs"));

        loadSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        saveSource.Should().NotContain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().NotContain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingModel? ToWorkbookFileSharing");
        workbookFileMetadataSource.Should().Contain("private static WorkbookFileSharingDto? FromWorkbookFileSharing");
        mapperSource.Should().Contain("private static NativeXmlPreserveBag? ToWorksheetPageSetupMetadata");
        mapperSource.Should().Contain("private static WorksheetPageSetupMetadataDto? FromWorksheetPageSetupMetadata");
        mapperSource.Should().NotContain("private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags");
        mapperSource.Should().NotContain("private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups");

        var workbookViewSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookViewMetadata.cs"));
        workbookViewSource.Should().Contain("private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups");
        workbookViewSource.Should().Contain("private static WorkbookAdditionalViewsDto? FromWorkbookAdditionalViews");

        var smartTagSource = File.ReadAllText(TestWorkspaceFiles.FindWorkspaceFile("src", "FreeX.Core.IO", "NativeJsonAdapter.WorkbookSmartTags.cs"));
        smartTagSource.Should().Contain("private static WorkbookSmartTagMetadataModel? ToWorkbookSmartTags");
        smartTagSource.Should().Contain("private static WorkbookSmartTagMetadataDto? FromWorkbookSmartTags");
    }

    [Fact]
    public void Save_TreatsNullNativeJsonMetadataChildMapsAsEmpty()
    {
        var workbook = new Workbook("NullMetadataChildMaps");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = new Dictionary<string, string> { ["manualBreakCount"] = "0" },
            BreakNativeAttributes = null!
        };
        sheet.ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
        {
            BreakNativeAttributes = null!
        };
        sheet.CellWatchesMetadata = new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = new Dictionary<string, string> { ["xr:uid"] = "{watches}" },
            WatchNativeAttributes = null!
        };
        sheet.IgnoredErrorsMetadata = new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = new Dictionary<string, string> { ["ext"] = "1" },
            ErrorNativeAttributes = null!
        };

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single();
        var rowPageBreaksMetadata = sheetJson.GetProperty("RowPageBreaksMetadata");
        rowPageBreaksMetadata.GetProperty("NativeAttributes").GetProperty("manualBreakCount").GetString().Should().Be("0");
        rowPageBreaksMetadata.GetProperty("BreakNativeAttributes").EnumerateObject().Should().BeEmpty();
        sheetJson.GetProperty("ColumnPageBreaksMetadata").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("CellWatchesMetadata").GetProperty("WatchNativeAttributes").EnumerateObject().Should().BeEmpty();
        sheetJson.GetProperty("IgnoredErrorsMetadata").GetProperty("ErrorNativeAttributes").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Save_TreatsNullNativeJsonWorkbookMetadataListsAsEmpty()
    {
        var workbook = new Workbook("NullWorkbookMetadataLists")
        {
            FunctionGroups = new WorkbookFunctionGroupsModel
            {
                BuiltInGroupCount = "16",
                Groups = null!
            },
            SmartTags = new WorkbookSmartTagMetadataModel
            {
                Show = "all",
                Types = null!
            },
            AdditionalViews = new WorkbookAdditionalViewsModel
            {
                NativeAttributes = new Dictionary<string, string> { ["xr:uid"] = "{views}" },
                Views = null!
            }
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement;
        root.GetProperty("FunctionGroups").GetProperty("Groups").EnumerateArray().Should().BeEmpty();
        root.GetProperty("SmartTags").GetProperty("Types").EnumerateArray().Should().BeEmpty();
        root.GetProperty("AdditionalViews").GetProperty("Views").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonWorkbookFileRecoveryEntries()
    {
        var workbook = new Workbook("NullWorkbookFileRecoveryEntries");
        workbook.FileRecoveryProperties.Add(null!);
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel());
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = true
        });
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var propertyJson = document.RootElement
            .GetProperty("FileRecoveryProperties").EnumerateArray()
            .Should().ContainSingle().Subject;

        propertyJson.GetProperty("AutoRecover").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Save_TreatsNullNativeJsonWorkbookFileMetadataNativeAttributesAsEmpty()
    {
        var workbook = new Workbook("NullWorkbookFileMetadataNativeAttributes")
        {
            FileVersion = new WorkbookFileVersionModel
            {
                AppName = "FreeX",
                NativeAttributes = null!
            }
        };
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = true,
            NativeAttributes = null!
        });
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement;
        root.GetProperty("FileVersion").GetProperty("NativeAttributes")
            .EnumerateObject().Should().BeEmpty();
        root.GetProperty("FileRecoveryProperties").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("NativeAttributes").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Save_TreatsNullNativeJsonWorksheetMetadataListsAsEmpty()
    {
        var workbook = new Workbook("NullWorksheetMetadataLists");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SmartTags = new WorksheetSmartTagsModel
        {
            NativeXml = "<smartTags />",
            Cells = null!
        };
        sheet.SingleXmlCells = new WorksheetSingleXmlCellsModel
        {
            NativeAttributes = new Dictionary<string, string> { ["xr:uid"] = "{singleXmlCells}" },
            Cells = null!
        };
        sheet.AdditionalViews = new WorksheetAdditionalViewsModel
        {
            NativeAttributes = new Dictionary<string, string> { ["xr:uid"] = "{sheetViews}" },
            Views = null!
        };

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("SmartTags").GetProperty("Cells").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("SingleXmlCells").GetProperty("Cells").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("AdditionalViews").GetProperty("Views").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonPivotTableEntries()
    {
        var workbook = new Workbook("NullPivotTableEntries");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.PivotTables.Add(null!);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("PivotTables").EnumerateArray()
            .Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonPivotCacheEntries()
    {
        var workbook = new Workbook("NullPivotCacheEntries");
        workbook.AddSheet("Sheet1");

        workbook.PivotCaches.Add(null!);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement
            .GetProperty("PivotCaches").EnumerateArray()
            .Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonPivotChildEntries()
    {
        var workbook = new Workbook("NullPivotChildEntries");
        var sheet = workbook.AddSheet("Sheet1");
        var sourceRange = GridRange.Parse("A1:B2", sheet.Id);

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString()
        };
        cache.Fields.Add(null!);
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = GridRange.Parse("D1:E2", sheet.Id)
        };
        pivot.RowFields.Add(null!);
        pivot.ColumnFields.Add(null!);
        pivot.PageFields.Add(null!);
        pivot.DataFields.Add(null!);
        pivot.CalculatedFields.Add(null!);
        pivot.CalculatedItems.Add(null!);
        pivot.LabelFilters.Add(null!);
        pivot.ValueFilters.Add(null!);
        pivot.Sorts.Add(null!);
        sheet.PivotTables.Add(pivot);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement.GetProperty("PivotCaches").EnumerateArray().Single()
            .GetProperty("Fields").EnumerateArray().Should().BeEmpty();

        var pivotJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("PivotTables").EnumerateArray().Single();
        pivotJson.GetProperty("RowFields").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("ColumnFields").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("PageFields").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("DataFields").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("CalculatedFields").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("CalculatedItems").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("LabelFilters").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("ValueFilters").EnumerateArray().Should().BeEmpty();
        pivotJson.GetProperty("Sorts").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonThemeAlternateColorSchemeEntries()
    {
        var workbook = new Workbook("NullThemeAlternateColorSchemes");
        workbook.AddSheet("Sheet1");
        workbook.Theme = WorkbookTheme.Office.WithSupplementalMetadata(
            [
                null!,
                new WorkbookThemeAlternateColorScheme("Empty", null!),
                new WorkbookThemeAlternateColorScheme(
                    "Kept",
                    new Dictionary<WorkbookThemeColorSlot, CellColor>
                    {
                        [WorkbookThemeColorSlot.Accent1] = new(1, 2, 3)
                    })
            ],
            hasObjectDefaults: false);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var schemes = document.RootElement
            .GetProperty("Theme")
            .GetProperty("AlternateColorSchemes")
            .EnumerateArray()
            .ToList();

        schemes.Should().HaveCount(2);
        schemes[0].GetProperty("Name").GetString().Should().Be("Empty");
        schemes[0].GetProperty("Colors").EnumerateArray().Should().BeEmpty();
        schemes[1].GetProperty("Name").GetString().Should().Be("Kept");
        schemes[1].GetProperty("Colors").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("Color").GetString().Should().Be("#010203");
    }

    [Fact]
    public void Load_DropsNullNativeJsonThemeAlternateColorSchemeEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullThemeAlternateColorSchemes",
              "Theme": {
                "AlternateColorSchemes": [
                  null,
                  {
                    "Name": "Kept",
                    "Colors": [
                      null,
                      { "Slot": 4, "Color": "#010203" }
                    ]
                  }
                ]
              },
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var scheme = new NativeJsonAdapter().Load(stream).Theme.AlternateColorSchemes
            .Should().ContainSingle().Subject;

        scheme.Name.Should().Be("Kept");
        scheme.Colors.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<WorkbookThemeColorSlot, CellColor>(
                WorkbookThemeColorSlot.Accent1,
                new CellColor(1, 2, 3)));
    }

    [Fact]
    public void Load_DropsNullNativeJsonThemeColorEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullThemeColors",
              "Theme": {
                "Colors": [
                  null,
                  { "Slot": 4, "Color": "#010203" }
                ]
              },
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var theme = new NativeJsonAdapter().Load(stream).Theme;

        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(1, 2, 3));
    }
}
