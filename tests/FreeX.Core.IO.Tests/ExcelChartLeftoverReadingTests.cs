using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Series XML exactly as Excel 365 writes it for <c>xlXYScatter</c> (markers only) and <c>xlXYScatterLines</c>,
/// plus the radar style: the line fill decides whether a scatter/line series connects its points.
/// </summary>
public sealed class ExcelChartLeftoverReadingTests
{
    private const string Ns =
        "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";

    private static ChartSeriesFormat ReadSeries(string spPr, string marker)
    {
        var series = XElement.Parse($"<c:ser {Ns}><c:idx val=\"0\"/><c:order val=\"0\"/>{spPr}{marker}</c:ser>");
        XlsxChartSeriesFormatReader.TryReadSeriesLine(series, 0, out var format).Should().BeTrue();
        return format;
    }

    private const string Marker =
        "<c:marker><c:symbol val=\"circle\"/><c:size val=\"5\"/><c:spPr><a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill>" +
        "<a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill></a:ln></c:spPr></c:marker>";

    [Fact]
    public void Scatter_markers_only_series_reads_as_no_line()
    {
        var format = ReadSeries("<c:spPr><a:ln w=\"25400\" cap=\"rnd\"><a:noFill/><a:round/></a:ln></c:spPr>", Marker);

        format.NoLine.Should().BeTrue("plain Insert > Scatter stores <a:ln><a:noFill/>");
        format.MarkerSize.Should().Be(5, "Excel's default marker size");
    }

    [Fact]
    public void Scatter_lines_series_reads_its_line_colour_and_stays_connected()
    {
        var format = ReadSeries(
            "<c:spPr><a:ln w=\"19050\" cap=\"rnd\"><a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill><a:round/></a:ln></c:spPr>",
            Marker);

        format.NoLine.Should().BeFalse();
        format.StrokeThemeColor.Should().NotBeNull();
        format.StrokeThickness.Should().Be(1.5, "19050 EMU is 1.5 pt, the xlXYScatterLines default");
    }

    [Fact]
    public void Smooth_flag_is_read_from_the_series()
    {
        var series = XElement.Parse(
            $"<c:ser {Ns}><c:idx val=\"0\"/><c:order val=\"0\"/><c:spPr><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill></a:ln></c:spPr>" +
            "<c:smooth val=\"1\"/></c:ser>");

        XlsxChartSeriesFormatReader.TryReadSeriesLine(series, 0, out var format).Should().BeTrue();

        format.Smooth.Should().BeTrue();
    }

    [Fact]
    public void Radar_style_filled_round_trips_through_the_native_json_file()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.Charts.Add(new ChartModel { Type = ChartType.Radar, RadarFilled = true, DataRange = GridRange.Parse("A1:B2", sheet.Id) });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        ((ChartModel)reloaded.Sheets[0].Charts.Single()!).RadarFilled.Should().BeTrue("a filled radar must stay filled after .fxl save/load");
    }
}
