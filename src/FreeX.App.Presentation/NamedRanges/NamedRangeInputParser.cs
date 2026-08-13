using FreeX.Core.Model;

namespace FreeX.App.Presentation.NamedRanges;

public static class NamedRangeInputParser
{
    public static bool TryParseRange(Workbook workbook, string input, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input) || workbook.SheetCount == 0)
            return false;

        return TryParseRange(workbook, workbook.GetSheetAt(0).Id, input, out range);
    }

    public static bool TryParseRange(
        Workbook workbook,
        SheetId defaultSheetId,
        string input,
        out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(input) || workbook.SheetCount == 0)
            return false;

        var normalized = input.Trim();
        if (normalized.StartsWith('='))
            normalized = normalized[1..].Trim();

        if (workbook.TryGetNamedRange(normalized, defaultSheetId, out range))
            return true;

        return WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            normalized,
            sheetName => workbook.GetSheet(sheetName)?.Id,
            out range);
    }
}
