using Avalonia.Input;

namespace FreeW.App.Avalonia;

internal static class FreeWKeyboardGestures
{
    // Keep Command distinct until this macOS gesture has been resolved; normalizing it to
    // Control first loses the distinction from the Windows Ctrl+Shift+Z gesture.
    internal static bool IsCommandRedo(KeyEventArgs args) =>
        args.Key == Key.Z && args.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift);
}
