using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r190 (backlog item 17): axis TITLES were captured and written from the X* fields whatever the
/// chart type, while every other per-axis property is routed by physical position -- both
/// XlsxChartAxisReader and XlsxChartXmlWriter compute valueAxisOnX / categoryAxisIsOnY and send
/// reverse-order, gridlines, tick styles, line, crosses, bounds and number formats through it
/// (R16/R47/R62/R71 each extended that convention by one more property).
///
/// For a bar-family chart the category axis is physically on the LEFT and the value axis along the
/// BOTTOM, so the two model fields swap relative to the axes' roles. The renderer already followed
/// the physical convention -- ChartRenderer builds the left axis as
/// <c>CreateCategoryAxis(AxisPosition.Left, chart.YAxisTitle)</c> and the bottom one from
/// <c>chart.XAxisTitle</c> -- so a bar chart's two axis titles were drawn on each other's axes.
/// Reader and writer were symmetric with each other, which is why round-tripping FreeX's own files
/// never exposed it; opening a bar chart authored elsewhere did.
/// </summary>
public sealed class R190_BarChartAxisTitleRoutingTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R190BarChartAxisTitles");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static ChartModel RoundTrip(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    [Theory]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    public void BarFamily_CategoryTitleLivesInY_AndValueTitleInX(ChartType type)
    {
        // The physical reading: left axis (category) = Y, bottom axis (value) = X -- the same
        // orientation the renderer draws and the sanitizer validates bounds against.
        var reloaded = RoundTrip(new ChartModel
        {
            Type = type,
            YAxisTitle = "Region",
            XAxisTitle = "Revenue",
        });

        reloaded.YAxisTitle.Should().Be("Region", "the left category axis title round-trips in Y");
        reloaded.XAxisTitle.Should().Be("Revenue", "the bottom value axis title round-trips in X");
    }

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Line)]
    public void NonBarFamily_KeepsCategoryOnXAndValueOnY(ChartType type)
    {
        // The change must not disturb the ordinary orientation, where the category axis IS the
        // bottom axis and the existing X=category reading is already the physical one.
        var reloaded = RoundTrip(new ChartModel
        {
            Type = type,
            XAxisTitle = "Quarter",
            YAxisTitle = "Revenue",
        });

        reloaded.XAxisTitle.Should().Be("Quarter");
        reloaded.YAxisTitle.Should().Be("Revenue");
    }

    [Fact]
    public void BarChart_CategoryTitleIsWrittenOntoTheCategoryAxisElement()
    {
        // Reader and writer were symmetric before this fix, so a round-trip alone cannot prove the
        // title reached the right ELEMENT. This asserts against the emitted XML directly.
        var workbook = CreateWorkbookWithChart(new ChartModel
        {
            Type = ChartType.Bar,
            YAxisTitle = "Region",
            XAxisTitle = "Revenue",
        });

        var bytes = XlsxPackageTestHelper.SaveToBytes(workbook);
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var xml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml");

        // Axis title text is DrawingML <a:t>, not <c:v> (which carries series caches).
        xml.Descendants(ChartNs + "catAx").Single()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "t").First().Value
            .Should().Be("Region", "the category axis is the left one on a bar chart");
        xml.Descendants(ChartNs + "valAx").First()
            .Element(ChartNs + "title")!.Descendants(DrawingNs + "t").First().Value
            .Should().Be("Revenue");
    }
}
