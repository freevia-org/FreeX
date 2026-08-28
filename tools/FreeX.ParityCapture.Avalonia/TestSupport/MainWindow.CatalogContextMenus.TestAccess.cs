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

    /// <summary>
    /// Test-only entry into the real production QAT context-menu customization handler
    /// (<c>ApplyAvaloniaQuickAccessCustomization</c>), the same code path
    /// <c>AttachAvaloniaQuickAccessCustomization</c> wires every QAT button's "Add"/"Remove"
    /// context-menu item to. Exercises the identical shared-AppOptions mutation + broadcast
    /// production code, not a re-implementation.
    /// </summary>
    internal void ApplyAvaloniaQuickAccessCustomizationForTest(
        string commandId,
        QuickAccessToolbarCustomizationAction action) =>
        ApplyAvaloniaQuickAccessCustomization(new QuickAccessToolbarMenuCommand(
            ResourceKey: "",
            Action: action == QuickAccessToolbarCustomizationAction.Remove
                ? QuickAccessToolbarMenuAction.Remove
                : QuickAccessToolbarMenuAction.Add,
            CommandId: commandId));

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
