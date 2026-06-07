using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCalculationPropertyNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> CalculationPropertyAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "fullCalcOnLoad" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var calculationProperties = worksheetRoot.Elements(WorksheetNs + "sheetCalcPr").ToList();
        if (calculationProperties.Count == 0)
            return false;

        var changed = false;
        var keptCalculationProperties = false;
        foreach (var sheetCalcPr in calculationProperties)
        {
            if (keptCalculationProperties)
            {
                sheetCalcPr.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetCalcPr, CalculationPropertyAttributes);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetCalcPr, "fullCalcOnLoad", NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(sheetCalcPr);

            if (!sheetCalcPr.HasAttributes)
            {
                sheetCalcPr.Remove();
                changed = true;
                continue;
            }

            keptCalculationProperties = true;
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" => "1",
            "false" => "0",
            _ => null
        };
    }

}
