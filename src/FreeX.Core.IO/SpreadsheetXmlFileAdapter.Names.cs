using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    private static void ReadNamedRanges(Workbook workbook, XElement workbookElement)
    {
        var namesElement = workbookElement.Element(SpreadsheetNs + "Names");
        if (namesElement is null)
            return;

        foreach (var namedRangeElement in namesElement.Elements(SpreadsheetNs + "NamedRange"))
        {
            var name = namedRangeElement.Attribute(SpreadsheetNameAttribute)?.Value?.Trim();
            var refersTo = namedRangeElement.Attribute(SpreadsheetRefersToAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                workbook.ValidateNamedRangeName(name) is not null ||
                !TryParseNamedRangeRefersTo(workbook, refersTo, out var range))
            {
                continue;
            }

            workbook.DefineNamedRange(name, range);
        }
    }

    private static void WriteNamesElement(XmlWriter writer, Workbook workbook)
    {
        var wroteNames = false;
        foreach (var (name, range) in workbook.NamedRanges.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryFormatNamedRangeRefersTo(workbook, name, range, out var refersTo))
                continue;

            if (!wroteNames)
            {
                WriteSpreadsheetStartElement(writer, "Names");
                wroteNames = true;
            }

            WriteSpreadsheetStartElement(writer, "NamedRange");
            WriteSpreadsheetAttribute(writer, SpreadsheetNameAttribute, name);
            WriteSpreadsheetAttribute(writer, SpreadsheetRefersToAttribute, refersTo);
            writer.WriteEndElement();
        }

        if (wroteNames)
            writer.WriteEndElement();
    }

    private static bool TryFormatNamedRangeRefersTo(
        Workbook workbook,
        string name,
        GridRange range,
        out string refersTo)
    {
        refersTo = "";
        if (workbook.ValidateNamedRangeName(name) is not null ||
            workbook.GetSheet(range.Start.Sheet) is not { } sheet ||
            !IsValidGridRange(range))
        {
            return false;
        }

        refersTo = FormatNamedRangeRefersTo(sheet.Name, range);
        return true;
    }

    private static bool TryParseNamedRangeRefersTo(Workbook workbook, string? refersTo, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(refersTo))
            return false;

        var text = refersTo.Trim();
        if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].Trim();

        return WorkbookNamedRangeReferenceParser.TryParse(workbook, text, out range);
    }

    private static string FormatNamedRangeRefersTo(string sheetName, GridRange range)
    {
        var reference = range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return $"={SheetNameFormatter.QuoteIfNeeded(sheetName)}!{reference}";
    }
}
