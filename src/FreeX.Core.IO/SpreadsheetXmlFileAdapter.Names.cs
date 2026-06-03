using System.Globalization;
using System.Text;
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

        if (!TrySplitSheetQualifiedReference(text, out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        var parts = rangeText.Split(':');
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseA1Part(parts[0], sheet.Id, out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseA1Part(endText, sheet.Id, out var end))
            return false;

        range = new GridRange(start, end);
        return true;
    }

    private static bool TrySplitSheetQualifiedReference(string text, out string sheetName, out string rangeText)
    {
        sheetName = "";
        rangeText = "";
        if (text.Length == 0)
            return false;

        if (text[0] == '\'')
        {
            var builder = new StringBuilder();
            for (var index = 1; index < text.Length; index++)
            {
                if (text[index] != '\'')
                {
                    builder.Append(text[index]);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index++;
                    continue;
                }

                if (index + 1 >= text.Length || text[index + 1] != '!')
                    return false;

                sheetName = builder.ToString();
                rangeText = text[(index + 2)..].Trim();
                return rangeText.Length > 0;
            }

            return false;
        }

        var separator = text.IndexOf('!', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        sheetName = text[..separator].Trim();
        rangeText = text[(separator + 1)..].Trim();
        return sheetName.Length > 0 && rangeText.Length > 0;
    }

    private static bool TryParseA1Part(string text, SheetId sheetId, out CellAddress address)
    {
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        return CellAddress.TryParse(normalized, sheetId, out address);
    }

    private static string FormatNamedRangeRefersTo(string sheetName, GridRange range)
    {
        var reference = range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return $"={QuoteSheetName(sheetName)}!{reference}";
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;
}
