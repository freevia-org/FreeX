using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxWorkbookSheetPathReader
{
    public static IEnumerable<(string SheetName, string WorksheetPath)> GetWorkbookSheetPaths(
        XDocument workbookXml,
        IReadOnlyDictionary<string, string> workbookRels,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(name) &&
                !string.IsNullOrWhiteSpace(relId) &&
                workbookRels.TryGetValue(relId, out var worksheetPath))
            {
                yield return (name, worksheetPath);
            }
        }
    }
}
