using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookOpenNormalizer
{
    public static void ApplyTextWorkbookSheetName(Workbook workbook, string extension, string displayName)
    {
        if (workbook.Sheets.Count != 1 || !IsTextWorkbookExtension(extension))
            return;

        workbook.Sheets[0].Name = CreateExcelCompatibleSheetName(displayName);
    }

    public static bool IsTextWorkbookExtension(string extension) =>
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tab", StringComparison.OrdinalIgnoreCase);

    public static string CreateExcelCompatibleSheetName(string displayName)
    {
        var chars = displayName
            .Trim()
            .Select(ch => IsInvalidSheetNameCharacter(ch) ? '_' : ch)
            .ToArray();
        var sheetName = new string(chars).Trim();
        if (sheetName.Length == 0)
            sheetName = "Sheet1";
        // r194: text-element aware. A raw [..31] can leave half a surrogate pair, and a lone
        // surrogate in a sheet name aborts every later save to .xlsx. See SurrogateSafeTruncation.
        return sheetName.Length <= 31
            ? sheetName
            : Free.Shared.IO.SurrogateSafeTruncation.LimitToTextElements(sheetName, 31).Trim();
    }

    private static bool IsInvalidSheetNameCharacter(char ch) =>
        ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']';
}
