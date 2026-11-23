using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Reading charts the way current Microsoft Excel writes them (<c>AddChart2</c> default style). The XML
/// fragments below are the verbatim elements Excel 365 emitted for a clustered column chart with a title,
/// axis labels and "Add Data Labels"; the expected values come from the same chart as Excel reported them
/// through COM (title 14 pt regular, tick labels automatic rotation, labels shown on the series).
/// </summary>
public sealed class ExcelChartStyleReadingTests
{
    private const string Ns =
        "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"";

    private const string GreyText =
        "<a:solidFill><a:schemeClr val=\"tx1\"><a:lumMod val=\"65000\"/><a:lumOff val=\"35000\"/></a:schemeClr></a:solidFill>";

    [Fact]
    public void Automatic_tick_label_rotation_sentinel_does_not_turn_axis_labels_vertical()
    {
        var plotArea = XElement.Parse(
            $"<c:plotArea {Ns}><c:barChart><c:barDir val=\"col\"/><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/>" +
            "<c:txPr><a:bodyPr rot=\"-60000000\" vert=\"horz\"/><a:lstStyle/><a:p><a:pPr>" +
            $"<a:defRPr sz=\"900\">{GreyText}</a:defRPr></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr><c:crossAx val=\"2\"/></c:catAx>" +
            "<c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/>" +
            "<c:txPr><a:bodyPr rot=\"-60000000\" vert=\"horz\"/><a:lstStyle/><a:p><a:pPr>" +
            $"<a:defRPr sz=\"900\">{GreyText}</a:defRPr></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr><c:crossAx val=\"1\"/></c:valAx>" +
            "</c:plotArea>");
        var chart = new ChartModel { Type = ChartType.Column };

        XlsxChartAxisReader.ApplyAxisMetadata(plotArea, chart);

        chart.XAxisLabelAngle.Should().Be(0, "rot=-60000000 is Excel's automatic-rotation sentinel, not -90 degrees");
        chart.YAxisLabelAngle.Should().Be(0);
        chart.XAxisLabelFontSize.Should().Be(9, "Excel's default tick label size is 9 pt");
    }

    [Fact]
    public void Explicit_tick_label_rotation_is_still_honoured()
    {
        var plotArea = XElement.Parse(
            $"<c:plotArea {Ns}><c:barChart><c:barDir val=\"col\"/><c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/>" +
            "<c:txPr><a:bodyPr rot=\"-2700000\" vert=\"horz\"/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"900\"/></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr>" +
            "<c:crossAx val=\"2\"/></c:catAx>" +
            "<c:valAx><c:axId val=\"2\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"1\"/></c:valAx>" +
            "</c:plotArea>");
        var chart = new ChartModel { Type = ChartType.Column };

        XlsxChartAxisReader.ApplyAxisMetadata(plotArea, chart);

        chart.XAxisLabelAngle.Should().Be(-45);
    }

    [Fact]
    public void Title_style_is_read_from_the_title_text_properties_Excel_writes()
    {
        var title = XElement.Parse(
            $"<c:title {Ns}><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr/></a:pPr><a:r><a:rPr lang=\"en-US\"/><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx>" +
            "<c:overlay val=\"0\"/><c:txPr><a:bodyPr rot=\"0\" vert=\"horz\" anchor=\"ctr\" anchorCtr=\"1\"/><a:lstStyle/><a:p><a:pPr>" +
            $"<a:defRPr sz=\"1400\" b=\"0\" i=\"0\">{GreyText}</a:defRPr></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr></c:title>");
        var chart = new ChartModel();

        XlsxChartFormattingReader.ApplyChartTitleFormatting(title, chart);

        chart.ChartTitleFontSize.Should().Be(14, "Excel reported ChartTitle.Font.Size = 14");
        chart.ChartTitleBold.Should().BeFalse("the default chart title is not bold");
        chart.ChartTitleTextThemeColor.Should().NotBeNull("the title is tx1 at 65% (595959), not black");
    }

    [Fact]
    public void Run_properties_still_win_over_the_title_defaults()
    {
        var title = XElement.Parse(
            $"<c:title {Ns}><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\" sz=\"2000\" b=\"1\"/><a:t>Big</a:t></a:r></a:p></c:rich></c:tx>" +
            "<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1400\" b=\"0\"/></a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr></c:title>");
        var chart = new ChartModel();

        XlsxChartFormattingReader.ApplyChartTitleFormatting(title, chart);

        chart.ChartTitleFontSize.Should().Be(20);
        chart.ChartTitleBold.Should().BeTrue();
    }

    [Fact]
    public void Series_level_data_labels_with_empty_group_labels_are_shown_for_the_labelled_series_only()
    {
        // "Add Data Labels" on series 1 of a two series chart: the group-level dLbls stays all zero.
        var plotArea = XElement.Parse(
            $"<c:plotArea {Ns}><c:barChart><c:barDir val=\"col\"/>" +
            "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:dLbls><c:dLblPos val=\"outEnd\"/><c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/>" +
            "<c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls></c:ser>" +
            "<c:ser><c:idx val=\"1\"/><c:order val=\"1\"/></c:ser>" +
            "<c:dLbls><c:showLegendKey val=\"0\"/><c:showVal val=\"0\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart></c:plotArea>");
        var chart = new ChartModel { Type = ChartType.Column };
        var series = plotArea.Descendants(XName.Get("ser", "http://schemas.openxmlformats.org/drawingml/2006/chart")).ToList();
        for (var i = 0; i < series.Count; i++)
            XlsxChartDataLabelReader.ApplyPointDataLabels(series[i], i, chart);

        XlsxChartDataLabelReader.ApplyDataLabels(plotArea, chart);

        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelValue.Should().BeTrue();
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        chart.SeriesDataLabelFormats.Should().Contain(f => f.SeriesIndex == 1 && f.IsDeleted == true,
            "the series without its own labels must stay unlabelled");
        chart.SeriesDataLabelFormats.Should().NotContain(f => f.SeriesIndex == 0 && f.IsDeleted == true);
    }

    [Fact]
    public void Group_with_no_visible_series_labels_stays_unlabelled()
    {
        var plotArea = XElement.Parse(
            $"<c:plotArea {Ns}><c:barChart><c:barDir val=\"col\"/>" +
            "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/></c:ser>" +
            "<c:dLbls><c:showLegendKey val=\"0\"/><c:showVal val=\"0\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart></c:plotArea>");
        var chart = new ChartModel { Type = ChartType.Column };

        XlsxChartDataLabelReader.ApplyDataLabels(plotArea, chart);

        chart.ShowDataLabels.Should().BeFalse();
        chart.SeriesDataLabelFormats.Should().BeEmpty();
    }
}
