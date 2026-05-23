using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ResolvePivotChartCacheBindings(Workbook workbook)
    {
        foreach (var chartSheet in workbook.Sheets)
        {
            foreach (var chart in chartSheet.Charts.Where(chart =>
                         chart.IsPivotChart &&
                         chart.PivotCacheId is null &&
                         !string.IsNullOrWhiteSpace(chart.PivotTableName)))
            {
                var sourceSheet = string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
                    ? chartSheet
                    : workbook.Sheets.FirstOrDefault(sheet =>
                        string.Equals(sheet.Name, chart.PivotSourceSheetName, StringComparison.OrdinalIgnoreCase));
                var pivot = sourceSheet?.PivotTables.FirstOrDefault(pivot =>
                    string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
                if (pivot is not null)
                    chart.PivotCacheId = pivot.CacheId;
            }
        }
    }

    private static void ApplyChartExternalDataRelationshipMetadata(ChartModel chart, XlsxChartPackagePart chartPart)
    {
        if (chart.ExternalData?.RelationshipId is not { Length: > 0 } relationshipId)
            return;

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationship = chartPart.Relationships?.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Id")?.Value,
                relationshipId,
                StringComparison.Ordinal));
        if (relationship is null)
            return;

        chart.ExternalData.RelationshipType = relationship.Attribute("Type")?.Value;
        chart.ExternalData.Target = relationship.Attribute("Target")?.Value;
        chart.ExternalData.TargetMode = relationship.Attribute("TargetMode")?.Value;
    }

}
