using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetProtectionNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly IReadOnlySet<string> ProtectionAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "password",
            "algorithmName",
            "hashValue",
            "saltValue",
            "spinCount",
            "sheet",
            "objects",
            "scenarios",
            "formatCells",
            "formatColumns",
            "formatRows",
            "insertColumns",
            "insertRows",
            "insertHyperlinks",
            "deleteColumns",
            "deleteRows",
            "selectLockedCells",
            "sort",
            "autoFilter",
            "pivotTables",
            "selectUnlockedCells"
        };

    private static readonly IReadOnlySet<string> BooleanAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "sheet",
            "objects",
            "scenarios",
            "formatCells",
            "formatColumns",
            "formatRows",
            "insertColumns",
            "insertRows",
            "insertHyperlinks",
            "deleteColumns",
            "deleteRows",
            "selectLockedCells",
            "sort",
            "autoFilter",
            "pivotTables",
            "selectUnlockedCells"
        };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var protections = worksheetRoot.Elements(WorksheetNs + "sheetProtection").ToList();
        if (protections.Count == 0)
            return false;

        var changed = false;
        var protection = protections[0];
        foreach (var duplicate in protections.Skip(1))
        {
            changed |= MergeProtectionAttributes(protection, duplicate);
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(protection);
        return changed;
    }

    public static bool NormalizeElement(XElement protection)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(protection, ProtectionAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, "password", NormalizeLegacyPasswordHashOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, "algorithmName", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, "hashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, "saltValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, "spinCount", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protection, attributeName, XlsxXmlNormalizationHelpers.NormalizeBooleanAsNumeric);
        changed |= RemoveLegacyPasswordWhenAdvancedHashExists(protection);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(protection);
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

    private static bool MergeProtectionAttributes(XElement target, XElement source)
    {
        var changed = false;
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || target.Attribute(attribute.Name) is not null)
                continue;

            target.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool RemoveLegacyPasswordWhenAdvancedHashExists(XElement protection)
    {
        var password = protection.Attribute("password");
        if (password is null || protection.Attribute("hashValue") is null)
            return false;

        password.Remove();
        return true;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeBase64BinaryOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        try
        {
            _ = Convert.FromBase64String(trimmed);
            return trimmed;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeLegacyPasswordHashOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is not { Length: 4 } ||
            !trimmed.All(static c => char.IsAsciiHexDigit(c)))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

}
