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
        return sheetName.Length <= 31 ? sheetName : sheetName[..31].Trim();
    }

    private static bool IsInvalidSheetNameCharacter(char ch) =>
        ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']';
}
