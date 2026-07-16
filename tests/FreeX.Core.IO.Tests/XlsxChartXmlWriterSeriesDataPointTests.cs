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
/// R45-meta-1: round 44 wired the per-data-point fill READ path (XlsxChartSeriesFormatReader
/// .ApplyPiePointFills, now called from the bar/line/scatter series loops too, see
/// XlsxChartPartReaderTests.DataPointFillNonPie.cs) but left the matching WRITE side pie-only —
/// BuildChartSeries (bar/column/line/area/radar/stock) and BuildScatterChartSeries never called
/// ToDataPointsXml, so a load-then-save of a real Excel file with a per-point "Format Data Point >
/// Fill" override on a bar/line/scatter series silently dropped the override. Fixed by calling
/// ToDataPointsXml from both builders (and BuildBubbleChartSeries) in the same idx/order/tx/spPr/
/// [marker]/dPt/dLbls/... sequence position the pie-family builder already used.
/// </summary>
public sealed class XlsxChartXmlWriterSeriesDataPointTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void BarChart_PerPointFillColor_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("BarPointFillsWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            // Mirrors Excel's "Format Data Point > Fill" override on a single column of a bar chart.
            PointFillColors = [new ChartPointFillFormat(0, 2, new CellColor(0xFF, 0x00, 0x00))],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var dPt = series.Elements(ChartNs + "dPt").Should()
            .ContainSingle("the per-point fill override must be written as a <c:dPt> element").Subject;
        dPt.Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("2");
        dPt.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("FF0000");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 2)
            .Which.FillColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void BarChart_NoPerPointOverride_EmitsNoDataPointElements()
    {
        // Sibling no-regression case: a series with no per-point override must not spuriously gain
        // a <c:dPt> element now that BuildChartSeries calls ToDataPointsXml.
        var workbook = new Workbook("BarNoPointFills");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        series.Elements(ChartNs + "dPt").Should().BeEmpty();

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.PointFillColors.Should().BeEmpty();
    }

    [Fact]
    public void ScatterChart_PerPointFillColor_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("ScatterPointFillsWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Y"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            PointFillColors =
                [new ChartPointFillFormat(0, 1, null, new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3))],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var dPt = series.Elements(ChartNs + "dPt").Should().ContainSingle().Subject;
        dPt.Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("1");
        dPt.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "schemeClr")!.Attribute("val")!.Value.Should().Be("accent3");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var point = reloaded.PointFillColors.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 1).Subject;
        point.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));
        point.FillColor.Should().BeNull();
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
