using System.Globalization;
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
        changed |= RemoveUnknownAttributes(protection);
        changed |= NormalizeAttribute(protection, "password", NormalizeOptionalText);
        changed |= NormalizeAttribute(protection, "algorithmName", NormalizeOptionalText);
        changed |= NormalizeAttribute(protection, "hashValue", NormalizeBase64BinaryOrNull);
        changed |= NormalizeAttribute(protection, "saltValue", NormalizeBase64BinaryOrNull);
        changed |= NormalizeAttribute(protection, "spinCount", NormalizeUnsignedIntOrNull);
        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(protection, attributeName, NormalizeBoolean);
        changed |= RemoveLegacyPasswordWhenAdvancedHashExists(protection);
        changed |= RemoveAllNodes(protection);
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

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && ProtectionAttributes.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
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

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    private static bool RemoveLegacyPasswordWhenAdvancedHashExists(XElement protection)
    {
        var password = protection.Attribute("password");
        if (password is null || protection.Attribute("hashValue") is null)
            return false;

        password.Remove();
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "1";
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        return null;
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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
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
