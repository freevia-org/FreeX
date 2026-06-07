using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetGridXmlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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
    {
        var changed = false;

        if (worksheetRoot.Element(WorksheetNs + "cols") is { } columns)
            changed |= NormalizeColumnsElement(columns);
        if (worksheetRoot.Element(WorksheetNs + "sheetData") is { } sheetData)
            changed |= NormalizeSheetDataElement(sheetData);

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeColumnsElement(XElement columns)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(columns, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(columns, WorksheetNs + "col");

        foreach (var column in columns.Elements(WorksheetNs + "col").ToList())
            changed |= NormalizeColumnElement(column);

        return changed;
    }

    public static bool NormalizeSheetDataElement(XElement sheetData)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetData, EmptyAttributes);
        changed |= RemoveUnexpectedChildren(sheetData, WorksheetNs + "row");

        foreach (var row in sheetData.Elements(WorksheetNs + "row").ToList())
            changed |= NormalizeRowElement(row);

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
        changed |= SetAttributeIfChanged(column, "min", normalizedMin);
        changed |= SetAttributeIfChanged(column, "max", normalizedMax);
        changed |= NormalizeAttribute(column, "width", NormalizeNonNegativeDouble);
        changed |= NormalizeAttribute(column, "style", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(column, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "hidden", "bestFit", "customWidth", "phonetic", "collapsed" })
            changed |= NormalizeAttribute(column, attributeName, NormalizeBoolean);
        changed |= RemoveAllChildren(column);
        return changed;
    }

    private static bool NormalizeRowElement(XElement row)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(row, RowAttributes);
        changed |= NormalizeAttribute(row, "r", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(row, "spans", NormalizeCellSpans);
        changed |= NormalizeAttribute(row, "s", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(row, "ht", NormalizeNonNegativeDouble);
        changed |= NormalizeAttribute(row, "outlineLevel", NormalizeOutlineLevel);
        foreach (var attributeName in new[] { "customFormat", "hidden", "customHeight", "collapsed", "thickTop", "thickBot", "ph" })
            changed |= NormalizeAttribute(row, attributeName, NormalizeBoolean);

        changed |= NormalizeRowChildren(row);
        foreach (var cell in row.Elements(WorksheetNs + "c").ToList())
            changed |= NormalizeCellElement(cell);

        return changed;
    }

    private static bool NormalizeCellElement(XElement cell)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(cell, CellAttributes);
        changed |= NormalizeAttribute(cell, "r", NormalizeCellReference);
        changed |= NormalizeAttribute(cell, "s", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(cell, "t", value => NormalizeToken(value, CellTypeValues));
        changed |= NormalizeAttribute(cell, "ph", NormalizeBoolean);

        changed |= NormalizeCellChildren(cell);
        foreach (var formula in cell.Elements(WorksheetNs + "f").ToList())
            changed |= NormalizeFormulaElement(formula);

        return changed;
    }

    private static bool NormalizeFormulaElement(XElement formula)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(formula, FormulaAttributes);
        changed |= NormalizeAttribute(formula, "t", value => NormalizeToken(value, FormulaTypeValues));
        foreach (var attributeName in new[] { "aca", "dt2D", "dtr", "del1", "del2", "ca", "bx" })
            changed |= NormalizeAttribute(formula, attributeName, NormalizeBoolean);
        changed |= NormalizeAttribute(formula, "ref", NormalizeCellRange);
        changed |= NormalizeAttribute(formula, "r1", NormalizeCellReference);
        changed |= NormalizeAttribute(formula, "r2", NormalizeCellReference);
        changed |= NormalizeAttribute(formula, "si", NormalizeUnsignedIntOrNull);
        changed |= RemoveAllChildren(formula);
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
                changed |= RemoveAllChildren(child);
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

    private static bool RemoveUnexpectedChildren(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name == allowedChildName)
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllChildren(XElement element)
    {
        if (!element.HasElements)
            return false;

        element.Elements().Remove();
        return true;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
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

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
