using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Projects the active Avalonia workbook session into the portable autosave service.
/// </summary>
public sealed partial class MainWindow : IAutosaveWorkbookSource
{
    Workbook IAutosaveWorkbookSource.Workbook => _session.Workbook;
    string? IAutosaveWorkbookSource.CurrentFilePath => _session.CurrentFilePath;
    string IAutosaveWorkbookSource.DisplayName => _session.DisplayName;
    bool IAutosaveWorkbookSource.IsWorkbookDirty => _session.IsDirty;
    int IAutosaveWorkbookSource.WorkbookDirtyGeneration => _session.DirtyGeneration;
    string IAutosaveWorkbookSource.DocumentId => _session.Workbook.Id.Value.ToString();
}
