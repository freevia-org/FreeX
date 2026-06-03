using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetSmartTags()
    {
        var workbook = new Workbook("WorksheetSmartTagsNativeJson");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SmartTags = new WorksheetSmartTagsModel
        {
            NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellSmartTags r=\"A1\"><cellSmartTag type=\"0\" deleted=\"0\"><cellSmartTagPr key=\"place\" val=\"Seattle\" customSmartTagPropertyFlag=\"keep\" /></cellSmartTag></cellSmartTags></smartTags>",
            Cells =
            [
                new WorksheetCellSmartTagsModel
                {
                    Reference = "A1",
                    Tags =
                    [
                        new WorksheetCellSmartTagModel
                        {
                            Type = "0",
                            Deleted = false,
                            Properties =
                            [
                                new WorksheetCellSmartTagPropertyModel
                                {
                                    Key = "place",
                                    Value = "Seattle",
                                    NativeAttributes = new Dictionary<string, string> { ["customSmartTagPropertyFlag"] = "keep" }
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.SmartTags.Should().BeEquivalentTo(sheet.SmartTags);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetDataConsolidation()
    {
        var workbook = new Workbook("WorksheetDataConsolidationNativeJson");
        var sheet = workbook.AddSheet("Data");
        sheet.DataConsolidation = new WorksheetDataConsolidationModel
        {
            Function = "sum",
            LeftLabels = true,
            TopLabels = true,
            Link = true,
            NativeXml = "<dataConsolidate xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" function=\"sum\" leftLabels=\"1\" topLabels=\"1\" link=\"1\" customDataConsolidationFlag=\"keep\"><dataRefs count=\"1\"><dataRef ref=\"A1:B2\" sheet=\"Data\" customDataRefFlag=\"keep\" /></dataRefs></dataConsolidate>",
            NativeAttributes = new Dictionary<string, string> { ["customDataConsolidationFlag"] = "keep" },
            References =
            [
                new WorksheetDataConsolidationReferenceModel
                {
                    Reference = "A1:B2",
                    Sheet = "Data",
                    NativeAttributes = new Dictionary<string, string> { ["customDataRefFlag"] = "keep" }
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.DataConsolidation.Should().BeEquivalentTo(sheet.DataConsolidation);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetSortState()
    {
        var workbook = new Workbook("WorksheetSortStateNativeJson");
        var sheet = workbook.AddSheet("Data");
        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "A1:A3",
            CaseSensitive = true,
            SortMethod = "stroke",
            NativeXml = "<sortState xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ref=\"A1:A3\" caseSensitive=\"1\" sortMethod=\"stroke\" customSortStateFlag=\"keep\"><sortCondition ref=\"A2:A3\" descending=\"1\" sortBy=\"cellColor\" customSortConditionFlag=\"keep\" /></sortState>",
            NativeAttributes = new Dictionary<string, string> { ["customSortStateFlag"] = "keep" },
            Conditions =
            [
                new WorksheetSortConditionModel
                {
                    Reference = "A2:A3",
                    Descending = true,
                    SortBy = "cellColor",
                    NativeAttributes = new Dictionary<string, string> { ["customSortConditionFlag"] = "keep" }
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.SortState.Should().BeEquivalentTo(sheet.SortState);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidWorksheetSortStateRanges()
    {
        const string json = """
        {
          "Name": "WorksheetSortStateInvalidRangeLoad",
          "Sheets": [
            {
              "Name": "Data",
              "SortState": {
                "Reference": "XFE1:XFE3",
                "CaseSensitive": true,
                "Conditions": [
                  { "Reference": "A2:A3", "Descending": true, "SortBy": "cellColor" },
                  { "Reference": "A0:A1", "Descending": true }
                ]
              }
            }
          ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        loaded.SortState.Should().NotBeNull();
        loaded.SortState!.Reference.Should().BeNull();
        loaded.SortState.Conditions.Should().ContainSingle()
            .Which.Reference.Should().Be("A2:A3");
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsInvalidWorksheetSortStateRanges()
    {
        var workbook = new Workbook("WorksheetSortStateInvalidRangeSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "XFE1:XFE3",
            CaseSensitive = true,
            Conditions =
            [
                new WorksheetSortConditionModel
                {
                    Reference = "A2:A3",
                    Descending = true,
                    SortBy = "cellColor"
                },
                new WorksheetSortConditionModel
                {
                    Reference = "A0:A1",
                    Descending = true
                }
            ]
        };

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var document = JsonDocument.Parse(stream);
        var sortState = document.RootElement
            .GetProperty("Sheets")[0]
            .GetProperty("SortState");

        sortState.GetProperty("Reference").ValueKind.Should().Be(JsonValueKind.Null);
        var conditions = sortState.GetProperty("Conditions").EnumerateArray().ToList();
        conditions.Should().ContainSingle();
        conditions[0].GetProperty("Reference").GetString().Should().Be("A2:A3");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_AdditionalWorksheetViews()
    {
        var workbook = new Workbook("AdditionalWorksheetViewsNativeJson");
        var sheet = workbook.AddSheet("Data");
        sheet.AdditionalViews = new WorksheetAdditionalViewsModel
        {
            NativeAttributes = new Dictionary<string, string> { ["nativeSheetViewsAttr"] = "kept" },
            Views =
            [
                new WorksheetAdditionalViewModel
                {
                    WorkbookViewId = "1",
                    NativeXml = "<sheetView xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" workbookViewId=\"1\" view=\"pageLayout\" customSheetViewFlag=\"keep\" />",
                    NativeAttributes = new Dictionary<string, string> { ["customSheetViewFlag"] = "keep" }
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.AdditionalViews.Should().BeEquivalentTo(sheet.AdditionalViews);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_HeaderFooterPictures()
    {
        var workbook = new Workbook("HeaderPicture");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3, 4], "image/png", "logo.png", 120, 48);
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(picture, null, null);

        var adapter = new NativeJsonAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.PageHeader.Left.Should().Be("&[Picture]");
        loaded.PageHeaderPictures.Left.Should().NotBeNull();
        loaded.PageHeaderPictures.Left!.ImageBytes.Should().Equal([1, 2, 3, 4]);
        loaded.PageHeaderPictures.Left.ContentType.Should().Be("image/png");
        loaded.PageHeaderPictures.Left.FileName.Should().Be("logo.png");
        loaded.PageHeaderPictures.Left.Width.Should().Be(120);
        loaded.PageHeaderPictures.Left.Height.Should().Be(48);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_IgnoredFormulaErrors()
    {
        var workbook = new Workbook("IgnoredErrors");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        cell.IgnoreFormulaError = true;
        sheet.SetCell(address, cell);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).GetCell(1, 1)!.IgnoreFormulaError.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ErrorCheckingOptions()
    {
        var workbook = new Workbook("ErrorCheckingOptions");
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);
        workbook.DisabledFormulaErrorCodes.Add(NumberStoredAsTextCode);
        workbook.DisabledFormulaErrorCodes.Add(FormulaRefersToBlankCellsCode);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.DisabledFormulaErrorCodes.Should().BeEquivalentTo(
            ErrorValue.DivByZero.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsUnsupportedErrorCheckingOptions()
    {
        const string json = """
        {
          "Name": "ErrorCheckingOptions",
          "DisabledFormulaErrorCodes": [ "#DIV/0!", "NumberStoredAsText", "FormulaRefersToBlankCells", "#NOT-AN-EXCEL-RULE!" ],
          "Sheets": [ { "Name": "Sheet1" } ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = new NativeJsonAdapter().Load(ms);

        loaded.DisabledFormulaErrorCodes.Should().BeEquivalentTo(
            ErrorValue.DivByZero.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsUnsupportedErrorCheckingOptions()
    {
        var workbook = new Workbook("ErrorCheckingSaveSanitize");
        workbook.AddSheet("Sheet1");
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.Ref.Code);
        workbook.DisabledFormulaErrorCodes.Add(NumberStoredAsTextCode);
        workbook.DisabledFormulaErrorCodes.Add(FormulaRefersToBlankCellsCode);
        workbook.DisabledFormulaErrorCodes.Add("#NOT-AN-EXCEL-RULE!");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var codes = document.RootElement.GetProperty("DisabledFormulaErrorCodes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        codes.Should().BeEquivalentTo(
            ErrorValue.Ref.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WatchedCells()
    {
        var workbook = new Workbook("WatchTest");
        var sheet = workbook.AddSheet("Sheet1");
        var watched = new CellAddress(sheet.Id, 2, 3);
        sheet.SetFormula(watched, "A1+1");
        workbook.WatchedCells.Add(watched);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loaded.WatchedCells.Should().ContainSingle()
            .Which.Should().Be(new CellAddress(loadedSheet.Id, 2, 3));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Scenarios()
    {
        var workbook = new Workbook("ScenarioTest");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(42)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 2, 1), new TextValue("manual"))
            ],
            "Scenario comment",
            Hidden: true,
            Locked: true,
            User: "FreeXTest"));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var scenario = loaded.Scenarios.Should().ContainSingle().Subject;
        scenario.Name.Should().Be("Best Case");
        scenario.Comment.Should().Be("Scenario comment");
        scenario.Hidden.Should().BeTrue();
        scenario.Locked.Should().BeTrue();
        scenario.User.Should().Be("FreeXTest");
        scenario.ChangingCells.Should().Contain(new ScenarioCellValue(
            new CellAddress(loadedSheet.Id, 1, 1),
            new NumberValue(42)));
        scenario.ChangingCells.Should().Contain(new ScenarioCellValue(
            new CellAddress(loadedSheet.Id, 2, 1),
            new TextValue("manual")));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetIgnoredErrorsMetadata()
    {
        var workbook = new Workbook("IgnoredErrorsNativeJson");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));
        sheet.GetCell(1, 1)!.IgnoreFormulaError = true;
        sheet.IgnoredErrorsMetadata = new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes =
            {
                ["nativeContainer"] = "kept"
            },
            ErrorNativeAttributes =
            {
                ["A1"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["twoDigitTextYear"] = "1",
                    ["nativeIgnoredError"] = "kept"
                }
            }
        };

        var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var loaded = new NativeJsonAdapter().Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.GetCell(1, 1)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.IgnoredErrorsMetadata.Should().BeEquivalentTo(sheet.IgnoredErrorsMetadata);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetCellWatchesMetadata()
    {
        var workbook = new Workbook("CellWatchesNativeJson");
        var sheet = workbook.AddSheet("Data");
        workbook.WatchedCells.Add(new CellAddress(sheet.Id, 1, 1));
        sheet.CellWatchesMetadata = new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes =
            {
                ["nativeContainer"] = "kept"
            },
            WatchNativeAttributes =
            {
                ["A1"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeWatch"] = "kept"
                }
            }
        };

        var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var loaded = new NativeJsonAdapter().Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);

        loaded.WatchedCells.Should().ContainSingle(address =>
            address.Sheet.Equals(loadedSheet.Id) &&
            address.Row == 1 &&
            address.Col == 1);
        loadedSheet.CellWatchesMetadata.Should().BeEquivalentTo(sheet.CellWatchesMetadata);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetCustomPropertyMetadata()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesNativeJson");
        var sheet = workbook.AddSheet("Data");
        sheet.CustomProperties.Add(new WorksheetCustomProperty(
            "FreeXModeledProperty",
            7,
            MakeBag("customPr",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["unsupportedAttr"] = "kept"
                },
                ["<fx:customPrChild xmlns:fx=\"urn:freex:test\" value=\"kept\" />"])));

        var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var loaded = new NativeJsonAdapter().Load(stream);

        loaded.GetSheetAt(0).CustomProperties.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(sheet.CustomProperties[0]);
    }
}
