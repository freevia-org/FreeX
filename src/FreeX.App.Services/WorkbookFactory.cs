using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookFactory
{
    public const string DefaultWorkbookNamePrefix = "Book";
    public const string DefaultWorkbookName = DefaultWorkbookNamePrefix + "1";
    public const string DefaultFontNameFallback = "Calibri";
    public const int DefaultFontSizeFallback = 11;
    public const int MaxDefaultFontSize = 409;
    public const int MinDefaultSheetCount = 1;
    public const int MaxDefaultSheetCount = 255;
    public const int DefaultSheetCountFallback = 1;

    public static Workbook Create(WorkbookCreationOptions? options = null)
    {
        options ??= new WorkbookCreationOptions();

        var defaultStyle = CellStyle.Default.Clone();
        defaultStyle.FontName = NormalizeDefaultFontName(options.DefaultFontName);
        defaultStyle.FontSize = NormalizeDefaultFontSize(options.DefaultFontSize);
        // When no custom default font is specified the default style follows the workbook theme's
        // minor (body) font, so switching Theme Fonts re-renders default cells automatically.
        // An explicit DefaultFontName pins the scheme to None so the user's choice is respected.
        defaultStyle.FontScheme = string.IsNullOrWhiteSpace(options.DefaultFontName)
            ? CellFontScheme.Minor
            : CellFontScheme.None;

        var workbook = new Workbook(NormalizeWorkbookName(options.Name), defaultStyle);
        var sheetCount = NormalizeDefaultSheetCount(options.DefaultSheetCount);
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
            workbook.AddSheet($"Sheet{sheetIndex}");

        if (options.UserName is not null)
        {
            workbook.FileSharing = new WorkbookFileSharingModel
            {
                UserName = NormalizeUserName(options.UserName),
            };
        }

        return workbook;
    }

    public static Workbook CreateFromAppOptions(AppOptions options, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Create(new WorkbookCreationOptions(
            Name: string.IsNullOrWhiteSpace(name) ? DefaultWorkbookName : name,
            DefaultSheetCount: options.DefaultSheetCount,
            DefaultFontName: options.DefaultFontName,
            DefaultFontSize: options.DefaultFontSize,
            UserName: options.UserName));
    }

    public static string NormalizeDefaultFontName(string? fontName)
    {
        var normalized = fontName?.Trim();
        return string.IsNullOrEmpty(normalized) ? DefaultFontNameFallback : normalized;
    }

    public static int NormalizeDefaultFontSize(int fontSize)
    {
        if (fontSize <= 0)
            return DefaultFontSizeFallback;

        return Math.Min(fontSize, MaxDefaultFontSize);
    }

    public static int NormalizeDefaultSheetCount(int sheetCount) =>
        Math.Clamp(sheetCount, MinDefaultSheetCount, MaxDefaultSheetCount);

    public static string NormalizeUserName(string? userName)
    {
        var normalized = userName?.Trim();
        return string.IsNullOrEmpty(normalized) ? Environment.UserName : normalized;
    }

    private static string NormalizeWorkbookName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrEmpty(normalized) ? DefaultWorkbookName : normalized;
    }
}
