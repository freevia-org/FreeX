using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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
                    : FindChartSourceSheet(workbook, chart.PivotSourceSheetName);
                var pivot = sourceSheet is null ? null : FindChartPivotTable(sourceSheet, chart.PivotTableName);
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
        var relationship = FindChartRelationship(chartPart.Relationships?.Root, packageRelNs, relationshipId);
        if (relationship is null)
            return;

        chart.ExternalData.RelationshipType = relationship.Attribute("Type")?.Value;
        chart.ExternalData.Target = relationship.Attribute("Target")?.Value;
        chart.ExternalData.TargetMode = relationship.Attribute("TargetMode")?.Value;
    }

    private static void ApplyChartUserShapesRelationshipMetadata(ChartModel chart, XlsxChartPackagePart chartPart)
    {
        if (chart.UserShapes?.RelationshipId is not { Length: > 0 } relationshipId)
            return;

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationship = FindChartRelationship(chartPart.Relationships?.Root, packageRelNs, relationshipId);
        if (relationship is null)
            return;

        chart.UserShapes.RelationshipType = relationship.Attribute("Type")?.Value;
        chart.UserShapes.Target = relationship.Attribute("Target")?.Value;
        chart.UserShapes.TargetMode = relationship.Attribute("TargetMode")?.Value;
    }

    private static Sheet? FindChartSourceSheet(Workbook workbook, string sheetName) =>
        workbook.Sheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase));

    private static PivotTableModel? FindChartPivotTable(Sheet sheet, string? pivotTableName) =>
        sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, pivotTableName, StringComparison.OrdinalIgnoreCase));

    private static XElement? FindChartRelationship(XElement? relationshipsRoot, XNamespace packageRelNs, string relationshipId) =>
        relationshipsRoot?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Id")?.Value,
                relationshipId,
                StringComparison.Ordinal));
}
