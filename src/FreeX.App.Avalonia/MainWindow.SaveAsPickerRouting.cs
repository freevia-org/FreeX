using Avalonia.Platform.Storage;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal sealed class WorkbookSaveAsPickerSelection : IDisposable
    {
        private readonly IDisposable? _owner;
        private readonly Action? _onDispose;
        private int _isDisposed;

        private WorkbookSaveAsPickerSelection(
            string? localPath,
            IStorageFile? storageFile,
            IDisposable? owner,
            Action? onDispose = null)
        {
            LocalPath = localPath;
            StorageFile = storageFile;
            _owner = owner;
            _onDispose = onDispose;
        }

        internal string? LocalPath { get; }
        internal IStorageFile? StorageFile { get; }

        internal static WorkbookSaveAsPickerSelection FromLocalPath(
            string path,
            Action? onDispose = null) =>
            new(path, storageFile: null, owner: null, onDispose: onDispose);

        internal static WorkbookSaveAsPickerSelection FromPickedStorageFile(
            AvaloniaPickedStorageFile pickedFile) =>
            new(pickedFile.LocalPath, pickedFile.StorageFile, pickedFile);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;

            _owner?.Dispose();
            _onDispose?.Invoke();
        }
    }

    private Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>?
        _workbookSaveAsPickerOverride;

    internal Func<WorkbookSaveAsCommandPickerPlan, Task<WorkbookSaveAsPickerSelection?>>?
        WorkbookSaveAsPickerOverrideForTest
    {
        get => _workbookSaveAsPickerOverride;
        set => _workbookSaveAsPickerOverride = value;
    }

    private static bool ResolveWorkbookSaveAsPickerAvailability(
        bool nativePickerAvailable,
        bool deterministicPickerAvailable) =>
        nativePickerAvailable || deterministicPickerAvailable;

    internal WorkbookSaveAsPickerSelection CreateTransientWorkbookSaveAsSelection(string path) =>
        WorkbookSaveAsPickerSelection.FromLocalPath(path, () => _recentFiles.Remove(path));
}
