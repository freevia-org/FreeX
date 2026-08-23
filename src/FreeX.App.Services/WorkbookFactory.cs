using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class WorkbookFactory
{
    public const string DefaultWorkbookNamePrefix = WorkbookCreationDefaults.WorkbookNamePrefix;
    public const string DefaultWorkbookName = WorkbookCreationDefaults.WorkbookName;
    public const string DefaultFontNameFallback = WorkbookCreationDefaults.FontName;
    public const int DefaultFontSizeFallback = WorkbookCreationDefaults.FontSize;
    public const int MaxDefaultFontSize = WorkbookCreationDefaults.MaxFontSize;
    public const int MinDefaultSheetCount = WorkbookCreationDefaults.MinSheetCount;
    public const int MaxDefaultSheetCount = WorkbookCreationDefaults.MaxSheetCount;
    public const int DefaultSheetCountFallback = WorkbookCreationDefaults.SheetCount;

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

    public static string NormalizeDefaultFontName(string? fontName) =>
        WorkbookCreationDefaults.NormalizeFontName(fontName);

    public static int NormalizeDefaultFontSize(int fontSize) =>
        WorkbookCreationDefaults.NormalizeFontSize(fontSize);

    public static int NormalizeDefaultSheetCount(int sheetCount) =>
        WorkbookCreationDefaults.NormalizeSheetCount(sheetCount);

    public static string NormalizeUserName(string? userName) =>
        WorkbookCreationDefaults.NormalizeUserName(userName);

    private static string NormalizeWorkbookName(string? name) =>
        WorkbookCreationDefaults.NormalizeWorkbookName(name);
}
