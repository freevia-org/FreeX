using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class NewWorkbookFactory
{
    public static Workbook Create(AppOptions options) => Create(options, name: null);

    public static Workbook Create(AppOptions options, string? name)
    {
        ArgumentNullException.ThrowIfNull(options);

        return WorkbookFactory.Create(new WorkbookCreationOptions(
            Name: string.IsNullOrWhiteSpace(name) ? WorkbookFactory.DefaultWorkbookName : name,
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
