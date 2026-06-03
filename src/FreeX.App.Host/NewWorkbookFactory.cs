using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class NewWorkbookFactory
{
    public static Workbook Create(FreeXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var workbook = Create(
            options.DefaultSheetCount,
            options.DefaultFontName,
            options.DefaultFontSize);
        workbook.FileSharing = new WorkbookFileSharingModel
        {
            UserName = FreeXOptions.NormalizeUserName(options.UserName)
        };
        return workbook;
    }

    public static Workbook Create(int defaultSheetCount)
    {
        return Create(
            defaultSheetCount,
            FreeXOptions.DefaultFontNameFallback,
            FreeXOptions.DefaultFontSizeFallback);
    }

    private static Workbook Create(
        int defaultSheetCount,
        string? defaultFontName,
        int defaultFontSize)
    {
        var defaultStyle = CellStyle.Default.Clone();
        defaultStyle.FontName = FreeXOptions.NormalizeDefaultFontName(defaultFontName);
        defaultStyle.FontSize = FreeXOptions.NormalizeDefaultFontSize(defaultFontSize);

        var workbook = new Workbook("Book1", defaultStyle);
        var sheetCount = FreeXOptions.NormalizeDefaultSheetCount(defaultSheetCount);
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
            workbook.AddSheet($"Sheet{sheetIndex}");

        return workbook;
    }
}
