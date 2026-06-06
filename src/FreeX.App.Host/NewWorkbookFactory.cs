using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class NewWorkbookFactory
{
    public static Workbook Create(FreeXOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return WorkbookFactory.Create(new WorkbookCreationOptions(
            DefaultSheetCount: options.DefaultSheetCount,
            DefaultFontName: options.DefaultFontName,
            DefaultFontSize: options.DefaultFontSize,
            UserName: options.UserName));
    }

    public static Workbook Create(int defaultSheetCount)
    {
        return WorkbookFactory.Create(new WorkbookCreationOptions(DefaultSheetCount: defaultSheetCount));
    }
}
