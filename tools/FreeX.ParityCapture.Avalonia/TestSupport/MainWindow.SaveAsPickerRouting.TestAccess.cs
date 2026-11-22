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

    private Func<string, ExportSavePickerPlan, Task<AvaloniaPickedStorageFile?>>? _exportSavePickerOverride;

    internal Func<string, ExportSavePickerPlan, Task<AvaloniaPickedStorageFile?>>? ExportSavePickerOverrideForTest
    {
        get => _exportSavePickerOverride;
        set => _exportSavePickerOverride = value;
    }

    /// <summary>
    /// Validation installs a deterministic export save picker instead of the native panel (which never
    /// returns under validation). It "chooses" a scratch .pdf under the temp directory so the flow proceeds
    /// to the Export Options dialog, which the harness then cancels; nothing is exported or kept.
    /// </summary>
    internal void InstallCancellingExportSavePicker()
    {
        if (_exportSavePickerOverride is null)
            _exportSavePickerOverride = PickScratchExportFileAsync;
    }

    private async Task<AvaloniaPickedStorageFile?> PickScratchExportFileAsync(
        string title,
        ExportSavePickerPlan pickerPlan)
    {
        var path = Path.Combine(Path.GetTempPath(), "freex-validation-export-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, []);
        try
        {
            var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            return file is null ? null : new AvaloniaPickedStorageFile(file);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    partial void ResolveExportSavePicker(
        ref Func<string, ExportSavePickerPlan, Task<AvaloniaPickedStorageFile?>>? picker) =>
        picker = _exportSavePickerOverride;

    internal WorkbookSaveAsPickerSelection CreateTransientWorkbookSaveAsSelection(string path) =>
        WorkbookSaveAsPickerSelection.FromLocalPath(path, () => _recentFiles.Remove(path));

    partial void ResolveWorkbookSaveAsPicker(
        ref Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>? picker) =>
        picker = _workbookSaveAsPickerOverride;

}
