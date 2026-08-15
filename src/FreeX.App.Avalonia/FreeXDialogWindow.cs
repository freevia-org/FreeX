using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

/// <summary>Keeps FreeX code-built dialogs on the shared Avalonia dialog chrome.</summary>
internal sealed class FreeXDialogWindow : AvaloniaDialogWindow
{
    public FreeXDialogWindow()
    {
    }

    public FreeXDialogWindow(AvaloniaCompactDialogChromeStyle style)
        : base(style)
    {
    }
}
