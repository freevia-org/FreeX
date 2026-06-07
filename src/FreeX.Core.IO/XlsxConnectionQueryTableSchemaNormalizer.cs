using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxConnectionQueryTableSchemaNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly string[] ConnectionUnsignedIntAttributes =
    [
        "id",
        "minRefreshableVersion",
        "refreshedVersion",
        "reconnectionMethod",
        "interval"
    ];

    private static readonly string[] ConnectionBooleanAttributes =
    [
        "deleted",
        "onlyUseConnectionFile",
        "background",
        "refreshOnLoad",
        "saveData",
        "savePassword",
        "new",
        "keepAlive"
    ];

    private static readonly string[] QueryTableUnsignedIntAttributes =
    [
        "connectionId",
        "autoFormatId"
    ];

    private static readonly string[] QueryTableBooleanAttributes =
    [
        "headers",
        "rowNumbers",
        "disableRefresh",
        "backgroundRefresh",
        "firstBackgroundRefresh",
        "refreshOnLoad",
        "fillFormulas",
        "removeDataOnSave",
        "disableEdit",
        "preserveFormatting",
        "adjustColumnWidth",
        "intermediate",
        "applyNumberFormats",
        "applyBorderFormats",
        "applyFontFormats",
        "applyPatternFormats",
        "applyAlignmentFormats",
        "applyWidthHeightFormats"
    ];

    public static void NormalizePackage(ZipArchive archive)
    {
        NormalizeConnections(archive);
        NormalizeQueryTables(archive);
        NormalizeWorksheetQueryTableParts(archive);
    }

    private static void NormalizeConnections(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/connections.xml");
        if (entry is null)
            return;

        var document = XlsxPackageXmlEditor.LoadXml(entry);
        var root = document.Root;
        if (root is null)
            return;

        var changed = false;
        var count = root.Attribute("count");
        if (count is not null)
        {
            count.Remove();
            changed = true;
        }

        var connections = root.Elements(WorksheetNs + "connection").ToArray();
        for (var index = 0; index < connections.Length; index++)
            changed |= NormalizeConnection(connections[index], index + 1);

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/connections.xml", document);
    }

    private static bool NormalizeConnection(XElement connection, int fallbackId)
    {
        var changed = NormalizeRequiredUnsignedIntAttribute(connection, "id", fallbackId);
        changed |= NormalizeRequiredUnsignedIntAttribute(connection, "refreshedVersion", 0);

        foreach (var attributeName in ConnectionUnsignedIntAttributes)
        {
            if (attributeName is "id" or "refreshedVersion")
                continue;

            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, NormalizeUnsignedIntOrNull);
        }

        foreach (var attributeName in ConnectionBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, NormalizeBoolean);

        return changed;
    }

    private static void NormalizeQueryTables(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(IsQueryTableXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            var changed = NormalizeRequiredUnsignedIntAttribute(root, "connectionId", 1);
            foreach (var attributeName in QueryTableUnsignedIntAttributes)
            {
                if (attributeName == "connectionId")
                    continue;

                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, NormalizeUnsignedIntOrNull);
            }

            foreach (var attributeName in QueryTableBooleanAttributes)
                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, NormalizeBoolean);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static void NormalizeWorksheetQueryTableParts(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            var changed = false;
            foreach (var queryTableParts in root.Elements(WorksheetNs + "queryTableParts").ToList())
                changed |= NormalizeQueryTablePartsElement(queryTableParts);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static bool NormalizeQueryTablePartsElement(XElement queryTableParts)
    {
        var changed = false;
        foreach (var queryTablePart in queryTableParts.Elements(WorksheetNs + "queryTablePart").ToList())
        {
            if (string.IsNullOrWhiteSpace(queryTablePart.Attribute(RelNs + "id")?.Value))
            {
                queryTablePart.Remove();
                changed = true;
            }
        }

        var count = queryTableParts
            .Elements(WorksheetNs + "queryTablePart")
            .Count()
            .ToString(CultureInfo.InvariantCulture);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(queryTableParts, "count", count);
        return changed;
    }

    private static bool NormalizeRequiredUnsignedIntAttribute(XElement element, string attributeName, int fallbackValue)
    {
        var normalized = NormalizeUnsignedIntOrNull(element.Attribute(attributeName)?.Value) ??
                         fallbackValue.ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, normalized);
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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsQueryTableXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
