using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class FindReplaceSearchPlanner
{
    public static IEnumerable<Sheet> SheetsForScope(Workbook workbook, FindOptions options)
    {
        if (options.Within == FindWithin.Sheet && options.CurrentSheetId is { } sheetId)
        {
            var sheet = workbook.GetSheet(sheetId);
            if (sheet is not null)
                yield return sheet;
            yield break;
        }

        foreach (var sheet in workbook.Sheets)
            yield return sheet;
    }

    public static IEnumerable<(CellAddress Address, string Text)> EnumerateSearchTexts(Sheet sheet, FindLookIn lookIn)
    {
        if (lookIn == FindLookIn.Notes)
        {
            foreach (var (address, text) in sheet.Comments)
                yield return (address, text);
            yield break;
        }

        if (lookIn == FindLookIn.Comments)
        {
            foreach (var (address, comment) in sheet.ThreadedComments)
                yield return (address, comment.Text);
            yield break;
        }

        foreach (var (addr, cell) in sheet.EnumerateCells())
        {
            string? text = lookIn == FindLookIn.Formulas && cell.HasFormula
                ? cell.FormulaText
                : GetDisplayText(cell.Value);

            if (text is not null)
                yield return (addr, text);
        }
    }

    public static void SortResults(List<FindResult> results, FindSearchOrder searchOrder)
    {
        results.Sort((a, b) =>
        {
            if (searchOrder == FindSearchOrder.ByColumns)
            {
                var colCmp = a.Address.Col.CompareTo(b.Address.Col);
                return colCmp != 0 ? colCmp : a.Address.Row.CompareTo(b.Address.Row);
            }

            var rowCmp = a.Address.Row.CompareTo(b.Address.Row);
            return rowCmp != 0 ? rowCmp : a.Address.Col.CompareTo(b.Address.Col);
        });
    }

    public static bool MatchesRequiredFormat(Workbook workbook, Sheet sheet, CellAddress address, StyleDiff? requiredFormat)
    {
        if (requiredFormat is null)
            return true;

        var styleId = sheet.GetCell(address)?.StyleId
            ?? sheet.GetStyleOnly(address.Row, address.Col)
            ?? StyleId.Default;
        var style = workbook.GetStyle(styleId);

        return Matches(requiredFormat.Bold, style.Bold)
            && Matches(requiredFormat.Italic, style.Italic)
            && Matches(requiredFormat.Underline, style.Underline)
            && Matches(requiredFormat.Strikethrough, style.Strikethrough)
            && Matches(requiredFormat.Superscript, style.Superscript)
            && Matches(requiredFormat.Subscript, style.Subscript)
            && Matches(requiredFormat.FontName, style.FontName)
            && Matches(requiredFormat.FontSize, style.FontSize)
            && Matches(requiredFormat.FontColor, style.FontColor)
            && Matches(requiredFormat.FillColor, style.FillColor)
            && Matches(requiredFormat.FillPatternStyle, style.FillPatternStyle)
            && Matches(requiredFormat.FillPatternColor, style.FillPatternColor)
            && Matches(requiredFormat.HAlign, style.HorizontalAlignment)
            && Matches(requiredFormat.VAlign, style.VerticalAlignment)
            && Matches(requiredFormat.WrapText, style.WrapText)
            && Matches(requiredFormat.ShrinkToFit, style.ShrinkToFit)
            && Matches(requiredFormat.NumberFormat, style.NumberFormat)
            && Matches(requiredFormat.DoubleUnderline, style.DoubleUnderline)
            && Matches(requiredFormat.IndentLevel, style.IndentLevel)
            && Matches(requiredFormat.TextRotation, style.TextRotation)
            && Matches(requiredFormat.BorderTop, style.BorderTop)
            && Matches(requiredFormat.BorderRight, style.BorderRight)
            && Matches(requiredFormat.BorderBottom, style.BorderBottom)
            && Matches(requiredFormat.BorderLeft, style.BorderLeft)
            && Matches(requiredFormat.Locked, style.Locked)
            && Matches(requiredFormat.Hidden, style.Hidden);
    }

    private static bool Matches<T>(T? expected, T actual)
        where T : struct
        => expected is null || EqualityComparer<T>.Default.Equals(expected.Value, actual);

    private static bool Matches(string? expected, string actual) =>
        expected is null || string.Equals(expected, actual, StringComparison.Ordinal);

    private static bool Matches(CellColor? expected, CellColor? actual) =>
        expected is null || expected.Equals(actual);

    private static string? GetDisplayText(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextValue t => t.Value,
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        DateTimeValue dt => dt.ToDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ErrorValue err => err.Code,
        _ => null
    };
}
