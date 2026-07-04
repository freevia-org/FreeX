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
/// Regression coverage for review findings H25 (per-point dPt/spPr fills dropped on save),
/// H52 (series-level explicit noFill/noLine dropped on save), and H62 (category axis
/// crosses/crossesAt hardcoded to autoZero on save).
/// </summary>
public sealed class XlsxChartXmlWriterGroupNTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void PieChart_PerPointFillColors_SurviveSaveAndReload()
    {
        var workbook = new Workbook("PointFillsWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            PointFillColors =
            [
                new ChartPointFillFormat(0, 0, new CellColor(0x92, 0xD0, 0x50)),
                new ChartPointFillFormat(0, 2, null, new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)),
            ],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var dPts = series.Elements(ChartNs + "dPt").ToList();
        dPts.Should().HaveCount(2);

        var dPt0 = dPts.Single(d => d.Element(ChartNs + "idx")!.Attribute("val")!.Value == "0");
        // CT_DPt child order: idx, then spPr (no explosion here).
        dPt0.Elements().Select(e => e.Name.LocalName).Should().Equal("idx", "spPr");
        dPt0.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("92D050");

        var dPt2 = dPts.Single(d => d.Element(ChartNs + "idx")!.Attribute("val")!.Value == "2");
        dPt2.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "schemeClr")!.Attribute("val")!.Value.Should().Be("accent2");

        // Reloading re-captures the per-point fills.
        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.PointFillColors.Should().HaveCount(2);
        reloaded.PointFillColors.Should().Contain(p => p.SeriesIndex == 0 && p.PointIndex == 0 &&
            p.FillColor == new CellColor(0x92, 0xD0, 0x50));
        reloaded.PointFillColors.Should().Contain(p => p.SeriesIndex == 0 && p.PointIndex == 2 &&
            p.FillThemeColor == new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
    }

    [Fact]
    public void PieChart_ExplodedSliceAndPointFill_OnSamePoint_EmitBothInCorrectOrder()
    {
        var workbook = new Workbook("ExplodedAndFill");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.2,
            PointFillColors = [new ChartPointFillFormat(0, 1, new CellColor(0xFF, 0x00, 0x00))],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var dPt = series.Elements(ChartNs + "dPt").Should().ContainSingle().Subject;

        // CT_DPt sequence: idx, explosion, spPr.
        dPt.Elements().Select(e => e.Name.LocalName).Should().Equal("idx", "explosion", "spPr");
        dPt.Element(ChartNs + "explosion")!.Attribute("val")!.Value.Should().Be("20");
        dPt.Element(ChartNs + "spPr")!.Element(DrawingNs + "solidFill")!
            .Element(DrawingNs + "srgbClr")!.Attribute("val")!.Value.Should().Be("FF0000");
    }

    [Fact]
    public void BarChart_SeriesNoFillAndNoLine_SurviveSaveAndReload()
    {
        var workbook = new Workbook("NoFillSpacerSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesFormats = [new ChartSeriesFormat(0, NoFill: true, NoLine: true)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var spPr = series.Element(ChartNs + "spPr");
        spPr.Should().NotBeNull("an explicit noFill/noLine series format must still emit spPr");
        spPr!.Element(DrawingNs + "noFill").Should().NotBeNull();
        spPr.Element(DrawingNs + "ln")!.Element(DrawingNs + "noFill").Should().NotBeNull();

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var format = reloaded.SeriesFormats.Should().ContainSingle(f => f.SeriesIndex == 0).Subject;
        format.NoFill.Should().BeTrue();
        format.NoLine.Should().BeTrue();
    }

    [Fact]
    public void CategoryAxis_CrossesAtMaximum_RoundTripsInsteadOfHardcodedAutoZero()
    {
        var workbook = new Workbook("CategoryAxisCrosses");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            XAxisCrosses = ChartAxisCrosses.Maximum,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var catAx = chartDoc.Descendants(ChartNs + "catAx").Single();
        catAx.Element(ChartNs + "crosses")!.Attribute("val")!.Value.Should().Be("max");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.XAxisCrosses.Should().Be(ChartAxisCrosses.Maximum);
    }

    [Fact]
    public void CategoryAxis_CrossesAtCustomValue_RoundTrips()
    {
        var workbook = new Workbook("CategoryAxisCrossesAt");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            XAxisCrosses = ChartAxisCrosses.Custom,
            XAxisCrossesAt = 2.5,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var catAx = chartDoc.Descendants(ChartNs + "catAx").Single();
        catAx.Element(ChartNs + "crossesAt")!.Attribute("val")!.Value.Should().Be("2.5");
        catAx.Element(ChartNs + "crosses").Should().BeNull();

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.XAxisCrosses.Should().Be(ChartAxisCrosses.Custom);
        reloaded.XAxisCrossesAt.Should().Be(2.5);
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
