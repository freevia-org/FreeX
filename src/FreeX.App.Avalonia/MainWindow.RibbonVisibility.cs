using Avalonia.Input;

namespace FreeX.App.Avalonia;

// WPF parity (e505a5e15b "Ctrl+F1 ribbon visibility toggle", fdaf36e852 "collapse reflow"):
// Ctrl+F1 hides/shows the ribbon content row and keeps only the tab strip, even while a text box
// has focus. The TabControl MinHeight pin is released while collapsed so the work area reflows.
// macOS: Cmd+F1 and Excel for Mac's Cmd+Option+R also toggle it (Ctrl+F1 is a system shortcut there).
//
// The collapse itself lives in the shared ribbon display state (RibbonDisplayState, applied by the
// Avalonia ribbon renderer), so Ctrl+F1, the tab double-click and the temporary show after a tab
// click all agree; this file only owns the macOS chords and the focus-independent key routing.
public sealed partial class MainWindow
{
    internal bool IsRibbonContentCollapsed => RibbonDisplayState?.IsCollapsed == true;

    internal static bool IsToggleRibbonVisibilityShortcut(Key key, KeyModifiers modifiers) =>
        key == Key.F1 && modifiers is KeyModifiers.Control or KeyModifiers.Meta ||
        key == Key.R && modifiers == (KeyModifiers.Meta | KeyModifiers.Alt);

    internal void ToggleRibbonVisibility() => ToggleRibbonCollapsed();
}
