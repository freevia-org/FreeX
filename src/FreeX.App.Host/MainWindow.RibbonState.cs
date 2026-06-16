using Free.Shared.Ribbon;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// Platform-neutral source of truth for ribbon command state (checked-ness, enablement, combo
    /// values). The declarative WPF renderer binds every rendered control to this store, so the host
    /// updates ribbon state by writing the store rather than poking hidden WPF stub controls. An
    /// Avalonia renderer will bind to the same store.
    /// </summary>
    private readonly RibbonStateStore _ribbonState = new();

    /// <summary>True when the command's toggle is currently checked in the store.</summary>
    private bool IsRibbonCommandChecked(string commandId) =>
        _ribbonState.GetState(commandId).IsChecked;
}
