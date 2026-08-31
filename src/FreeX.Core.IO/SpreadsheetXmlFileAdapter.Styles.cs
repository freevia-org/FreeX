using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    private static Dictionary<string, StyleId> ReadStyles(Workbook workbook, XElement workbookElement)
    {
        var styles = new Dictionary<string, StyleId>(StringComparer.Ordinal);
        var stylesElement = workbookElement.Element(SpreadsheetNs + "Styles");
        if (stylesElement is null)
            return styles;

        var definitions = new Dictionary<string, StyleDefinition>(StringComparer.Ordinal);
        foreach (var styleElement in stylesElement.Elements(SpreadsheetNs + "Style"))
        {
            var id = styleElement.Attribute(SpreadsheetIdAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            definitions[id] = new StyleDefinition(
                id,
                styleElement.Attribute(SpreadsheetParentAttribute)?.Value,
                styleElement.Element(SpreadsheetNs + "NumberFormat")?.Attribute(SpreadsheetFormatAttribute)?.Value);
        }

        foreach (var styleElement in stylesElement.Elements(SpreadsheetNs + "Style"))
        {
            var id = styleElement.Attribute(SpreadsheetIdAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var numberFormat = ResolveNumberFormat(id, definitions, []);
            if (string.IsNullOrWhiteSpace(numberFormat))
                continue;

            styles[id] = workbook.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        }

        return styles;
    }

    private static string? ResolveNumberFormat(
        string styleId,
        IReadOnlyDictionary<string, StyleDefinition> definitions,
        HashSet<string> visited)
    {
        if (!visited.Add(styleId) || !definitions.TryGetValue(styleId, out var definition))
            return null;

        if (!string.IsNullOrWhiteSpace(definition.NumberFormat))
            return definition.NumberFormat;

        return string.IsNullOrWhiteSpace(definition.ParentId)
            ? null
            : ResolveNumberFormat(definition.ParentId, definitions, visited);
    }

    private sealed record StyleDefinition(string? Id, string? ParentId, string? NumberFormat);

    private static Dictionary<StyleId, string> CreateNumberFormatStyleIds(Workbook workbook)
    {
        var styleIds = new Dictionary<StyleId, string>();
        for (var index = 1; index < workbook.StyleCount; index++)
        {
            var styleId = new StyleId(index);
            var numberFormat = workbook.GetStyleNumberFormat(styleId);
            if (string.IsNullOrWhiteSpace(numberFormat) ||
                string.Equals(numberFormat, CellStyle.Default.NumberFormat, StringComparison.Ordinal))
            {
                continue;
            }

            styleIds[styleId] = $"s{index}";
        }

        return styleIds;
    }
    private static void WriteStylesElement(
        XmlWriter writer,
        Workbook workbook,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        if (styleIds.Count == 0)
            return;

        WriteSpreadsheetStartElement(writer, "Styles");
        foreach (var (styleId, styleName) in styleIds)
        {
            WriteSpreadsheetStartElement(writer, "Style");
            WriteSpreadsheetAttribute(writer, SpreadsheetIdAttribute, styleName);
            WriteSpreadsheetStartElement(writer, "NumberFormat");
            WriteSpreadsheetAttribute(writer, SpreadsheetFormatAttribute, workbook.GetStyleNumberFormat(styleId));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

}
