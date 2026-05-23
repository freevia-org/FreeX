using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPageSetupMapper
{
    public static void LoadPrintArea(IXLWorksheet xlSheet, Sheet sheet)
    {
        var xlRange = xlSheet.PageSetup.PrintAreas.FirstOrDefault();
        if (xlRange is null)
            return;

        var start = new CellAddress(
            sheet.Id,
            (uint)xlRange.RangeAddress.FirstAddress.RowNumber,
            (uint)xlRange.RangeAddress.FirstAddress.ColumnNumber);
        var end = new CellAddress(
            sheet.Id,
            (uint)xlRange.RangeAddress.LastAddress.RowNumber,
            (uint)xlRange.RangeAddress.LastAddress.ColumnNumber);
        sheet.PrintArea = new GridRange(start, end);
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
        if (differentFirstPage)
            AddHeaderFooterText(target, firstPage, XLHFOccurrence.FirstPage);
        if (differentOddEvenPages)
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
        foreach (var mapping in mappings)
            text = text.Replace(mapping.Source, mapping.Target, comparison);
        return text;
    }

    private readonly record struct HeaderFooterTokenMapping(string Source, string Target);
}
