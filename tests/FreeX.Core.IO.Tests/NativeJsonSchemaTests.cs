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
    public void Save_WritesRepeatedCellStylesThroughWorkbookStyleTable()
    {
        var workbook = new Workbook("Styles");
        var sheet = workbook.AddSheet("Sheet1");
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "$#,##0.00"
        });
        var first = Cell.FromValue(new NumberValue(1));
        first.StyleId = styleId;
        var second = Cell.FromValue(new NumberValue(2));
        second.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), first);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), second);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement;
        root.GetProperty("CellStyles").GetArrayLength().Should().BeGreaterThan(1);
        var cells = root.GetProperty("Sheets")[0].GetProperty("Cells");
        cells[0].GetProperty("StyleId").GetInt32().Should().Be(styleId.Value);
        cells[1].GetProperty("StyleId").GetInt32().Should().Be(styleId.Value);
        cells[0].TryGetProperty("Style", out _).Should().BeFalse();

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedStyle = loaded.GetStyle(loadedSheet.GetCell(1, 1)!.StyleId);
        loadedStyle.Bold.Should().BeTrue();
        loadedStyle.NumberFormat.Should().Be("$#,##0.00");
        loadedSheet.GetCell(2, 1)!.StyleId.Should().Be(loadedSheet.GetCell(1, 1)!.StyleId);
    }

    [Fact]
    public void Save_OmitsDefaultNativeJsonCellFields()
    {
        var workbook = new Workbook("CompactCells");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "A1*2");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        var literal = cells.EnumerateArray().Single(cell => cell.GetProperty("Address").GetString() == "A1");
        literal.GetProperty("Value").GetString().Should().Be("42");
        literal.GetProperty("ValueType").GetString().Should().Be("n");
        literal.TryGetProperty("Formula", out _).Should().BeFalse();
        literal.TryGetProperty("IgnoreFormulaError", out _).Should().BeFalse();

        var formula = cells.EnumerateArray().Single(cell => cell.GetProperty("Address").GetString() == "A2");
        formula.GetProperty("Formula").GetString().Should().Be("A1*2");
        formula.TryGetProperty("Value", out _).Should().BeFalse();
        formula.TryGetProperty("ValueType", out _).Should().BeFalse();
        formula.TryGetProperty("IgnoreFormulaError", out _).Should().BeFalse();

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42));
        loaded.GetCell(2, 1)!.FormulaText.Should().Be("A1*2");
        loaded.GetCell(2, 1)!.IgnoreFormulaError.Should().BeFalse();
    }

    [Fact]
    public void Save_WritesNonFiniteNativeJsonNumbersAsTextCells()
    {
        var workbook = new Workbook("NonFinite");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(double.NegativeInfinity));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        cells[0].GetProperty("Value").GetString().Should().Be("NaN");
        cells[0].GetProperty("ValueType").GetString().Should().Be("t");
        cells[1].GetProperty("Value").GetString().Should().Be("Infinity");
        cells[1].GetProperty("ValueType").GetString().Should().Be("t");
        cells[2].GetProperty("Value").GetString().Should().Be("-Infinity");
        cells[2].GetProperty("ValueType").GetString().Should().Be("t");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_RoundTripsNativeJsonDateTimeValues()
    {
        var workbook = new Workbook("DateTimes");
        var sheet = workbook.AddSheet("Sheet1");
        var dateTime = DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30));
        var timeOnly = new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), dateTime);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), timeOnly);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        cells[0].GetProperty("ValueType").GetString().Should().Be("d");
        cells[1].GetProperty("ValueType").GetString().Should().Be("d");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(dateTime);
        loaded.GetCell(1, 2)!.Value.Should().Be(timeOnly);
    }

    [Fact]
    public void Load_AcceptsLegacyInlineCellStylesWithoutWorkbookStyleTable()
    {
        const string legacyJson = """
            {
              "Name": "LegacyInlineStyle",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    {
                      "Address": "A1",
                      "Value": "1",
                      "ValueType": "n",
                      "Style": {
                        "Bold": true,
                        "NumberFormat": "0.00"
                      }
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        var style = workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId);
        style.Bold.Should().BeTrue();
        style.NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void Load_AcceptsTrimmedFallbackCellAddresses()
    {
        const string json = """
            {
              "Name": "AddressFallback",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": " a1 ", "Value": "42", "ValueType": "n" },
                    { "Address": " b2 ", "Value": "fallback", "ValueType": "s" }
                  ],
                  "StyleOnlyCells": [
                    { "Address": " c3 ", "StyleId": 0 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("fallback"));
        sheet.GetStyleOnly(3, 3).Should().Be(StyleId.Default);
    }

    [Fact]
    public void Load_TreatsNullSheetCellCollectionsAsEmpty()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullCells",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": null,
                  "StyleOnlyCells": null
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Sheet1");
        sheet.GetCell(1, 1).Should().BeNull();
        sheet.HasStyleOnlyCells.Should().BeFalse();
    }

    [Fact]
    public void Load_HonorsEmptySheetProtectionPermissions()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "EmptyProtectionPermissions",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "IsProtected": true,
                  "ProtectionPermissions": []
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPermissions.Should().BeEmpty();
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
    public void Save_SkipsOutOfBoundsNativeJsonCellAddresses()
    {
        var workbook = new Workbook("InvalidAddresses");
        var sheet = workbook.AddSheet("Sheet1");
        var valid = new CellAddress(sheet.Id, 1, 1);
        var invalidRow = new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1);
        var invalidColumn = new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1);

        sheet.SetCell(valid, new TextValue("kept"));
        sheet.SetCell(invalidRow, new TextValue("dropped"));
        sheet.Comments[valid] = "kept";
        sheet.Comments[invalidRow] = "dropped";
        sheet.ThreadedComments[valid] = new ThreadedComment("kept");
        sheet.ThreadedComments[invalidColumn] = new ThreadedComment("dropped");
        sheet.Hyperlinks[valid] = "https://example.invalid/kept";
        sheet.Hyperlinks[invalidColumn] = "https://example.invalid/dropped";
        var styleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(valid.Row, valid.Col + 1, styleId);
        sheet.SetStyleOnly(invalidRow.Row, invalidRow.Col + 1, styleId);
        workbook.WatchedCells.Add(valid);
        workbook.WatchedCells.Add(invalidRow);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Mixed",
            [
                new ScenarioCellValue(valid, new TextValue("kept")),
                new ScenarioCellValue(invalidColumn, new TextValue("dropped"))
            ]));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        using var document = JsonDocument.Parse(stream.ToArray());

        var root = document.RootElement;
        root.GetProperty("WatchedCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        root.GetProperty("Scenarios").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("ChangingCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");

        var sheetJson = root.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("Cells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("Comments").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("ThreadedComments").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("Hyperlinks").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("StyleOnlyCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("B1");
    }

    [Fact]
    public void Save_SkipsOutOfBoundsNativeJsonRanges()
    {
        var workbook = new Workbook("InvalidRanges");
        var sheet = workbook.AddSheet("Sheet1");
        var validRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var invalidRowRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 2));
        var invalidColumnRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol + 1));

        workbook.DefineNamedRange("ValidRange", validRange);
        workbook.NamedRanges["InvalidRange"] = invalidRowRange;
        sheet.AddMergedRegion(validRange);
        sheet.AddMergedRegion(invalidRowRange);
        sheet.AllowEditRanges.Add(validRange);
        sheet.AllowEditRanges.Add(invalidColumnRange);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        using var document = JsonDocument.Parse(stream.ToArray());

        var root = document.RootElement;
        var namedRange = root.GetProperty("NamedRanges").EnumerateArray()
            .Should().ContainSingle().Subject;
        namedRange.GetProperty("Name").GetString().Should().Be("ValidRange");
        namedRange.GetProperty("Range").GetString().Should().Be("A1:B2");

        var sheetJson = root.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("MergedRegions").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("A1:B2");
        sheetJson.GetProperty("AllowEditRanges").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("A1:B2");
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
    public void Save_TreatsNullNativeJsonChartListsAsEmpty()
    {
        var workbook = new Workbook("NullChartLists");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B2", sheet.Id),
            LegendEntries = null!,
            SecondaryAxisSeriesIndexes = null!,
            ComboLineSeriesIndexes = null!,
            SeriesFormats = null!,
            SeriesDataLabelFormats = null!,
            PointDataLabelFormats = null!
        });

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var chartJson = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("Charts").EnumerateArray().Single();

        chartJson.GetProperty("LegendEntries").EnumerateArray().Should().BeEmpty();
        chartJson.GetProperty("SecondaryAxisSeriesIndexes").EnumerateArray().Should().BeEmpty();
        chartJson.GetProperty("ComboLineSeriesIndexes").EnumerateArray().Should().BeEmpty();
        chartJson.GetProperty("SeriesFormats").EnumerateArray().Should().BeEmpty();
        chartJson.GetProperty("SeriesDataLabelFormats").EnumerateArray().Should().BeEmpty();
        chartJson.GetProperty("PointDataLabelFormats").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonChartEntries()
    {
        var workbook = new Workbook("NullChartEntries");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        sheet.Charts.Add(null!);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B2", sheet.Id),
            Title = "Kept"
        });

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var chartJson = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("Charts").EnumerateArray().Should().ContainSingle().Subject;
        chartJson.GetProperty("Title").GetString().Should().Be("Kept");
    }

    [Fact]
    public void Save_DropsNullNativeJsonDataValidationEntries()
    {
        var workbook = new Workbook("NullDataValidationEntries");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DataValidations.Add(null!);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("DataValidations").EnumerateArray()
            .Should().BeEmpty();
    }

    [Fact]
    public void Save_DropsNullNativeJsonDrawingAndSparklineEntries()
    {
        var workbook = new Workbook("NullDrawingEntries");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.Pictures.Add(null!);
        sheet.TextBoxes.Add(null!);
        sheet.DrawingShapes.Add(null!);
        sheet.Sparklines.Add(null!);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("Pictures").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("TextBoxes").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("DrawingShapes").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("Sparklines").EnumerateArray().Should().BeEmpty();
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
    public void Save_DropsNullNativeJsonWorkbookViewAndScenarioEntries()
    {
        var workbook = new Workbook("NullWorkbookViewAndScenarioEntries");
        var sheet = workbook.AddSheet("Sheet1");
        var validAddress = new CellAddress(sheet.Id, 1, 1);

        workbook.CustomViews.Add(null!);
        workbook.CustomViews.Add(new WorkbookCustomView("EmptySheets", null!));
        workbook.Scenarios.Add(null!);
        workbook.Scenarios.Add(new WorkbookScenario("NoChanges", null!));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Kept",
            [new ScenarioCellValue(validAddress, new TextValue("kept"))]));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var customViewJson = document.RootElement
            .GetProperty("CustomViews").EnumerateArray().Should().ContainSingle().Subject;
        customViewJson.GetProperty("Name").GetString().Should().Be("EmptySheets");
        customViewJson.GetProperty("Sheets").EnumerateArray().Should().BeEmpty();

        var scenarioJson = document.RootElement
            .GetProperty("Scenarios").EnumerateArray().Should().ContainSingle().Subject;
        scenarioJson.GetProperty("Name").GetString().Should().Be("Kept");
        scenarioJson.GetProperty("ChangingCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
    }

    [Fact]
    public void Save_DropsNullNativeJsonChartListEntries()
    {
        var workbook = new Workbook("NullChartListEntries");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B2", sheet.Id),
            LegendEntries = [null!, new ChartLegendEntryModel(0, true)],
            SeriesFormats = [null!, new ChartSeriesFormat(0, FillColor: new CellColor(1, 2, 3))],
            SeriesDataLabelFormats = [null!, new ChartSeriesDataLabelFormat(0, ShowValue: false)],
            PointDataLabelFormats = [null!, new ChartPointDataLabelFormat(0, 0, IsDeleted: true)]
        });

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var chartJson = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("Charts").EnumerateArray().Single();

        chartJson.GetProperty("LegendEntries").EnumerateArray().Should().ContainSingle();
        chartJson.GetProperty("SeriesFormats").EnumerateArray().Should().ContainSingle();
        chartJson.GetProperty("SeriesDataLabelFormats").EnumerateArray().Should().ContainSingle();
        chartJson.GetProperty("PointDataLabelFormats").EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public void Load_DropsNullNativeJsonChartListEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullChartListEntries",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "Name", "ValueType": "t" },
                    { "Address": "B1", "Value": "Value", "ValueType": "t" },
                    { "Address": "A2", "Value": "A", "ValueType": "t" },
                    { "Address": "B2", "Value": "1", "ValueType": "n" }
                  ],
                  "Charts": [
                    {
                      "Type": 0,
                      "DataRange": "A1:B2",
                      "LegendEntries": [ null, { "Index": 0, "IsDeleted": true } ],
                      "SeriesFormats": [ null, { "SeriesIndex": 0, "FillColor": { "R": 1, "G": 2, "B": 3 } } ],
                      "SeriesDataLabelFormats": [ null, { "SeriesIndex": 0, "ShowValue": false } ],
                      "PointDataLabelFormats": [ null, { "SeriesIndex": 0, "PointIndex": 0, "IsDeleted": true } ]
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var chart = new NativeJsonAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.LegendEntries.Should().ContainSingle().Which.Should().Be(new ChartLegendEntryModel(0, true));
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillColor: new CellColor(1, 2, 3)));
        chart.SeriesDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesDataLabelFormat(0, ShowValue: false));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, IsDeleted: true));
    }

    [Fact]
    public void Save_NormalizesNativeJsonWaterfallTotalPointIndices()
    {
        var workbook = new Workbook("WaterfallTotals");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = GridRange.Parse("A1:A4", sheet.Id),
            WaterfallTotalPointIndices = [3, -1, 0, 3, 1]
        });

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var totals = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("Charts").EnumerateArray().Single()
            .GetProperty("WaterfallTotalPointIndices")
            .EnumerateArray()
            .Select(element => element.GetInt32());

        totals.Should().Equal(0, 1, 3);
    }

    [Fact]
    public void Load_ClearsUnsupportedNativeJsonWaterfallTotalPointIndices()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "UnsupportedWaterfallTotals",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "Name", "ValueType": "t" },
                    { "Address": "B1", "Value": "Value", "ValueType": "t" },
                    { "Address": "A2", "Value": "A", "ValueType": "t" },
                    { "Address": "B2", "Value": "1", "ValueType": "n" }
                  ],
                  "Charts": [
                    {
                      "Type": 0,
                      "DataRange": "A1:B2",
                      "WaterfallTotalPointIndices": [ 0, 1 ]
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var chart = new NativeJsonAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.Type.Should().Be(ChartType.Column);
        chart.WaterfallTotalPointIndices.Should().BeNull();
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
