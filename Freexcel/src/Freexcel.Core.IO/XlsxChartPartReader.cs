using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static bool TryReadSupportedChart(XDocument chartXml, SheetId sheetId, out ChartModel chart)
    {
        chart = new ChartModel();
        var plotArea = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "plotArea");
        var barCharts = plotArea?.Elements(ChartNs + "barChart").ToList() ?? [];
        var barChart = barCharts.FirstOrDefault();
        var lineCharts = plotArea?.Elements(ChartNs + "lineChart").ToList() ?? [];
        var lineChart = lineCharts.FirstOrDefault();
        var scatterCharts = plotArea?.Elements(ChartNs + "scatterChart").ToList() ?? [];
        var scatterChart = scatterCharts.FirstOrDefault();
        var areaCharts = plotArea?.Elements(ChartNs + "areaChart").ToList() ?? [];
        var areaChart = areaCharts.FirstOrDefault();
        var threeDAreaCharts = plotArea?.Elements(ChartNs + "area3DChart").ToList() ?? [];
        var threeDAreaChart = threeDAreaCharts.FirstOrDefault();
        var radarCharts = plotArea?.Elements(ChartNs + "radarChart").ToList() ?? [];
        var stockCharts = plotArea?.Elements(ChartNs + "stockChart").ToList() ?? [];
        var deferredAdvancedChart = HasDirectSupportedChart(plotArea) ? null : FindDeferredAdvancedChart(plotArea);
        var threeDBarChart = plotArea?.Element(ChartNs + "bar3DChart");
        var bubbleChart = plotArea?.Element(ChartNs + "bubbleChart");
        var threeDPieChart = plotArea?.Element(ChartNs + "pie3DChart");
        var pieChart = plotArea?.Element(ChartNs + "pieChart");
        var doughnutChart = plotArea?.Element(ChartNs + "doughnutChart");
        bool read;
        if (doughnutChart is not null)
            read = TryReadPieFamilyChart(chartXml, doughnutChart, sheetId, ChartType.Doughnut, out chart);
        else if (threeDPieChart is not null)
            read = TryReadPieFamilyChart(chartXml, threeDPieChart, sheetId, ChartType.ThreeDPie, out chart);
        else if (pieChart is not null)
            read = TryReadPieFamilyChart(chartXml, pieChart, sheetId, ChartType.Pie, out chart);
        else if (bubbleChart is not null)
            read = TryReadBubbleChart(chartXml, bubbleChart, sheetId, out chart);
        else if (areaChart is not null && lineChart is not null)
            read = TryReadAreaLineComboChart(chartXml, plotArea, areaCharts, lineCharts, sheetId, out chart);
        else if (areaCharts.Count > 0)
            read = TryReadAreaChart(chartXml, plotArea, areaCharts, sheetId, ChartType.Area, out chart);
        else if (threeDAreaChart is not null)
            read = TryReadAreaChart(chartXml, plotArea, threeDAreaCharts, sheetId, ChartType.ThreeDArea, out chart);
        else if (scatterCharts.Count > 0)
            read = TryReadScatterChart(chartXml, plotArea, scatterCharts, sheetId, out chart);
        else if (barChart is not null && lineChart is not null)
            read = TryReadBarLineComboChart(chartXml, plotArea, barCharts, lineCharts, sheetId, out chart);
        else if (lineCharts.Count > 1)
            read = TryReadLineChart(chartXml, plotArea, lineCharts, sheetId, out chart);
        else if (lineChart is not null)
            read = TryReadLineChart(chartXml, plotArea, [lineChart], sheetId, out chart);
        else if (radarCharts.Count > 0)
            read = TryReadLineLikeChart(chartXml, plotArea, radarCharts, sheetId, ChartType.Radar, out chart);
        else if (stockCharts.Count > 0)
            read = TryReadStockChart(chartXml, plotArea, stockCharts, barCharts, sheetId, out chart);
        else if (threeDBarChart is not null)
            read = TryReadThreeDBarChart(chartXml, threeDBarChart, sheetId, out chart);
        else if (deferredAdvancedChart is { } advanced)
            read = TryReadDeferredAdvancedChart(chartXml, advanced.Element, sheetId, advanced.Type, out chart);
        else if (barChart is not null)
            read = TryReadBarChart(chartXml, plotArea, barCharts, sheetId, out chart);
        else
            return false;

        if (read)
        {
            XlsxChartMetadataReader.ApplyPackageMetadata(chartXml, chart);
            ApplyChartBehaviorMetadata(chartXml, chart);
            ApplyPivotSourceMetadata(chartXml, chart);
        }

        return read;
    }

}
