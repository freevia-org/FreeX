namespace FreeX.App.Services;

/// <summary>Owns the normalized defaults shared by persisted options and workbook creation.</summary>
public static class WorkbookCreationDefaults
{
    public const string WorkbookNamePrefix = "Book";
    public const string WorkbookName = WorkbookNamePrefix + "1";
    public const string FontName = "Calibri";
    public const int FontSize = 11;
    public const int MaxFontSize = 409;
    public const int MinSheetCount = 1;
    public const int MaxSheetCount = 255;
    public const int SheetCount = 1;

    public static string NormalizeFontName(string? fontName)
    {
        var normalized = fontName?.Trim();
        return string.IsNullOrEmpty(normalized) ? FontName : normalized;
    }

    public static int NormalizeFontSize(int fontSize)
    {
        if (fontSize <= 0)
            return FontSize;

        return Math.Min(fontSize, MaxFontSize);
    }

    public static int NormalizeSheetCount(int sheetCount) =>
        Math.Clamp(sheetCount, MinSheetCount, MaxSheetCount);

    public static string NormalizeUserName(string? userName)
    {
        var normalized = userName?.Trim();
        return string.IsNullOrEmpty(normalized) ? Environment.UserName : normalized;
    }

    public static string NormalizeWorkbookName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrEmpty(normalized) ? WorkbookName : normalized;
    }
}
