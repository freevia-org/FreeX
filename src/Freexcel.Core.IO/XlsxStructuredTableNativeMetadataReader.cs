using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxStructuredTableNativeMetadataReader
{
    public static IReadOnlyDictionary<string, string>? ReadTableAttributes(XElement table)
    {
        string[] modeledAttributes =
        [
            "id",
            "name",
            "displayName",
            "ref",
            "totalsRowShown",
            "headerRowCount",
            "totalsRowCount",
            "insertRow",
            "insertRowShift",
            "published",
            "comment"
        ];

        return ReadUnmodeledAttributesOrNull(table, modeledAttributes);
    }

    public static IReadOnlyList<string>? ReadTableChildXmls(XElement table, XNamespace workbookNs)
    {
        XName[] modeledChildren =
        [
            workbookNs + "autoFilter",
            workbookNs + "sortState",
            workbookNs + "tableColumns",
            workbookNs + "tableStyleInfo"
        ];

        return ReadUnmodeledChildrenOrNull(table, element => !modeledChildren.Contains(element.Name));
    }

    public static IReadOnlyDictionary<string, string>? ReadAutoFilterAttributes(XElement? autoFilter) =>
        autoFilter is null
            ? null
            : ReadUnmodeledAttributesOrNull(autoFilter, ["ref"]);

    public static IReadOnlyList<string>? ReadAutoFilterChildXmls(XElement? autoFilter, XNamespace workbookNs) =>
        autoFilter is null
            ? null
            : ReadUnmodeledChildrenOrNull(autoFilter, element => element.Name != workbookNs + "filterColumn");

    public static IReadOnlyDictionary<string, string>? ReadStyleInfoAttributes(XElement? styleInfo) =>
        styleInfo is null
            ? null
            : ReadUnmodeledAttributesOrNull(
                styleInfo,
                ["name", "showFirstColumn", "showLastColumn", "showRowStripes", "showColumnStripes"]);

    public static IReadOnlyList<string>? ReadStyleInfoChildXmls(XElement? styleInfo) =>
        styleInfo is null
            ? null
            : ReadUnmodeledChildrenOrNull(styleInfo, _ => true);

    public static IReadOnlyList<string> ReadColumnChildXmls(XElement column, XNamespace workbookNs) =>
        column.Elements()
            .Where(element =>
                element.Name != workbookNs + "calculatedColumnFormula" &&
                element.Name != workbookNs + "totalsRowFormula")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    public static IReadOnlyDictionary<string, string> ReadColumnAttributes(XElement column) =>
        ReadUnmodeledAttributes(column, ["id", "name", "totalsRowLabel", "totalsRowFunction"]);

    public static IReadOnlyList<string> ReadFilterXmls(XElement filterColumn, XNamespace workbookNs) =>
        filterColumn.Elements()
            .Where(element => element.Name != workbookNs + "filters")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    public static IReadOnlyDictionary<string, string>? ReadFilterColumnAttributes(XElement filterColumn) =>
        ReadUnmodeledAttributesOrNull(filterColumn, ["colId"]);

    public static bool? ReadOptionalBoolAttribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, string>? ReadUnmodeledAttributesOrNull(
        XElement element,
        IReadOnlyCollection<string> modeledAttributes)
    {
        var attributes = ReadUnmodeledAttributes(element, modeledAttributes);
        return attributes.Count == 0 ? null : attributes;
    }

    private static Dictionary<string, string> ReadUnmodeledAttributes(
        XElement element,
        IReadOnlyCollection<string> modeledAttributes) =>
        element.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);

    private static IReadOnlyList<string>? ReadUnmodeledChildrenOrNull(
        XElement element,
        Func<XElement, bool> shouldRetain)
    {
        var children = element.Elements()
            .Where(shouldRetain)
            .Select(child => child.ToString(SaveOptions.DisableFormatting))
            .ToList();
        return children.Count == 0 ? null : children;
    }
}
