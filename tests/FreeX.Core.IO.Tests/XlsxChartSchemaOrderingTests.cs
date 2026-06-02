using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxChartSchemaOrderingTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void LineChart_WithFormattedLabelsAxesAndSmoothSeries_ProducesSchemaValidOrder()
    {
        var saved = SaveBytes(CreateLineChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();

        var chartXml = LoadChartXml(saved);
        AssertAllChartTextPropertiesStartWithBodyProperties(chartXml);

        var lineChart = chartXml.Descendants(ChartNs + "lineChart").Single();
        var series = lineChart.Elements(ChartNs + "ser").First();
        AssertChildOrder(series, "idx", "order", "tx", "spPr", "marker", "dLbls", "trendline", "cat", "val", "smooth");

        var seriesLabels = series.Element(ChartNs + "dLbls")!;
        AssertChildOrder(seriesLabels, "dLbl", "numFmt", "spPr", "txPr", "dLblPos", "showVal");
        AssertChildOrder(seriesLabels.Element(ChartNs + "dLbl")!, "idx", "numFmt", "spPr", "txPr", "dLblPos", "showVal");

        var chartLabels = lineChart.Element(ChartNs + "dLbls")!;
        AssertChildOrder(chartLabels, "numFmt", "spPr", "txPr", "dLblPos", "showLegendKey", "showVal");

        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        AssertChildOrder(valueAxis, "numFmt", "majorTickMark", "spPr", "txPr", "crossAx", "crosses", "crossBetween", "majorUnit", "minorUnit", "dispUnits");
    }

    [Fact]
    public void PieChart_WithLabelsAndFirstSliceAngle_ProducesSchemaValidOrder()
    {
        var saved = SaveBytes(CreatePieChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();

        var chartXml = LoadChartXml(saved);
        AssertAllChartTextPropertiesStartWithBodyProperties(chartXml);

        var pieChart = chartXml.Descendants(ChartNs + "pieChart").Single();
        AssertChildOrder(pieChart, "ser", "dLbls", "firstSliceAng");
        AssertChildOrder(pieChart.Element(ChartNs + "dLbls")!, "numFmt", "spPr", "txPr", "dLblPos", "showVal", "showPercent");
    }

    private static Workbook CreateLineChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingLine");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));

        for (uint row = 2; row <= 6; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 1000));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(offset * 240));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Revenue trend",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3)),
            ChartDefaultTextColor = new CellColor(31, 31, 31),
            ChartDefaultFontSize = 10,
            ChartTitleTextColor = new CellColor(31, 78, 121),
            XAxisTitle = "Month",
            YAxisTitle = "Revenue",
            AxisTitleTextColor = new CellColor(68, 68, 68),
            XAxisLabelTextColor = new CellColor(68, 68, 68),
            XAxisLineColor = new CellColor(127, 127, 127),
            YAxisMajorUnit = 1000,
            YAxisMinorUnit = 500,
            YAxisDisplayUnit = ChartAxisDisplayUnit.Thousands,
            YAxisCrossBetween = ChartAxisCrossBetween.Between,
            YAxisLabelTextColor = new CellColor(68, 68, 68),
            YAxisLineColor = new CellColor(127, 127, 127),
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendTextColor = new CellColor(89, 89, 89),
            ShowDataLabels = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number,
            DataLabelNumberFormatSourceLinked = false,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderColor = new CellColor(180, 180, 180),
            DataLabelTextColor = new CellColor(31, 31, 31),
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            DataLabelFontSize = 9,
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = true,
                ShowOutline = true,
                ShowLegendKeys = true,
                TextColor = new CellColor(89, 89, 89),
                FontSize = 9
            },
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 1.5,
                    MarkerStyle: ChartMarkerStyle.Circle,
                    MarkerSize: 6,
                    Smooth: true)
            ],
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(
                    0,
                    FillColor: new CellColor(255, 255, 255),
                    BorderColor: new CellColor(180, 180, 180),
                    TextColor: new CellColor(31, 31, 31),
                    FontSize: 9,
                    Position: ChartDataLabelPosition.Center,
                    ShowValue: true,
                    NumberFormatCode: "0.0",
                    NumberFormatSourceLinked: false)
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    0,
                    FillColor: new CellColor(242, 242, 242),
                    BorderColor: new CellColor(160, 160, 160),
                    TextColor: new CellColor(31, 31, 31),
                    FontSize: 8,
                    Position: ChartDataLabelPosition.InsideEnd,
                    ShowValue: true,
                    NumberFormatCode: "0",
                    NumberFormatSourceLinked: false)
            ],
            ShowLinearTrendline = true,
            TrendlineLabelTextColor = new CellColor(31, 31, 31),
            TrendlineLabelFontSize = 9,
            TrendlineLabelNumberFormatCode = "0.00",
            TrendlineLabelNumberFormatSourceLinked = false
        });

        return workbook;
    }

    private static Workbook CreatePieChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingPie");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Segment"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Share",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstSliceAngle = 45,
            ShowDataLabels = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Percent,
            DataLabelNumberFormatSourceLinked = false,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderColor = new CellColor(180, 180, 180),
            DataLabelTextColor = new CellColor(31, 31, 31),
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelPercentage = true
        });

        return workbook;
    }

    private static byte[] SaveBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("xl/charts/chart1.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        return XDocument.Load(entryStream);
    }

    private static void AssertAllChartTextPropertiesStartWithBodyProperties(XDocument chartXml)
    {
        foreach (var textProperties in chartXml.Descendants(ChartNs + "txPr"))
        {
            textProperties.Elements().FirstOrDefault()?.Name
                .Should().Be(DrawingNs + "bodyPr");
        }
    }

    private static void AssertChildOrder(XElement parent, params string[] expectedNames)
    {
        var childNames = parent.Elements().Select(element => element.Name.LocalName).ToList();
        var startIndex = 0;
        foreach (var expectedName in expectedNames)
        {
            var foundIndex = childNames.FindIndex(startIndex, name => name == expectedName);
            foundIndex.Should().BeGreaterThanOrEqualTo(0, $"{expectedName} should appear after the previous checked child");
            startIndex = foundIndex + 1;
        }
    }
}
