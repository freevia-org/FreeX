using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R36-io-chart-axis-scaling-2-2/2-3: the secondary value axis's own orientation (reversed/maxMin),
/// log scale, tick style, and crosses were never read (silently overwritten by the primary axis's
/// current settings on save), and the axis display-units label (&lt;c:dispUnitsLbl/&gt;, Excel's "Show
/// display units label on chart" checkbox) was never round-tripped at all. Each fix below pairs the
/// bug scenario with an already-working sibling case to guard against over-correcting the common
/// (no-explicit-secondary-scaling / no-display-unit-label) path.
/// </summary>
public sealed class R36_ChartAxisScalingDeepTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ComboChart_SecondaryAxisOwnReversedLogTickCrosses_RoundTripsIndependentlyOfPrimary()
    {
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        // Primary (Y) axis: normal orientation, linear scale, outside ticks, auto-zero crossing.
        chart.YAxisReverseOrder = false;
        chart.YAxisLogScale = false;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.YAxisCrosses = ChartAxisCrosses.AutoZero;
        // Secondary axis: explicitly reversed, logarithmic, no ticks, crosses at maximum — must NOT be
        // clobbered by the primary axis's settings above.
        chart.SecondaryAxisReverseOrder = true;
        chart.SecondaryAxisLogScale = true;
        chart.SecondaryAxisLogBase = 10;
        chart.SecondaryAxisMajorTickStyle = ChartAxisTickStyle.None;
        chart.SecondaryAxisCrosses = ChartAxisCrosses.Maximum;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var primaryAxis = valueAxes[0];
        var secondaryAxis = valueAxes[1];

        primaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "orientation")!.Attribute("val")!.Value.Should().Be("minMax");
        primaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "logBase").Should().BeNull();
        primaryAxis.Element(ChartNs + "majorTickMark")!.Attribute("val")!.Value.Should().Be("out");
        primaryAxis.Element(ChartNs + "crosses")!.Attribute("val")!.Value.Should().Be("autoZero");

        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "orientation")!.Attribute("val")!.Value.Should().Be("maxMin");
        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "logBase")!.Attribute("val")!.Value.Should().Be("10");
        secondaryAxis.Element(ChartNs + "majorTickMark")!.Attribute("val")!.Value.Should().Be("none");
        secondaryAxis.Element(ChartNs + "crosses")!.Attribute("val")!.Value.Should().Be("max");

        var reloaded = ReloadSingleChart(saved);
        reloaded.YAxisReverseOrder.Should().BeFalse();
        reloaded.YAxisLogScale.Should().BeFalse();
        reloaded.SecondaryAxisReverseOrder.Should().BeTrue();
        reloaded.SecondaryAxisLogScale.Should().BeTrue();
        reloaded.SecondaryAxisLogBase.Should().Be(10);
        reloaded.SecondaryAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.None);
        reloaded.SecondaryAxisCrosses.Should().Be(ChartAxisCrosses.Maximum);
    }

    [Fact]
    public void ComboChart_SecondaryAxisWithoutOwnScaling_StillClonesPrimaryAsBefore()
    {
        // Already-working sibling: a secondary axis with no explicit scaling of its own (the only
        // shape this writer supported before the fix) must keep falling back to the primary axis's
        // orientation/log-scale/tick-style/crosses, not silently drop to hardcoded defaults.
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisReverseOrder = true;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Inside;
        chart.YAxisCrosses = ChartAxisCrosses.Minimum;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var secondaryAxis = valueAxes[1];
        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "orientation")!.Attribute("val")!.Value.Should().Be("maxMin");
        secondaryAxis.Element(ChartNs + "majorTickMark")!.Attribute("val")!.Value.Should().Be("in");
        secondaryAxis.Element(ChartNs + "crosses")!.Attribute("val")!.Value.Should().Be("min");

        var reloaded = ReloadSingleChart(saved);
        reloaded.SecondaryAxisReverseOrder.Should().BeTrue();
        reloaded.SecondaryAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        reloaded.SecondaryAxisCrosses.Should().Be(ChartAxisCrosses.Minimum);
    }

    [Fact]
    public void ColumnChart_DisplayUnitsLabelShown_RoundTripsDispUnitsLblElement()
    {
        var workbook = CreateSingleColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisDisplayUnit = ChartAxisDisplayUnit.Thousands;
        chart.ShowYAxisDisplayUnitLabel = true;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        var dispUnits = valueAxis.Element(ChartNs + "dispUnits");
        dispUnits.Should().NotBeNull();
        dispUnits!.Element(ChartNs + "builtInUnit")!.Attribute("val")!.Value.Should().Be("thousands");
        dispUnits.Element(ChartNs + "dispUnitsLbl").Should().NotBeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.YAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Thousands);
        reloaded.ShowYAxisDisplayUnitLabel.Should().BeTrue();
    }

    [Fact]
    public void ColumnChart_DisplayUnitsWithoutLabel_StillRoundTripsScalingWithoutLblElement()
    {
        // Already-working sibling: display units WITHOUT the "show label" checkbox (the only shape
        // this writer supported before the fix) must keep round-tripping the numeric scaling and must
        // NOT gain a <c:dispUnitsLbl/> element that was never requested.
        var workbook = CreateSingleColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisDisplayUnit = ChartAxisDisplayUnit.Millions;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        var dispUnits = valueAxis.Element(ChartNs + "dispUnits");
        dispUnits.Should().NotBeNull();
        dispUnits!.Element(ChartNs + "builtInUnit")!.Attribute("val")!.Value.Should().Be("millions");
        dispUnits.Element(ChartNs + "dispUnitsLbl").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.YAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Millions);
        reloaded.ShowYAxisDisplayUnitLabel.Should().BeFalse();
    }

    private static Workbook CreateSingleColumnWorkbook()
    {
        var workbook = new Workbook("ChartAxisScalingDeep");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("A"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Item{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10000));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true
        });

        return workbook;
    }

    private static Workbook CreateColumnLineComboWorkbook()
    {
        var workbook = new Workbook("ChartAxisScalingSecondaryDeep");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Growth"));
        for (uint row = 2; row <= 5; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 100));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(70 + (offset * 8)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(0.15 + (offset * 0.02)));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales, units, and growth",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1, 2]
        });

        return workbook;
    }

    private static ChartModel ReloadSingleChart(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        return new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
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
