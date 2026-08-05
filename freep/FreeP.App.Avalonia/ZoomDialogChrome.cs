using Avalonia.Controls;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

internal static class ZoomDialogChrome
{
    private static readonly AvaloniaCompactDialogChromeStyle Style = new(FontFamily.Default);

    internal static void Apply(Window window) =>
        AvaloniaCompactDialogChrome.ApplyWindow(window, Style);

    internal static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
