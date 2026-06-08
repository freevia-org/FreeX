namespace FreeX.App.Services;

public sealed record WorkbookCreationOptions(
    string Name = WorkbookFactory.DefaultWorkbookName,
    int DefaultSheetCount = WorkbookFactory.DefaultSheetCountFallback,
    string? DefaultFontName = null,
    int DefaultFontSize = WorkbookFactory.DefaultFontSizeFallback,
    string? UserName = null);
