using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool RibbonKeyTipsVisibleForTest => _ribbonKeyTipsVisible;

    internal bool HasWindowIconForTest => Icon is not null;

    internal IRibbonCommandRegistry? RibbonCommandRegistryForTest => _ribbonCommandRegistry;

    internal Control? RibbonControlForTest => _ribbonControl;

    internal Thickness CellAddressPaddingForTest => _cellAddressText.Padding;

    internal void ShowBackstageOverlayForTest() => ShowBackstageOverlay();

}
