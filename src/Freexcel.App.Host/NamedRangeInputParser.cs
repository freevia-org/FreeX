using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class NamedRangeInputParser
{
    public static bool TryParseRange(Workbook workbook, string input, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input) || workbook.SheetCount == 0)
            return false;

        var defaultSheet = workbook.GetSheetAt(0);
        return WorkbookRangeTextCodec.TryParse(
            defaultSheet.Id,
            input,
            sheetName => workbook.GetSheet(sheetName)?.Id,
            out range);
    }
}
