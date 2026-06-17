using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// A chartsheet discovered in an XLSX package: a full-page chart-only sheet whose
/// <c>&lt;sheet&gt;</c> entry in <c>xl/workbook.xml</c> points (via its relationship) to a
/// chartsheet part rather than a worksheet part.
/// </summary>
/// <param name="Name">The sheet tab name from <c>xl/workbook.xml</c>.</param>
/// <param name="WorkbookSheetIndex">
/// The zero-based position of this sheet within the workbook's <c>&lt;sheets&gt;</c> order, used to
/// insert the chartsheet at the correct tab position relative to worksheets.
/// </param>
/// <param name="ChartPart">The chart package part referenced by the chartsheet's drawing, if any.</param>
/// <param name="IsHidden">Whether the chartsheet is hidden.</param>
/// <param name="IsVeryHidden">Whether the chartsheet is very hidden.</param>
internal sealed record XlsxChartsheet(
    string Name,
    int WorkbookSheetIndex,
    XlsxChartPackagePart? ChartPart,
    bool IsHidden,
    bool IsVeryHidden);

/// <summary>
/// Loads chartsheets from an XLSX package. ClosedXML only surfaces worksheets via
/// <c>XLWorkbook.Worksheets</c>, so chartsheets are otherwise silently dropped on load. This reader
/// enumerates the workbook's <c>&lt;sheets&gt;</c> list, detects entries whose relationship targets a
/// chartsheet part, and resolves each chartsheet's single full-page chart by reusing the worksheet
/// drawing/chart reader (a chartsheet root carries the same <c>&lt;drawing r:id="..."/&gt;</c>
/// element a worksheet does).
/// </summary>
internal static class XlsxChartsheetReader
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<XlsxChartsheet> Read(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return [];

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            PackageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);
        var relTypes = ReadRelationshipTypes(relsXml);

        var sheets = workbookXml.Root?.Element(WorkbookNs + "sheets");
        if (sheets is null)
            return [];

        List<XlsxChartsheet>? result = null;
        var index = -1;
        foreach (var sheetElement in sheets.Elements(WorkbookNs + "sheet"))
        {
            index++;
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(RelNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!relTargets.TryGetValue(relId, out var partPath))
                continue;
            if (!IsChartsheetRelationship(relId, partPath, relTypes))
                continue;

            var chartPart = ReadChartsheetChartPart(archive, partPath);
            var (isHidden, isVeryHidden) = ReadVisibility(sheetElement);
            result ??= [];
            result.Add(new XlsxChartsheet(name, index, chartPart, isHidden, isVeryHidden));
        }

        return result ?? [];
    }

    private static bool IsChartsheetRelationship(
        string relId,
        string partPath,
        IReadOnlyDictionary<string, string> relTypes)
    {
        if (relTypes.TryGetValue(relId, out var type) &&
            type.EndsWith("/chartsheet", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return partPath.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase);
    }

    private static XlsxChartPackagePart? ReadChartsheetChartPart(ZipArchive archive, string chartsheetPath)
    {
        var chartsheetEntry = archive.GetEntry(chartsheetPath);
        if (chartsheetEntry is null)
            return null;

        // A chartsheet root (<chartsheet>) carries a <drawing r:id="..."/> child just like a
        // worksheet root, so the existing worksheet drawing reader resolves the drawing part,
        // its chart relationship, and the chart package part for us.
        var chartsheetXml = XlsxPackageXmlEditor.LoadXml(chartsheetEntry);
        var drawingParts = XlsxWorksheetDrawingPartReader.ReadParts(archive, chartsheetPath, chartsheetXml);
        return drawingParts.ChartParts.Count > 0 ? drawingParts.ChartParts[0] : null;
    }

    private static (bool IsHidden, bool IsVeryHidden) ReadVisibility(XElement sheetElement)
    {
        var state = sheetElement.Attribute("state")?.Value;
        return state switch
        {
            "hidden" => (true, false),
            "veryHidden" => (true, true),
            _ => (false, false)
        };
    }

    private static Dictionary<string, string> ReadRelationshipTypes(XDocument relsXml)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (relsXml.Root is null)
            return result;

        foreach (var relationship in relsXml.Root.Elements(PackageRelNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var type = relationship.Attribute("Type")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(type))
                result[id] = type;
        }

        return result;
    }
}
