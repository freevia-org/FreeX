using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Persists a cached <c>&lt;v&gt;</c> (and the matching <c>t</c> type attribute) onto every formula
/// cell — <c>&lt;f&gt;…&lt;/f&gt;</c> — that was serialized WITHOUT a cached result.
///
/// <para>Why this is required: the full-save path writes formula TEXT (via ClosedXML's
/// <c>FormulaA1</c> / <c>FormulaArrayA1</c>) but no cached result, and preserved Excel source XML can
/// also carry formulas without a <c>&lt;v&gt;</c>. When FreeX reloads such a file it calls
/// <c>XLCell.Value</c>, and ClosedXML lazily RECALCULATES any formula that has no cached value
/// (<c>XLCalcEngine.Recalculate</c>). That recompute is fragile: modern dynamic-array functions throw
/// <c>NotImplementedException: Array formulas not implemented</c> (from
/// <c>SignatureAdapter.ToText</c>), and incomplete cross-sheet caches throw spurious cycle errors.
/// Excel itself always writes a cached <c>&lt;v&gt;</c> for every formula; writing the value FreeX
/// already holds in the model makes the reload read the cache instead of recomputing.</para>
///
/// <para>For multi-cell array (spill) anchors only the top-left scalar is written — that is the value
/// the <c>&lt;f&gt;</c> cell itself carries in the XLSX; the spilled cells round-trip as their own
/// value cells.</para>
/// </summary>
internal static class XlsxWorksheetFormulaCachedValueWriter
{
    public static void Save(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        foreach (var sheet in workbook.Sheets)
        {
            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            if (ApplyCachedValues(edit.Root, sheet))
                session.MarkDirty(edit);
        }
    }

    private static bool ApplyCachedValues(XElement root, Sheet sheet)
    {
        var worksheetNs = root.Name.Namespace;
        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is null)
            return false;

        var changed = false;
        var fName = worksheetNs + "f";
        var vName = worksheetNs + "v";
        var isName = worksheetNs + "is";

        foreach (var cell in sheetData.Elements(worksheetNs + "row").Elements(worksheetNs + "c"))
        {
            var formula = cell.Element(fName);
            if (formula is null)
                continue;

            // Already carries a cached value — ClosedXML won't recompute, nothing to do.
            if (cell.Element(vName) is not null || cell.Element(isName) is not null)
                continue;

            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrEmpty(reference) ||
                !CellAddress.TryParse(reference, default, out var address))
            {
                continue;
            }

            var modelCell = sheet.GetCell(address.Row, address.Col);
            var value = modelCell?.Value ?? sheet.GetValue(address.Row, address.Col);
            if (WriteCachedValue(cell, worksheetNs, formula, value))
                changed = true;
        }

        return changed;
    }

    private static bool WriteCachedValue(
        XElement cell,
        XNamespace worksheetNs,
        XElement formula,
        ScalarValue value)
    {
        switch (value)
        {
            case BlankValue:
                // No representable cached value; emit an empty numeric cache so ClosedXML still skips
                // recomputation. (Matches Excel's "0" cache for a formula that evaluated to blank.)
                cell.Attribute("t")?.Remove();
                formula.AddAfterSelf(new XElement(worksheetNs + "v", "0"));
                return true;
            case TextValue text:
                cell.SetAttributeValue("t", "str");
                formula.AddAfterSelf(new XElement(worksheetNs + "v", XlsxXmlTextEscaper.EscapeForXml(text.Value)));
                return true;
            case BoolValue boolean:
                cell.SetAttributeValue("t", "b");
                formula.AddAfterSelf(new XElement(worksheetNs + "v", boolean.Value ? "1" : "0"));
                return true;
            // #SPILL!/#CALC! ARE valid OOXML error codes that real Excel writes verbatim as t="e"
            // formula-cached values. ClosedXML's own XLError enum can't represent them (only the 7
            // classic codes), so XLCell.Value/MapFormulaValue returns BlankValue for such a cell —
            // but XlsxWorksheetCellLayoutReader.ReadCachedFormulaErrors raw-parses the <c t="e"><f/>
            // <v>#SPILL!</v></c> XML directly (bypassing ClosedXML's enum) and XlsxFileAdapter falls
            // back to that dictionary whenever MapFormulaValue comes back blank (see
            // XlsxFileAdapter.cs's `xmlLayout?.CachedFormulaErrors` fallback), so this round-trips
            // correctly today. #CIRCULAR! is a FreeX-only sentinel (RecalcEngine.AddCyclicCell), not
            // a valid OOXML error code at all — real Excel never writes it: with iterative
            // calculation off (the only path that stamps this value) Excel persists a plain 0 for a
            // non-iterative circular reference. Mirrors MapValueInverse's identical decision for the
            // non-formula path; the raw-XML fallback reader's `_ => new ErrorValue(rawValue)` branch
            // would otherwise happily round-trip "#CIRCULAR!" itself, which is not what Excel does.
            case ErrorValue error when error.Code.Equals("#CIRCULAR!", StringComparison.OrdinalIgnoreCase):
                cell.Attribute("t")?.Remove();
                formula.AddAfterSelf(new XElement(worksheetNs + "v", "0"));
                return true;
            case ErrorValue error:
                cell.SetAttributeValue("t", "e");
                formula.AddAfterSelf(new XElement(worksheetNs + "v", error.Code));
                return true;
            case DateTimeValue dateTime:
                cell.Attribute("t")?.Remove();
                formula.AddAfterSelf(new XElement(worksheetNs + "v", FormatNumber(dateTime.Value)));
                return true;
            case NumberValue number:
                cell.Attribute("t")?.Remove();
                formula.AddAfterSelf(new XElement(worksheetNs + "v", FormatNumber(number.Value)));
                return true;
            default:
                return false;
        }
    }

    private static string FormatNumber(double value) =>
        XlsxNumberFormatting.ToXmlString(value);
}
