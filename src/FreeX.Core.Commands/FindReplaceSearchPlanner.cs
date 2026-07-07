using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class FindReplaceSearchPlanner
{
    public readonly record struct SearchText(
        CellAddress Address,
        string Text,
        FindResultTarget Target = FindResultTarget.Cell,
        int? ReplyIndex = null);

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

    public static IEnumerable<SearchText> EnumerateSearchTexts(Sheet sheet, FindLookIn lookIn,
        Workbook? workbook = null,
        bool skipNumberValues = false)
    {
        if (lookIn == FindLookIn.Notes)
        {
            foreach (var (address, text) in sheet.Comments)
                yield return new SearchText(address, text, FindResultTarget.Note);
            yield break;
        }

        if (lookIn == FindLookIn.Comments)
        {
            foreach (var (address, comment) in sheet.ThreadedComments)
            {
                yield return new SearchText(address, comment.Text, FindResultTarget.ThreadedComment);

                for (var replyIndex = 0; replyIndex < comment.Replies.Count; replyIndex++)
                    yield return new SearchText(
                        address,
                        comment.Replies[replyIndex].Text,
                        FindResultTarget.ThreadedCommentReply,
                        replyIndex);
            }
            yield break;
        }

        foreach (var (addr, cell) in sheet.EnumerateCells())
        {
            // Excel's "Look in: Values" Find does not return matches in hidden or filtered-out
            // rows ("Values cannot find hidden data"), so Replace All must not rewrite cells a
            // user could never have navigated to via Find Next either. Formulas/Notes/Comments
            // modes are unaffected -- only the Values-mode cell-text branch below is scoped here.
            if (lookIn == FindLookIn.Values && sheet.IsRowEffectivelyHidden(addr.Row))
                continue;

            // Performance: skip number cells when the search pattern can never match any
            // invariant numeric rendering (no digits/sign/decimal/exponent or wildcards).
            // This optimisation is only safe when NOT using number-format-aware rendering
            // (formatted numbers can produce arbitrary strings like "50%" or "$1,000.00").
            if (skipNumberValues && cell.Value is NumberValue)
                continue;

            string? text;
            if (lookIn == FindLookIn.Formulas && cell.HasFormula)
            {
                text = cell.FormulaText;
            }
            else if (lookIn == FindLookIn.Values && workbook is not null)
            {
                text = GetDisplayTextFormatted(cell, workbook);
            }
            else
            {
                text = GetDisplayText(cell.Value);
            }

            if (text is not null)
                yield return new SearchText(addr, text);
        }
    }

    public static void SortResults(List<FindResult> results, FindSearchOrder searchOrder)
    {
        results.Sort((a, b) =>
        {
            int addressComparison;
            if (searchOrder == FindSearchOrder.ByColumns)
            {
                var colCmp = a.Address.Col.CompareTo(b.Address.Col);
                addressComparison = colCmp != 0 ? colCmp : a.Address.Row.CompareTo(b.Address.Row);
            }
            else
            {
                var rowCmp = a.Address.Row.CompareTo(b.Address.Row);
                addressComparison = rowCmp != 0 ? rowCmp : a.Address.Col.CompareTo(b.Address.Col);
            }

            if (addressComparison != 0)
                return addressComparison;

            var targetComparison = GetTargetSortIndex(a).CompareTo(GetTargetSortIndex(b));
            return targetComparison != 0
                ? targetComparison
                : Nullable.Compare(a.ReplyIndex, b.ReplyIndex);
        });
    }

    private static int GetTargetSortIndex(FindResult result) =>
        result.Target switch
        {
            FindResultTarget.ThreadedComment => 0,
            FindResultTarget.ThreadedCommentReply => 1 + Math.Max(0, result.ReplyIndex ?? 0),
            _ => 0
        };

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

    /// <summary>
    /// Invariant display text used for formula-mode search and as a fallback when no workbook
    /// is available. Numbers use the raw double representation; dates use yyyy-MM-dd.
    /// </summary>
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

    /// <summary>
    /// Number-format-aware display text for Values-mode search.  Numbers and dates are rendered
    /// through <see cref="NumberFormatter"/> using the cell's applied number format so that
    /// the searchable text matches what the user sees in the grid (e.g. "50%", "$1,000.00").
    /// Text, boolean, and error values are returned as-is; blanks return null.
    /// </summary>
    private static string? GetDisplayTextFormatted(Cell cell, Workbook workbook)
    {
        var value = cell.Value;

        // Fast-path for non-numeric types that are unaffected by number format.
        if (value is BlankValue)  return null;
        if (value is TextValue t) return t.Value;
        if (value is BoolValue b) return b.Value ? "TRUE" : "FALSE";
        if (value is ErrorValue e) return e.Code;

        // For numbers and dates, apply the cell's number format so the displayed text
        // matches the grid rendering (same as NumberFormatter.Format used in the grid).
        var style = workbook.GetStyle(cell.StyleId);
        return NumberFormatter.Format(value, style.NumberFormat, workbook.Uses1904DateSystem);
    }

}
