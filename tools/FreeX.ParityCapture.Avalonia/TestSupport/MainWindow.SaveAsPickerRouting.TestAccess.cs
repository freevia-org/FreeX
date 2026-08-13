using Avalonia.Platform.Storage;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>?
        _workbookSaveAsPickerOverride;

    internal Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>?
        WorkbookSaveAsPickerOverrideForTest
    {
        get => _workbookSaveAsPickerOverride;
        set => _workbookSaveAsPickerOverride = value;
    }

    internal WorkbookSaveAsPickerSelection CreateTransientWorkbookSaveAsSelection(string path) =>
        WorkbookSaveAsPickerSelection.FromLocalPath(path, () => _recentFiles.Remove(path));

    partial void ResolveWorkbookSaveAsPicker(
        ref Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>? picker) =>
        picker = _workbookSaveAsPickerOverride;

}
