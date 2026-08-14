using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

/// <summary>Keeps FreeP's code-built dialogs on the shared Avalonia dialog chrome.</summary>
internal abstract class FreePDialogWindow : AvaloniaDialogWindow
{
    protected FreePDialogWindow()
    {
    }

    protected FreePDialogWindow(AvaloniaCompactDialogChromeStyle style)
        : base(style)
    {
    }
}
