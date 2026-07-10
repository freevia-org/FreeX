using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxConnectionQueryTableSchemaNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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

            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        }

        foreach (var attributeName in ConnectionBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

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

                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
            }

            foreach (var attributeName in QueryTableBooleanAttributes)
                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static void NormalizeWorksheetQueryTableParts(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            // IMPORTANT: <queryTableParts> is NOT a member of CT_Worksheet's content model in the real
            // ECMA-376/ISO-29500 schema (verified against the actual Microsoft.OpenXml schema used by
            // this project's own OpenXmlValidator-based tests: any worksheet-level <queryTableParts>
            // element -- regardless of whether its child <queryTablePart r:id="..."/> resolves to a
            // valid worksheet relationship -- is flagged as an invalid child element of <worksheet>).
            // A range<->queryTable binding is carried entirely by the relationship graph instead: the
            // worksheet's own .rels ".../relationships/queryTable" entry, xl/queryTables/*.xml, and
            // xl/connections.xml. None of that is touched here, so removing this inert/invalid marker
            // element can never sever a real binding -- there is no "dangling vs. valid" distinction to
            // make for this element; every occurrence must be stripped to keep the package schema-valid.
            var changed = false;
            foreach (var queryTableParts in root.Elements(WorksheetNs + "queryTableParts").ToList())
            {
                queryTableParts.Remove();
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static bool NormalizeRequiredUnsignedIntAttribute(XElement element, string attributeName, int fallbackValue)
    {
        var normalized = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull(element.Attribute(attributeName)?.Value) ??
                         fallbackValue.ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool IsQueryTableXmlEntry(ZipArchiveEntry entry) =>
        XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/queryTables/");

}
