using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageLayoutNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly IReadOnlySet<string> PrintOptionsBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "gridLines",
            "headings",
            "horizontalCentered",
            "verticalCentered",
            "gridLinesSet"
        };

    private static readonly IReadOnlySet<string> PageMarginsAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "left",
            "right",
            "top",
            "bottom",
            "header",
            "footer"
        };

    private static readonly IReadOnlySet<string> PageSetupUnsignedIntAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "paperSize",
            "scale",
            "firstPageNumber",
            "fitToWidth",
            "fitToHeight",
            "horizontalDpi",
            "verticalDpi",
            "copies"
        };

    private static readonly IReadOnlySet<string> PageSetupBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "usePrinterDefaults",
            "blackAndWhite",
            "draft",
            "useFirstPageNumber"
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PageSetupTokenAttributes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["pageOrder"] = new HashSet<string>(StringComparer.Ordinal) { "downThenOver", "overThenDown" },
            ["orientation"] = new HashSet<string>(StringComparer.Ordinal) { "default", "portrait", "landscape" },
            ["cellComments"] = new HashSet<string>(StringComparer.Ordinal) { "none", "asDisplayed", "atEnd" },
            ["errors"] = new HashSet<string>(StringComparer.Ordinal) { "displayed", "blank", "dash", "NA" }
        };

    private static readonly IReadOnlySet<string> PageSetupAllowedAttributeNames =
        new HashSet<string>(
            PageSetupUnsignedIntAttributes
                .Concat(PageSetupBooleanAttributes)
                .Concat(PageSetupTokenAttributes.Keys),
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> HeaderFooterBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "differentOddEven",
            "differentFirst",
            "scaleWithDoc",
            "alignWithMargins"
        };

    private static readonly IReadOnlySet<string> HeaderFooterChildNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "oddHeader",
            "oddFooter",
            "evenHeader",
            "evenFooter",
            "firstHeader",
            "firstFooter"
        };

    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PageSetupPropertiesBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "fitToPage", "autoPageBreaks" };

    private static readonly IReadOnlySet<string> OutlinePropertiesBooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "summaryBelow",
            "summaryRight",
            "showOutlineSymbols",
            "applyStyles"
        };

    private static readonly IReadOnlyDictionary<string, string> PageMarginsFallbacks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["left"] = "0.5",
            ["right"] = "0.5",
            ["top"] = "0.5",
            ["bottom"] = "0.5",
            ["header"] = "0.3",
            ["footer"] = "0.3"
        };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;

        if (worksheetRoot.Element(WorksheetNs + "printOptions") is { } printOptions)
            changed |= NormalizePrintOptions(printOptions);
        if (worksheetRoot.Element(WorksheetNs + "pageMargins") is { } pageMargins)
            changed |= NormalizePageMargins(pageMargins);
        if (worksheetRoot.Element(WorksheetNs + "pageSetup") is { } pageSetup)
            changed |= NormalizePageSetup(pageSetup);
        if (worksheetRoot.Element(WorksheetNs + "headerFooter") is { } headerFooter)
            changed |= NormalizeHeaderFooter(headerFooter);

        changed |= XlsxWorksheetSheetPropertiesNormalizer.NormalizeWorksheetRoot(worksheetRoot);

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is not null &&
                NormalizeWorksheetRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    public static bool NormalizePrintOptions(XElement printOptions)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(printOptions, PrintOptionsBooleanAttributes);
        foreach (var attributeName in PrintOptionsBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(printOptions, attributeName, NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(printOptions);
        return changed;
    }

    public static bool NormalizePageMargins(XElement pageMargins)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pageMargins, PageMarginsAttributes);
        foreach (var attributeName in PageMarginsAttributes)
        {
            var normalized = NormalizeNonNegativeDouble(pageMargins.Attribute(attributeName)?.Value) ??
                PageMarginsFallbacks[attributeName];
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(pageMargins, attributeName, normalized);
        }

        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(pageMargins);
        return changed;
    }

    public static bool NormalizePageSetup(XElement pageSetup)
    {
        var changed = false;
        changed |= RemoveUnknownPageSetupAttributes(pageSetup);

        foreach (var attributeName in PageSetupUnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pageSetup, attributeName, NormalizeUnsignedIntOrNull);
        foreach (var attributeName in PageSetupBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pageSetup, attributeName, NormalizeBoolean);
        foreach (var (attributeName, allowedValues) in PageSetupTokenAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pageSetup, attributeName, value => NormalizeToken(value, allowedValues));

        changed |= NormalizeRelationshipIdAttribute(pageSetup);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(pageSetup);
        return changed;
    }

    public static bool NormalizeHeaderFooter(XElement headerFooter)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(headerFooter, HeaderFooterBooleanAttributes);
        foreach (var attributeName in HeaderFooterBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(headerFooter, attributeName, NormalizeBoolean);

        foreach (var child in headerFooter.Elements().ToList())
        {
            if (child.Name.Namespace == WorksheetNs && HeaderFooterChildNames.Contains(child.Name.LocalName))
            {
                changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(child, EmptyAttributes);
                changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(child);
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeSheetPropertiesPageLayout(XElement sheetProperties)
    {
        var changed = false;

        if (sheetProperties.Element(WorksheetNs + "pageSetUpPr") is { } pageSetupProperties)
        {
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pageSetupProperties, PageSetupPropertiesBooleanAttributes);
            foreach (var attributeName in PageSetupPropertiesBooleanAttributes)
                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(pageSetupProperties, attributeName, NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(pageSetupProperties);
        }

        if (sheetProperties.Element(WorksheetNs + "outlinePr") is { } outlineProperties)
        {
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(outlineProperties, OutlinePropertiesBooleanAttributes);
            foreach (var attributeName in OutlinePropertiesBooleanAttributes)
                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(outlineProperties, attributeName, NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(outlineProperties);
        }

        return changed;
    }

    private static bool RemoveUnknownPageSetupAttributes(XElement pageSetup)
    {
        var changed = false;
        foreach (var attribute in pageSetup.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && PageSetupAllowedAttributeNames.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeRelationshipIdAttribute(XElement pageSetup)
    {
        var attribute = pageSetup.Attribute(RelationshipNs + "id");
        if (attribute is null)
            return false;

        var normalized = attribute.Value.Trim();
        if (normalized.Length == 0)
        {
            attribute.Remove();
            return true;
        }

        if (string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        attribute.Value = normalized;
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

    private static string? NormalizeNonNegativeDouble(string? value)
    {
        var trimmed = value?.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed) &&
            parsed >= 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
