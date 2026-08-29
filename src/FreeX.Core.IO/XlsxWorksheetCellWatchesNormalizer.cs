using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCellWatchesNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CellWatchAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "r" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var cellWatchContainers = worksheetRoot.Elements(WorksheetNs + "cellWatches").ToList();
        if (cellWatchContainers.Count == 0)
            return false;

        var changed = false;
        var keptContainer = false;
        foreach (var cellWatches in cellWatchContainers)
        {
            if (keptContainer)
            {
                cellWatches.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(cellWatches);
            if (!cellWatches.Elements(WorksheetNs + "cellWatch").Any())
            {
                cellWatches.Remove();
                changed = true;
                continue;
            }

            keptContainer = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement cellWatches)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(cellWatches, EmptyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(cellWatches, WorksheetNs + "cellWatch");

        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cellWatch in cellWatches.Elements(WorksheetNs + "cellWatch").ToList())
        {
            var normalizedReference = NormalizeCellReference(cellWatch.Attribute("r")?.Value);
            if (normalizedReference is null || !seenReferences.Add(normalizedReference))
            {
                cellWatch.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(cellWatch, CellWatchAttributes);
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(cellWatch, "r", normalizedReference);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(cellWatch);
        }

        return changed;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && CellAddress.TryParse(trimmed, SheetId.New(), out var address)
            ? address.ToA1()
            : null;
    }

}
