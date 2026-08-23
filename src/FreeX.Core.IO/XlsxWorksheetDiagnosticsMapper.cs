using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    private static Dictionary<string, string> GetSheetPaths(
        ZipArchive archive,
        ZipArchiveEntry workbookEntry,
        XNamespace workbookNs)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        return XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool IsTruthy(string? value) =>
        value is "1" ||
        value is not null && bool.TryParse(value, out var parsed) && parsed;

}

internal sealed record IgnoredErrorLayout(
    IReadOnlyList<CellAddress> ExpandedCells,
    IReadOnlyList<GridRange> ExistingCellOnlyRanges);
