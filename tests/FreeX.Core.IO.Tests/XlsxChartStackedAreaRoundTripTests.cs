using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for finding R27-chart-types-deep-1: Stacked Area and 100%-Stacked Area charts
/// must survive write→read as their own <see cref="ChartType"/> — driven by the
/// <c>c:areaChart/c:grouping</c> value — rather than collapsing to a plain overlapping Area chart
/// (the pre-fix behavior, because the enum had no StackedArea/PercentStackedArea members and the
/// reader/writer hardcoded <c>grouping="standard"</c>).
/// </summary>
public sealed class XlsxChartStackedAreaRoundTripTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Theory]
    [InlineData(ChartType.Area, "standard")]
    [InlineData(ChartType.StackedArea, "stacked")]
    [InlineData(ChartType.PercentStackedArea, "percentStacked")]
    public void AreaChart_Grouping_RoundTripsThroughSaveAndReload(ChartType type, string expectedGrouping)
    {
        var workbook = new Workbook("StackedAreaRoundTrip");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("South"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 5));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = type,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);

        // The writer emits a single c:areaChart (never c:barChart) whose grouping encodes the subtype.
        var chartDoc = LoadChartXml(saved);
        chartDoc.Descendants(ChartNs + "barChart").Should().BeEmpty(
            "the area family must not fall through to the bar/column writer branch");
        var areaChart = chartDoc.Descendants(ChartNs + "areaChart").Should().ContainSingle().Subject;
        areaChart.Element(ChartNs + "grouping")!.Attribute("val")!.Value.Should().Be(expectedGrouping);

        // Reloading recovers the exact subtype rather than collapsing to a plain overlapping Area chart.
        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.Type.Should().Be(type);
    }

    [Theory]
    [InlineData("standard", ChartType.Area)]
    [InlineData("stacked", ChartType.StackedArea)]
    [InlineData("percentStacked", ChartType.PercentStackedArea)]
    public void TryReadSupportedChart_MapsRealExcelAreaGroupingToSubtype(string grouping, ChartType expectedType)
    {
        // Mirrors the <c:areaChart> Excel itself writes for the Area subtypes, to prove we read real
        // files correctly and not just our own writer's output.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:areaChart>
                    <c:grouping val="{{grouping}}"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:areaChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();
        chart.Type.Should().Be(expectedType);
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
