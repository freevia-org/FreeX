using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal StackPanel AvaloniaQuickAccessToolbarForTest => _avaloniaQuickAccessToolbar;

    internal Panel? AvaloniaQuickAccessTitleBarHostForTest => _avaloniaQuickAccessTitleBarHost;

    internal Border? AvaloniaQuickAccessBelowRibbonHostForTest => _avaloniaQuickAccessBelowRibbonHost;

    internal void SetAvaloniaQuickAccessPlacementForTest(bool belowRibbon)
    {
        if (_avaloniaQuickAccessOptions is null)
            return;

        _avaloniaQuickAccessOptions.QuickAccessToolbarBelowRibbon = belowRibbon;
        RebuildAvaloniaQuickAccessToolbar();
    }

    internal string? AvaloniaQuickAccessKeyTipForTest(string commandId) =>
        _avaloniaQuickAccessKeyTipButtons
            .FirstOrDefault(entry => string.Equals(entry.Value.Tag as string, commandId, StringComparison.OrdinalIgnoreCase))
            .Key;

    internal bool AvaloniaQuickAccessKeyTipVisibleForTest(string commandId) =>
        _avaloniaQuickAccessKeyTipButtons
            .FirstOrDefault(entry => string.Equals(entry.Value.Tag as string, commandId, StringComparison.OrdinalIgnoreCase))
            .Value is { } button &&
        _avaloniaQuickAccessKeyTipBadges.TryGetValue(button, out var badge) &&
        badge.IsVisible;

}
