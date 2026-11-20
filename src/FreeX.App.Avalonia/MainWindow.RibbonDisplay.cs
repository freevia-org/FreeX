using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia;

// Excel's collapsed ribbon ("Show tabs only"). The shared renderer handles double-clicking a tab,
// the temporary show on a tab click and hiding it again; the shell adds Ctrl+F1.
public partial class MainWindow
{
    internal RibbonDisplayState? RibbonDisplayState =>
        _ribbonControl is null ? null : AvaloniaRibbonRenderer.GetDisplayState(_ribbonControl);

    private void ToggleRibbonCollapsed() => RibbonDisplayState?.ToggleCollapsed();
}
