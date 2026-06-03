using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class NativeJsonSchemaTests
{
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
}
