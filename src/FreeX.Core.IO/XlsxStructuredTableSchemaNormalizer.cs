using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableSchemaNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidTableTypes = ["worksheet", "xml", "queryTable"];

    private static readonly HashSet<string> ValidTotalsRowFunctions =
    [
        "none",
        "sum",
        "min",
        "max",
        "average",
        "count",
        "countNums",
        "stdDev",
        "var",
        "custom"
    ];

    private static readonly string[] TableUnsignedIntAttributes =
    [
        "headerRowCount",
        "totalsRowCount",
        "headerRowDxfId",
        "dataDxfId",
        "totalsRowDxfId",
        "headerRowBorderDxfId",
        "tableBorderDxfId",
        "totalsRowBorderDxfId",
        "connectionId"
    ];

    private static readonly string[] TableBooleanAttributes =
    [
        "insertRow",
        "insertRowShift",
        "totalsRowShown",
        "published"
    ];

    private static readonly string[] TableColumnUnsignedIntAttributes =
    [
        "queryTableFieldId",
        "headerRowDxfId",
        "dataDxfId",
        "totalsRowDxfId"
    ];

    private static readonly string[] TableStyleInfoBooleanAttributes =
    [
        "showFirstColumn",
        "showLastColumn",
        "showRowStripes",
        "showColumnStripes"
    ];

    public static bool NormalizeElement(XElement table, string? tablePath = null)
    {
        var changed = false;

        changed |= NormalizeRequiredUnsignedIntAttribute(table, "id", ExtractTrailingNumber(tablePath));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
            table,
            "tableType",
            value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidTableTypes));
        foreach (var attributeName in TableUnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
                table,
                attributeName,
                XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        foreach (var attributeName in TableBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
                table,
                attributeName,
                XlsxXmlNormalizationHelpers.NormalizeBoolean);

        var autoFilter = table.Element(WorksheetNs + "autoFilter");
        if (autoFilter is not null)
            changed |= NormalizeAutoFilterExtensionLists(autoFilter);

        var sortState = table.Element(WorksheetNs + "sortState");
        if (sortState is not null)
            changed |= NormalizeSortStateExtensionLists(sortState);

        var tableColumns = table.Element(WorksheetNs + "tableColumns");
        if (tableColumns is not null)
            changed |= NormalizeTableColumnsElement(tableColumns);

        var tableStyleInfo = table.Element(WorksheetNs + "tableStyleInfo");
        if (tableStyleInfo is not null)
            changed |= NormalizeTableStyleInfoElement(tableStyleInfo);

        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(table);
        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(table, TableChildOrder);
        return changed;
    }

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var tableEntry in archive.Entries.Where(IsStructuredTableXmlEntry).ToList())
        {
            var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
            var root = tableXml.Root;
            if (root is not null &&
                NormalizeElement(root, tableEntry.FullName))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
            }
        }
    }

    private static bool NormalizeTableColumnsElement(XElement tableColumns)
    {
        var changed = false;
        var columns = tableColumns.Elements(WorksheetNs + "tableColumn").ToArray();
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            tableColumns,
            "count",
            columns.Length.ToString(CultureInfo.InvariantCulture));

        for (var index = 0; index < columns.Length; index++)
            changed |= NormalizeTableColumnElement(columns[index], index + 1);

        changed |= RemoveExtensionLists(tableColumns);
        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(tableColumns, TableColumnsChildOrder);
        return changed;
    }

    private static bool NormalizeTableColumnElement(XElement tableColumn, int fallbackId)
    {
        var changed = false;

        changed |= NormalizeRequiredUnsignedIntAttribute(tableColumn, "id", fallbackId);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
            tableColumn,
            "totalsRowFunction",
            value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidTotalsRowFunctions));
        foreach (var attributeName in TableColumnUnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
                tableColumn,
                attributeName,
                XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        foreach (var formula in tableColumn.Elements(WorksheetNs + "calculatedColumnFormula"))
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "array", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var formula in tableColumn.Elements(WorksheetNs + "totalsRowFormula"))
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(formula, "array", XlsxXmlNormalizationHelpers.NormalizeBoolean);

        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(tableColumn);
        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(tableColumn, TableColumnChildOrder);
        return changed;
    }

    private static bool NormalizeTableStyleInfoElement(XElement tableStyleInfo)
    {
        var changed = false;
        foreach (var attributeName in TableStyleInfoBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(
                tableStyleInfo,
                attributeName,
                XlsxXmlNormalizationHelpers.NormalizeBoolean);

        return changed;
    }

    private static bool NormalizeAutoFilterExtensionLists(XElement autoFilter)
    {
        var changed = XlsxWorksheetExtensionListNormalizer.NormalizeChildren(autoFilter);

        foreach (var filterColumn in autoFilter.Elements(WorksheetNs + "filterColumn"))
            changed |= RemoveExtensionLists(filterColumn);

        foreach (var sortState in autoFilter.Elements(WorksheetNs + "sortState"))
            changed |= NormalizeSortStateExtensionLists(sortState);

        return changed;
    }

    private static bool NormalizeSortStateExtensionLists(XElement sortState)
    {
        var changed = XlsxWorksheetExtensionListNormalizer.NormalizeChildren(sortState);

        foreach (var sortCondition in sortState.Elements(WorksheetNs + "sortCondition"))
            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(sortCondition);

        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(sortState, SortStateChildOrder);
        return changed;
    }

    private static bool RemoveExtensionLists(XElement parent)
    {
        var extensionLists = parent.Elements(WorksheetNs + "extLst").ToList();
        if (extensionLists.Count == 0)
            return false;

        foreach (var extensionList in extensionLists)
            extensionList.Remove();
        return true;
    }

    private static int TableChildOrder(XElement child) =>
        child.Name == WorksheetNs + "autoFilter" ? 0 :
        child.Name == WorksheetNs + "sortState" ? 1 :
        child.Name == WorksheetNs + "tableColumns" ? 2 :
        child.Name == WorksheetNs + "tableStyleInfo" ? 3 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static int TableColumnsChildOrder(XElement child) =>
        child.Name == WorksheetNs + "tableColumn" ? 0 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static int TableColumnChildOrder(XElement child) =>
        child.Name == WorksheetNs + "calculatedColumnFormula" ? 0 :
        child.Name == WorksheetNs + "totalsRowFormula" ? 1 :
        child.Name == WorksheetNs + "xmlColumnPr" ? 2 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static int SortStateChildOrder(XElement child) =>
        child.Name == WorksheetNs + "sortCondition" ? 0 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static bool NormalizeRequiredUnsignedIntAttribute(
        XElement element,
        string attributeName,
        int fallbackValue)
    {
        var normalized = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull(element.Attribute(attributeName)?.Value) ??
            Math.Max(1, fallbackValue).ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static int ExtractTrailingNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        var start = text.Length;
        while (start > 0 && char.IsDigit(text[start - 1]))
            start--;

        var value = 0;
        for (var index = start; index < text.Length; index++)
        {
            var digit = text[index] - '0';
            if (value > (int.MaxValue - digit) / 10)
                return 1;

            value = (value * 10) + digit;
        }

        return value > 0 ? value : 1;
    }

    private static bool IsStructuredTableXmlEntry(ZipArchiveEntry entry) =>
        XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/tables/");
}
