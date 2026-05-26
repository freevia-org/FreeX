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
        var normalized = NormalizeExcelRefersToText(input);
        return WorkbookRangeTextCodec.TryParse(
            defaultSheet.Id,
            normalized,
            sheetName => workbook.GetSheet(sheetName)?.Id,
            out range);
    }

    private static string NormalizeExcelRefersToText(string input)
    {
        var normalized = input.Trim();
        if (normalized.StartsWith('='))
            normalized = normalized[1..].Trim();

        var bangIndex = normalized.LastIndexOf('!');
        if (bangIndex < 0)
            return normalized.Replace("$", "", StringComparison.Ordinal);

        return string.Concat(
            normalized.AsSpan(0, bangIndex + 1),
            normalized[(bangIndex + 1)..].Replace("$", "", StringComparison.Ordinal));
    }
}
