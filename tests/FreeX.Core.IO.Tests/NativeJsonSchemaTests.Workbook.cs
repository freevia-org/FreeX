using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class NativeJsonSchemaTests
{
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
    public void Save_DropsNullBlankAndUnsupportedNativeJsonFormulaErrorCodes()
    {
        var workbook = new Workbook("FormulaErrorCodes");
        workbook.AddSheet("Sheet1");
        workbook.DisabledFormulaErrorCodes.Add(null!);
        workbook.DisabledFormulaErrorCodes.Add("");
        workbook.DisabledFormulaErrorCodes.Add("#NOT-AN-EXCEL-RULE!");
        workbook.DisabledFormulaErrorCodes.Add("#REF!");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement.GetProperty("DisabledFormulaErrorCodes").EnumerateArray()
            .Should().ContainSingle()
            .Which.GetString().Should().Be("#REF!");
    }

    [Fact]
    public void Load_DropsNullBlankAndUnsupportedNativeJsonFormulaErrorCodes()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "FormulaErrorCodes",
              "DisabledFormulaErrorCodes": [
                null,
                "",
                "#NOT-AN-EXCEL-RULE!",
                "#REF!"
              ],
              "Sheets": [
                { "Name": "Sheet1" }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.DisabledFormulaErrorCodes.Should().ContainSingle().Which.Should().Be("#REF!");
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
    public void Load_DropsNullNativeJsonWorkbookReferenceEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullWorkbookReferenceEntries",
              "Sheets": [
                { "Name": "Sheet1" }
              ],
              "NamedRanges": [
                null,
                { "Name": "Input", "SheetName": "Sheet1", "Range": "A1:B2" },
                { "Name": "MissingSheet", "SheetName": "Missing", "Range": "A1" }
              ],
              "WatchedCells": [
                null,
                { "SheetName": "Sheet1", "Address": "C3" },
                { "SheetName": "Sheet1" }
              ],
              "Scenarios": [
                null,
                {
                  "Name": "Scenario 1",
                  "ChangingCells": [
                    null,
                    { "SheetName": "Missing", "Address": "A1", "Value": "drop" },
                    { "SheetName": "Sheet1", "Address": "D4", "Value": "keep", "ValueType": "Text" }
                  ]
                },
                { "Name": "NoValidChanges", "ChangingCells": [ null ] }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        workbook.NamedRanges.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, GridRange>(
                "Input",
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2))));
        workbook.WatchedCells.Should().ContainSingle()
            .Which.Should().Be(new CellAddress(sheet.Id, 3, 3));

        var scenario = workbook.Scenarios.Should().ContainSingle().Subject;
        scenario.Name.Should().Be("Scenario 1");
        scenario.ChangingCells.Should().ContainSingle()
            .Which.Should().Be(new ScenarioCellValue(new CellAddress(sheet.Id, 4, 4), new TextValue("keep")));
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
    public void Save_DropsNullNativeJsonWorkbookViewAndScenarioEntries()
    {
        var workbook = new Workbook("NullWorkbookViewAndScenarioEntries");
        var sheet = workbook.AddSheet("Sheet1");
        var validAddress = new CellAddress(sheet.Id, 1, 1);

        workbook.CustomViews.Add(null!);
        workbook.CustomViews.Add(new WorkbookCustomView("EmptySheets", null!));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "KeptView",
            [
                null!,
                new WorksheetCustomViewState(
                    sheet.Name,
                    WorksheetViewMode.PageLayout,
                    FrozenRows: 0,
                    FrozenCols: 0,
                    SplitRow: null,
                    SplitColumn: null,
                    ZoomPercent: 125)
            ]));
        workbook.Scenarios.Add(null!);
        workbook.Scenarios.Add(new WorkbookScenario("NoChanges", null!));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Kept",
            [null!, new ScenarioCellValue(validAddress, new TextValue("kept"))]));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var customViews = document.RootElement
            .GetProperty("CustomViews").EnumerateArray().ToList();
        customViews.Should().HaveCount(2);
        customViews[0].GetProperty("Name").GetString().Should().Be("EmptySheets");
        customViews[0].GetProperty("Sheets").EnumerateArray().Should().BeEmpty();
        customViews[1].GetProperty("Name").GetString().Should().Be("KeptView");
        customViews[1].GetProperty("Sheets").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("ZoomPercent").GetInt32().Should().Be(125);

        var scenarioJson = document.RootElement
            .GetProperty("Scenarios").EnumerateArray().Should().ContainSingle().Subject;
        scenarioJson.GetProperty("Name").GetString().Should().Be("Kept");
        scenarioJson.GetProperty("ChangingCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
    }

    [Fact]
    public void Load_DropsNullNativeJsonCustomViewSheetEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullCustomViewSheetEntries",
              "Sheets": [
                { "Name": "Sheet1" }
              ],
              "CustomViews": [
                {
                  "Name": "Mixed",
                  "Sheets": [
                    null,
                    { "SheetName": " " },
                    { "SheetName": "Missing", "ZoomPercent": 120 },
                    { "SheetName": "Sheet1", "ZoomPercent": 125 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var view = new NativeJsonAdapter().Load(stream).CustomViews
            .Should().ContainSingle().Subject;

        view.Name.Should().Be("Mixed");
        var sheetState = view.Sheets.Should().ContainSingle().Subject;
        sheetState.SheetName.Should().Be("Sheet1");
        sheetState.ZoomPercent.Should().Be(125);
    }
}
