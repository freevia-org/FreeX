using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static FreeX.Core.IO.XlsxXmlNormalizationHelpers;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetGridXmlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ColumnAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "min",
            "max",
            "width",
            "style",
            "hidden",
            "bestFit",
            "customWidth",
            "phonetic",
            "outlineLevel",
            "collapsed"
        };

    private static readonly IReadOnlySet<string> RowAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "r",
            "spans",
            "s",
            "customFormat",
            "ht",
            "hidden",
            "customHeight",
            "outlineLevel",
            "collapsed",
            "thickTop",
            "thickBot",
            "ph"
        };

    private static readonly IReadOnlySet<string> CellAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "r",
            "s",
            "t",
            "cm",
            "vm",
            "ph"
        };

    private static readonly IReadOnlySet<string> CellTypeValues =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "b",
            "n",
            "e",
            "s",
            "str",
            "inlineStr",
            "d"
        };

    private static readonly IReadOnlySet<string> FormulaAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "t",
            "aca",
            "ref",
            "dt2D",
            "dtr",
            "del1",
            "del2",
            "r1",
            "r2",
            "ca",
            "si",
            "bx"
        };

    private static readonly IReadOnlySet<string> FormulaTypeValues =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "normal",
            "array",
            "dataTable",
            "shared"
        };

    private static readonly Regex CellReferencePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CellRangePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
        => NormalizeWorksheetRoot(worksheetRoot, cellMetadataCount: 0, valueMetadataCount: 0);

    internal static (uint CellMetadataCount, uint ValueMetadataCount) ReadMetadataCountsForSinglePass(ZipArchive archive)
        => ReadMetadataCounts(archive);

    internal static bool NormalizeWorksheetRoot(
        XElement worksheetRoot,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;

        if (worksheetRoot.Element(WorksheetNs + "cols") is { } columns)
            changed |= NormalizeColumnsElement(columns);
        if (worksheetRoot.Element(WorksheetNs + "sheetData") is { } sheetData)
            changed |= NormalizeSheetDataElement(sheetData, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        var (cellMetadataCount, valueMetadataCount) = ReadMetadataCounts(archive);
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root, cellMetadataCount, valueMetadataCount))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeColumnsElement(XElement columns)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(columns, EmptyAttributes);
        changed |= RemoveChildElementsExcept(columns, WorksheetNs + "col");

        foreach (var column in columns.Elements(WorksheetNs + "col").ToList())
            changed |= NormalizeColumnElement(column);

        return changed;
    }

    public static bool NormalizeSheetDataElement(XElement sheetData)
        => NormalizeSheetDataElement(sheetData, cellMetadataCount: 0, valueMetadataCount: 0);

    private static bool NormalizeSheetDataElement(
        XElement sheetData,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetData, EmptyAttributes);
        changed |= RemoveChildElementsExcept(sheetData, WorksheetNs + "row");

        foreach (var row in sheetData.Elements(WorksheetNs + "row").ToList())
            changed |= NormalizeRowElement(row, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    private static bool NormalizeColumnElement(XElement column)
    {
        var normalizedMin = NormalizeUnsignedIntOrNull(column.Attribute("min")?.Value);
        var normalizedMax = NormalizeUnsignedIntOrNull(column.Attribute("max")?.Value);
        if (normalizedMin is null || normalizedMax is null)
        {
            column.Remove();
            return true;
        }

        var changed = false;
        changed |= RemoveUnknownAttributes(column, ColumnAttributes);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(column, "min", normalizedMin);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(column, "max", normalizedMax);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "width", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "style", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "hidden", "bestFit", "customWidth", "phonetic", "collapsed" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(column, attributeName, NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(column);
        return changed;
    }

    private static bool NormalizeRowElement(
        XElement row,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(row, RowAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "r", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "spans", NormalizeCellSpans);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "s", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "ht", NormalizeNonNegativeDouble);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "customFormat", "hidden", "customHeight", "collapsed", "thickTop", "thickBot", "ph" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(row, attributeName, NormalizeBoolean);

        changed |= NormalizeRowChildren(row);
        foreach (var cell in row.Elements(WorksheetNs + "c").ToList())
            changed |= NormalizeCellElement(cell, cellMetadataCount, valueMetadataCount);

        return changed;
    }

    private static bool NormalizeCellElement(
        XElement cell,
        uint cellMetadataCount,
        uint valueMetadataCount)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(cell, CellAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "r", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "s", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "t", value => NormalizeToken(value, CellTypeValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "cm", value => NormalizeMetadataIndex(value, cellMetadataCount));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "vm", value => NormalizeMetadataIndex(value, valueMetadataCount));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(cell, "ph", NormalizeBoolean);

        changed |= NormalizeCellChildren(cell);
        foreach (var formula in cell.Elements(WorksheetNs + "f").ToList())
            changed |= NormalizeFormulaElement(formula);

        return changed;
    }

    private static bool NormalizeFormulaElement(XElement formula)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(formula, FormulaAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "t", value => NormalizeToken(value, FormulaTypeValues));
        foreach (var attributeName in new[] { "aca", "dt2D", "dtr", "del1", "del2", "ca", "bx" })
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, attributeName, NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "ref", NormalizeCellRange);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "r1", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "r2", NormalizeCellReference);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "si", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(formula);
        return changed;
    }

    private static bool NormalizeRowChildren(XElement row)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var child in row.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "c")
                continue;

            if (child.Name == WorksheetNs + "extLst")
            {
                changed |= NormalizeExtensionListChild(child, ref keptExtensionList);
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeCellChildren(XElement cell)
    {
        var changed = false;
        var seenFormula = false;
        var seenValue = false;
        var seenInlineString = false;
        var keptExtensionList = false;

        foreach (var child in cell.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "f" && !seenFormula)
            {
                seenFormula = true;
                continue;
            }

            if (child.Name == WorksheetNs + "v" && !seenValue)
            {
                seenValue = true;
                changed |= RemoveUnknownAttributes(child, EmptyAttributes);
                changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(child);
                continue;
            }

            if (child.Name == WorksheetNs + "is" && !seenInlineString)
            {
                seenInlineString = true;
                continue;
            }

            if (child.Name == WorksheetNs + "extLst")
            {
                changed |= NormalizeExtensionListChild(child, ref keptExtensionList);
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeExtensionListChild(XElement extensionList, ref bool keptExtensionList)
    {
        if (keptExtensionList)
        {
            extensionList.Remove();
            return true;
        }

        var changed = XlsxWorksheetExtensionListNormalizer.NormalizeExtensionListElement(extensionList);
        if (XlsxWorksheetExtensionListNormalizer.ShouldRemoveExtensionListElement(extensionList))
        {
            extensionList.Remove();
            return true;
        }

        keptExtensionList = true;
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name.NamespaceName == MarkupCompatNs.NamespaceName ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeMetadataIndex(string? value, uint metadataCount)
    {
        var normalized = NormalizeUnsignedIntOrNull(value);
        if (normalized is null)
            return null;

        return metadataCount > 0 &&
            uint.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed <= metadataCount
            ? normalized
            : null;
    }

    private static string? NormalizeOutlineLevel(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= 7
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeNonNegativeDouble(string? value)
    {
        var trimmed = value?.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed) &&
            parsed >= 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeCellReference(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellReferencePattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? NormalizeCellSpans(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        var spans = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var span in spans)
        {
            var parts = span.Split(':');
            if (parts.Length != 2 ||
                !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                start == 0 ||
                end == 0 ||
                start > end)
            {
                return null;
            }
        }

        return string.Join(" ", spans);
    }


    private static (uint CellMetadataCount, uint ValueMetadataCount) ReadMetadataCounts(ZipArchive archive)
    {
        var metadataEntry = archive.GetEntry("xl/metadata.xml");
        if (metadataEntry is null)
            return (0, 0);

        try
        {
            var metadataXml = XlsxPackageXmlEditor.LoadXml(metadataEntry);
            var root = metadataXml.Root;
            return (
                ReadMetadataCount(root, WorksheetNs + "cellMetadata"),
                ReadMetadataCount(root, WorksheetNs + "valueMetadata"));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static uint ReadMetadataCount(XElement? root, XName elementName)
    {
        var metadataElement = root?.Element(elementName);
        if (metadataElement is null)
            return 0;

        var countText = metadataElement.Attribute("count")?.Value?.Trim();
        if (uint.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return count;

        return (uint)metadataElement.Elements(WorksheetNs + "bk").Count();
    }
}
