using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static void ApplyChartBehaviorMetadata(XDocument chartXml, ChartModel chart)
    {
        var chartElement = chartXml.Root?.Element(ChartNs + "chart");
        chart.AutoTitleDeleted = XlsxChartScalarReader.IsTrue(chartElement?
            .Element(ChartNs + "autoTitleDeleted")?
            .Attribute("val")?
            .Value);

        chart.BlankDisplayMode = chartElement?
            .Element(ChartNs + "dispBlanksAs")?
            .Attribute("val")?
            .Value switch
            {
                "span" => ChartBlankDisplayMode.Span,
                "zero" => ChartBlankDisplayMode.Zero,
                _ => ChartBlankDisplayMode.Gap
            };

        chart.ShowDataLabelsOverMaximum = XlsxChartScalarReader.IsTrue(chartElement?
            .Element(ChartNs + "showDLblsOverMax")?
            .Attribute("val")?
            .Value);

        chart.ShowDataInHiddenRowsAndColumns = chartElement?
            .Element(ChartNs + "plotVisOnly")?
            .Attribute("val")?
            .Value is "0" or "false" or "False";
    }

    private static void ApplyPivotSourceMetadata(XDocument chartXml, ChartModel chart)
    {
        chart.PivotFormatsXml = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "pivotFmts")?
            .ToString(SaveOptions.DisableFormatting);

        var pivotSourceName = chartXml.Root?
            .Element(ChartNs + "pivotSource")?
            .Element(ChartNs + "name")?
            .Value;
        if (string.IsNullOrWhiteSpace(pivotSourceName))
            return;

        chart.IsPivotChart = true;
        chart.PivotSourceSheetName = ExtractPivotSourceSheetName(pivotSourceName);
        chart.PivotTableName = ExtractPivotTableName(pivotSourceName);
        chart.PivotSourceFormatId = XlsxChartScalarReader.ReadOptionalInt(chartXml.Root?
            .Element(ChartNs + "pivotSource")?
            .Element(ChartNs + "fmtId")?
            .Attribute("val")?
            .Value);
    }

    private static string? ExtractPivotSourceSheetName(string pivotSourceName)
    {
        var bangIndex = pivotSourceName.LastIndexOf('!');
        if (bangIndex <= 0)
            return null;

        return UnquoteSheetName(pivotSourceName[..bangIndex].Trim());
    }

    private static string ExtractPivotTableName(string pivotSourceName)
    {
        var bangIndex = pivotSourceName.LastIndexOf('!');
        var name = bangIndex >= 0 ? pivotSourceName[(bangIndex + 1)..] : pivotSourceName;
        return name.Trim().Trim('\'');
    }

    private static string UnquoteSheetName(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return value;
    }
}
