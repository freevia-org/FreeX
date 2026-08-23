using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSheetFormatNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14AcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac";

    private static readonly IReadOnlySet<string> SheetFormatAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "baseColWidth",
            "defaultColWidth",
            "defaultRowHeight",
            "customHeight",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        };

    private static readonly string[] BooleanAttributes =
    [
        "customHeight",
        "zeroHeight",
        "thickTop",
        "thickBottom"
    ];

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var sheetFormats = worksheetRoot.Elements(WorksheetNs + "sheetFormatPr").ToList();
        if (sheetFormats.Count == 0)
            return false;

        var changed = false;
        var sheetFormat = sheetFormats[0];
        foreach (var duplicate in sheetFormats.Skip(1))
        {
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(sheetFormat);
        return changed;
    }

    public static bool NormalizeElement(XElement sheetFormat)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(sheetFormat, SheetFormatAttributes, X14AcNs + "dyDescent");
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, "baseColWidth", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, "defaultColWidth", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, "defaultRowHeight", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, "outlineLevelRow", NormalizeOutlineLevel);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, "outlineLevelCol", NormalizeOutlineLevel);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, X14AcNs + "dyDescent", NormalizeNonNegativeDouble);

        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sheetFormat, attributeName, XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);

        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(sheetFormat);
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

    private static string? NormalizeNonNegativeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0)
        {
            return null;
        }

        return XlsxNumberFormatting.ToXmlString(parsed);
    }

    private static string? NormalizeOutlineLevel(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
               parsed <= 8
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

}
