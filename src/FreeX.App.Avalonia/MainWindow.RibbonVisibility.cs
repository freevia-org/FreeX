using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

// WPF parity (e505a5e15b "Ctrl+F1 ribbon visibility toggle", fdaf36e852 "collapse reflow"):
// Ctrl+F1 hides/shows the ribbon content row and keeps only the tab strip, even while a text box
// has focus. The TabControl MinHeight pin is released while collapsed so the work area reflows.
// macOS: Cmd+F1 and Excel for Mac's Cmd+Option+R also toggle it (Ctrl+F1 is a system shortcut there).
public sealed partial class MainWindow
{
    private bool _ribbonContentCollapsed;
    private double _ribbonTabControlExpandedMinHeight;

    internal bool IsRibbonContentCollapsed => _ribbonContentCollapsed;

    internal static bool IsToggleRibbonVisibilityShortcut(Key key, KeyModifiers modifiers) =>
        key == Key.F1 && modifiers is KeyModifiers.Control or KeyModifiers.Meta ||
        key == Key.R && modifiers == (KeyModifiers.Meta | KeyModifiers.Alt);

    private TabControl? FindRibbonTabControl() => _ribbonControl switch
    {
        TabControl tabControl => tabControl,
        { } root => root.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(),
        _ => null,
    };

    internal void ToggleRibbonVisibility()
    {
        if (FindRibbonTabControl() is not { } tabControl)
            return;

        tabControl.ApplyTemplate();
        var contentHost = tabControl
            .GetVisualDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(static presenter => presenter.Name == "PART_SelectedContentHost");
        if (contentHost is null)
            return;

        _ribbonContentCollapsed = !_ribbonContentCollapsed;
        contentHost.IsVisible = !_ribbonContentCollapsed;
        if (_ribbonContentCollapsed)
        {
            _ribbonTabControlExpandedMinHeight = tabControl.MinHeight;
            tabControl.MinHeight = 0;
        }
        else
        {
            tabControl.MinHeight = _ribbonTabControlExpandedMinHeight;
        }
    }
}
