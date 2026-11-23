using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal static MainWindow? ActivateRegisteredWorkbookWindow()
    {
        var visible = WindowRegistry.VisibleWindows;
        var target = visible.FirstOrDefault(window => window.IsActive)
            ?? visible.FirstOrDefault(window => window.WindowState != WindowState.Minimized)
            ?? visible.FirstOrDefault();
        if (target is not null)
        {
            target.ActivateWorkbookWindow();
            return target;
        }

        target = WindowRegistry.HiddenWindows.FirstOrDefault();
        if (target is not null)
            WindowRegistry.Unhide(target);
        return target;
    }

    internal IReadOnlyList<IStorageItem> ResolveNativeActivatedFiles(IReadOnlyList<IStorageItem> files) =>
        files.Where(file => TryResolveOpenableLocalWorkbookPath([file], out _, out _, out _)).ToArray();
}
