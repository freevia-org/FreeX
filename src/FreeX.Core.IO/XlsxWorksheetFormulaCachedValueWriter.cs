using System.Linq;
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
/// <para>For multi-cell array (spill) anchors, the anchor's own top-left scalar is patched onto its
/// <c>&lt;f&gt;</c> cell exactly like any other formula cell above. The OTHER cells the anchor's
/// <c>FormulaArrayA1</c> range write covers are also patched here: ClosedXML materializes an empty
/// placeholder <c>&lt;c&gt;</c> for each of them but never gives any of them a value (FreeX itself
/// never assigns a <c>.Value</c> to anything but the anchor), so without this second pass every
/// non-anchor spill member would silently round-trip as Blank (R61-io-shared-formula-6-1).</para>
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

        // R61-io-shared-formula-6-1: dynamic-array spill members (SetSpillRange writes them into
        // the sheet's separate spill-value store and removes them from _cells) never appear in
        // Sheet.GetOccupiedCellMap(), so the per-cell write loop in XlsxFileAdapter.Save.cs never
        // visits them directly — it only writes the anchor's FormulaArrayA1 across the full spill
        // rectangle. ClosedXML DOES materialize an empty placeholder <c> element for every other
        // cell covered by that range write (confirmed empirically: <c r="B3" s="0" /> with no
        // value/formula), so — unlike a genuinely-unwritten cell — there is always something here
        // to patch a cached <v> onto. Collect the candidate addresses once, up front, so the common
        // case (no live spills on this sheet) pays no extra cost.
        var spillTargets = sheet.HasSpillValues ? sheet.EnumerateSpillTargetCells().ToList() : null;
        HashSet<(uint Row, uint Col)>? spillTargetKeys = spillTargets is { Count: > 0 }
            ? spillTargets.Select(static a => (a.Row, a.Col)).ToHashSet()
            : null;
        Dictionary<(uint Row, uint Col), XElement>? spillCellsByAddress =
            spillTargetKeys is not null ? new Dictionary<(uint, uint), XElement>(spillTargetKeys.Count) : null;

        foreach (var cell in sheetData.Elements(worksheetNs + "row").Elements(worksheetNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var address = default(CellAddress);
            var hasAddress = !string.IsNullOrEmpty(reference) &&
                CellAddress.TryParse(reference, default, out address);

            if (spillTargetKeys is not null && hasAddress && spillTargetKeys.Contains((address.Row, address.Col)))
                spillCellsByAddress![(address.Row, address.Col)] = cell;

            var formula = cell.Element(fName);
            if (formula is null)
                continue;

            // Already carries a cached value — ClosedXML won't recompute, nothing to do.
            if (cell.Element(vName) is not null || cell.Element(isName) is not null)
                continue;

            if (!hasAddress)
                continue;

            var modelCell = sheet.GetCell(address.Row, address.Col);
            var value = modelCell?.Value ?? sheet.GetValue(address.Row, address.Col);
            if (WriteCachedValue(cell, worksheetNs, formula, value))
                changed = true;
        }

        if (spillCellsByAddress is not null)
        {
            foreach (var addr in spillTargets!)
            {
                if (!spillCellsByAddress.TryGetValue((addr.Row, addr.Col), out var memberCell))
                    continue;

                // A non-anchor spill member never carries its own <f>; if one is somehow present
                // (unexpected — stay defensive) or a value is already there, leave it alone.
                if (memberCell.Element(fName) is not null ||
                    memberCell.Element(vName) is not null ||
                    memberCell.Element(isName) is not null)
                {
                    continue;
                }

                var value = sheet.GetValue(addr.Row, addr.Col);
                if (WriteSpillMemberCachedValue(memberCell, worksheetNs, value))
                    changed = true;
            }
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
                var textValueElement = new XElement(worksheetNs + "v", XlsxXmlTextEscaper.EscapeForXml(text.Value));
                if (text.Value.Length > 0 &&
                    (char.IsWhiteSpace(text.Value[0]) || char.IsWhiteSpace(text.Value[^1])))
                {
                    textValueElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                formula.AddAfterSelf(textValueElement);
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

    /// <summary>
    /// Writes the cached value of a non-anchor dynamic-array spill member cell — a <c>&lt;c&gt;</c>
    /// that ClosedXML already materialized (as an empty placeholder) as part of the anchor's
    /// <c>FormulaArrayA1</c> range write, but that never got a <c>&lt;v&gt;</c> because FreeX itself
    /// never assigns a <c>.Value</c> to any cell but the anchor. Unlike <see cref="WriteCachedValue"/>
    /// (which patches a formula cell's own cache), there is no <c>&lt;f&gt;</c> here to anchor the
    /// insertion to, so the value is added directly as a child of <paramref name="cell"/>. Text is
    /// written as <c>t="inlineStr"</c>/<c>&lt;is&gt;</c> rather than the formula-cache <c>t="str"</c>
    /// convention, since this cell carries no formula of its own for "str" to describe.
    /// </summary>
    private static bool WriteSpillMemberCachedValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
    {
        switch (value)
        {
            case BlankValue:
                // A genuinely blank spill member gets no <v> at all (unlike a formula's own cached
                // blank result — see WriteCachedValue) — that already round-trips as Blank, matching
                // real Excel, which likewise omits <v> for a blank cell.
                return false;
            case TextValue text:
                cell.SetAttributeValue("t", "inlineStr");
                var textElement = new XElement(worksheetNs + "t", XlsxXmlTextEscaper.EscapeForXml(text.Value));
                if (text.Value.Length > 0 &&
                    (char.IsWhiteSpace(text.Value[0]) || char.IsWhiteSpace(text.Value[^1])))
                {
                    textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                cell.Add(new XElement(worksheetNs + "is", textElement));
                return true;
            case BoolValue boolean:
                cell.SetAttributeValue("t", "b");
                cell.Add(new XElement(worksheetNs + "v", boolean.Value ? "1" : "0"));
                return true;
            // See WriteCachedValue's identical case for the #CIRCULAR!/#SPILL!/#CALC! rationale.
            case ErrorValue error when error.Code.Equals("#CIRCULAR!", StringComparison.OrdinalIgnoreCase):
                cell.Attribute("t")?.Remove();
                cell.Add(new XElement(worksheetNs + "v", "0"));
                return true;
            case ErrorValue error:
                cell.SetAttributeValue("t", "e");
                cell.Add(new XElement(worksheetNs + "v", error.Code));
                return true;
            case DateTimeValue dateTime:
                cell.Attribute("t")?.Remove();
                cell.Add(new XElement(worksheetNs + "v", FormatNumber(dateTime.Value)));
                return true;
            case NumberValue number:
                cell.Attribute("t")?.Remove();
                cell.Add(new XElement(worksheetNs + "v", FormatNumber(number.Value)));
                return true;
            default:
                return false;
        }
    }

    private static string FormatNumber(double value) =>
        XlsxNumberFormatting.ToXmlString(value);
}
