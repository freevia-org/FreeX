using Avalonia.Controls;

namespace Free.Shared.Shell.Avalonia;

/// <summary>Shared base for compact app-owned Avalonia dialogs.</summary>
public abstract class AvaloniaDialogWindow : Window
{
    protected AvaloniaDialogWindow()
        : this(style: null)
    {
    }

    protected AvaloniaDialogWindow(AvaloniaCompactDialogChromeStyle? style)
    {
        AvaloniaCompactDialogChrome.ApplyWindow(this, style);
    }
}
