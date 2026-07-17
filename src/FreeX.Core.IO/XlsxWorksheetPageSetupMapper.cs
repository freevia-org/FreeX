using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageSetupMapper
{
    public static void LoadPrintArea(IXLWorksheet xlSheet, Sheet sheet)
    {
        var areas = new List<GridRange>();
        foreach (var xlRange in xlSheet.PageSetup.PrintAreas)
        {
            var start = new CellAddress(
                sheet.Id,
                (uint)xlRange.RangeAddress.FirstAddress.RowNumber,
                (uint)xlRange.RangeAddress.FirstAddress.ColumnNumber);
            var end = new CellAddress(
                sheet.Id,
                (uint)xlRange.RangeAddress.LastAddress.RowNumber,
                (uint)xlRange.RangeAddress.LastAddress.ColumnNumber);
            areas.Add(new GridRange(start, end));
        }

        if (areas.Count > 0)
            sheet.SetPrintAreas(areas);
    }

    public static void SetHeaderFooter(
        IXLHeaderFooter target,
        WorksheetHeaderFooter oddOrAllPages,
        WorksheetHeaderFooter firstPage,
        WorksheetHeaderFooter evenPages,
        bool differentFirstPage,
        bool differentOddEvenPages)
    {
        foreach (var occurrence in new[]
                 {
                     XLHFOccurrence.AllPages,
                     XLHFOccurrence.OddPages,
                     XLHFOccurrence.EvenPages,
                     XLHFOccurrence.FirstPage
                 })
        {
            target.Left.Clear(occurrence);
            target.Center.Clear(occurrence);
            target.Right.Clear(occurrence);
        }

        var primaryOccurrence = differentOddEvenPages ? XLHFOccurrence.OddPages : XLHFOccurrence.AllPages;
        AddHeaderFooterText(target, oddOrAllPages, primaryOccurrence);

        // ClosedXML's XLHFOccurrence.AllPages is a universal fallback: once written, reading
        // FirstPage/EvenPages (or OddPages) returns the AllPages text too, for any occurrence
        // that has no text of its own yet. AddText() APPENDS to whatever GetText() currently
        // resolves to for the target occurrence rather than replacing it -- so writing AllPages
        // above, then adding FirstPage/EvenPages text below, would otherwise land as
        // "<AllPages text><FirstPage/EvenPages text>" concatenated together. Re-clearing
        // FirstPage/EvenPages here (only needed when the primary occurrence is the fallback-y
        // AllPages -- OddPages does not bleed into them) removes that inherited fallback text so
        // the AddHeaderFooterText calls below set FirstPage/EvenPages independently.
        if (primaryOccurrence == XLHFOccurrence.AllPages)
        {
            foreach (var occurrence in new[] { XLHFOccurrence.FirstPage, XLHFOccurrence.EvenPages })
            {
                target.Left.Clear(occurrence);
                target.Center.Clear(occurrence);
                target.Right.Clear(occurrence);
            }
        }

        // Excel preserves first/even header-footer text even while the "Different first
        // page"/"Different odd and even pages" checkboxes are unchecked: the differentFirst/
        // differentOddEven flags only deactivate rendering, they do not purge the stored text,
        // so re-checking the box later restores it. Write firstPage/evenPages unconditionally
        // (independent of the flags, which are set separately by the caller) so a round-trip
        // save doesn't discard stale-but-still-present text; an explicit clear in the in-memory
        // model (empty string) still results in no text being (re-)added, since
        // AddHeaderFooterText is a no-op for empty values and the occurrence was already
        // cleared above.
        AddHeaderFooterText(target, firstPage, XLHFOccurrence.FirstPage);
        AddHeaderFooterText(target, evenPages, XLHFOccurrence.EvenPages);
    }

    public static string GetHeaderFooterText(IXLHFItem item, params XLHFOccurrence[] occurrences)
    {
        foreach (var occurrence in occurrences)
        {
            var text = item.GetText(occurrence);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return "";
    }

    public static string ToHeaderFooterText(string text) =>
        ReplaceHeaderFooterTokens(text, [
            new("&[Page]", "&P"),
            new("&[Pages]", "&N"),
            new("&[Date]", "&D"),
            new("&[Time]", "&T"),
            new("&[File]", "&F"),
            new("&[Path]", "&Z"),
            new("&[Tab]", "&A"),
            new("&[Picture]", "&G")
        ], StringComparison.OrdinalIgnoreCase);

    public static string FromHeaderFooterText(string text) =>
        ReplaceHeaderFooterTokens(text, [
            new("&P", "&[Page]"),
            new("&N", "&[Pages]"),
            new("&D", "&[Date]"),
            new("&T", "&[Time]"),
            new("&F", "&[File]"),
            new("&Z", "&[Path]"),
            new("&A", "&[Tab]"),
            new("&G", "&[Picture]")
        ], StringComparison.OrdinalIgnoreCase);

    public static XLPrintErrorValues ToPrintErrorValue(WorksheetPrintErrorValue value) =>
        value switch
        {
            WorksheetPrintErrorValue.Blank => XLPrintErrorValues.Blank,
            WorksheetPrintErrorValue.Dash => XLPrintErrorValues.Dash,
            WorksheetPrintErrorValue.NotAvailable => XLPrintErrorValues.NA,
            _ => XLPrintErrorValues.Displayed
        };

    public static WorksheetPrintErrorValue FromPrintErrorValue(XLPrintErrorValues value) =>
        value switch
        {
            XLPrintErrorValues.Blank => WorksheetPrintErrorValue.Blank,
            XLPrintErrorValues.Dash => WorksheetPrintErrorValue.Dash,
            XLPrintErrorValues.NA => WorksheetPrintErrorValue.NotAvailable,
            _ => WorksheetPrintErrorValue.Displayed
        };

    public static XLShowCommentsValues ToPrintComments(WorksheetPrintComments value) =>
        value switch
        {
            WorksheetPrintComments.AtEnd => XLShowCommentsValues.AtEnd,
            WorksheetPrintComments.AsDisplayed => XLShowCommentsValues.AsDisplayed,
            _ => XLShowCommentsValues.None
        };

    public static WorksheetPrintComments FromPrintComments(XLShowCommentsValues value) =>
        value switch
        {
            XLShowCommentsValues.AtEnd => WorksheetPrintComments.AtEnd,
            XLShowCommentsValues.AsDisplayed => WorksheetPrintComments.AsDisplayed,
            _ => WorksheetPrintComments.None
        };

    private static void AddHeaderFooterText(
        IXLHeaderFooter target,
        WorksheetHeaderFooter value,
        XLHFOccurrence occurrence)
    {
        if (!string.IsNullOrEmpty(value.Left))
            target.Left.AddText(ToHeaderFooterText(value.Left), occurrence);
        if (!string.IsNullOrEmpty(value.Center))
            target.Center.AddText(ToHeaderFooterText(value.Center), occurrence);
        if (!string.IsNullOrEmpty(value.Right))
            target.Right.AddText(ToHeaderFooterText(value.Right), occurrence);
    }

    private static string ReplaceHeaderFooterTokens(
        string text,
        IReadOnlyList<HeaderFooterTokenMapping> mappings,
        StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '&')
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            // "&&" is Excel's escape sequence for a literal ampersand in header/footer
            // text; the character following it must not be treated as a code letter
            // (e.g. "R&&D Report" must stay literal, not have "&D" read as the Date code).
            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                result.Append("&&");
                i += 2;
                continue;
            }

            var matched = false;
            foreach (var mapping in mappings)
            {
                if (i + mapping.Source.Length <= text.Length &&
                    string.Compare(text, i, mapping.Source, 0, mapping.Source.Length, comparison) == 0)
                {
                    result.Append(mapping.Target);
                    i += mapping.Source.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private readonly record struct HeaderFooterTokenMapping(string Source, string Target);
}
