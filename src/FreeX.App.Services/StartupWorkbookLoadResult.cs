using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record StartupWorkbookLoadResult(
    Workbook Workbook,
    string DisplayName,
    string Status,
    bool IsFallback,
    string? SourcePath = null);
