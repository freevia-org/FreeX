using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

public static class ChartInputParser
{
    public static bool TryParseDataRange(string input, SheetId sheetId, out GridRange range)
    {
        if (!LooksLikeRange(input))
        {
            range = default;
            return false;
        }

        return WorkbookRangeTextCodec.TryParseOnCurrentSheet(sheetId, input, out range);
    }

    public static bool TryParseDataRange(
        string input,
        SheetId sheetId,
        Func<string, SheetId?> resolveSheetId,
        out GridRange range)
    {
        if (!LooksLikeRange(input))
        {
            range = default;
            return false;
        }

        return WorkbookRangeTextCodec.TryParse(sheetId, input, resolveSheetId, out range);
    }

    private static bool LooksLikeRange(string input) =>
        input.AsSpan().Trim().Contains(':');
}
