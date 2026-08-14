using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

/// <summary>FreeW dialog base that keeps every code-built route on the shared Avalonia chrome.</summary>
public abstract class FreeWDialogWindow : AvaloniaDialogWindow
{
    protected FreeWDialogWindow()
    {
    }

    protected FreeWDialogWindow(AvaloniaCompactDialogChromeStyle style)
        : base(style)
    {
    }
}
