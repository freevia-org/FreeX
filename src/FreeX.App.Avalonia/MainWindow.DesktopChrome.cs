using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Control? _ribbonControl;
    private IRibbonCommandRegistry? _ribbonCommandRegistry;
    private bool _ribbonKeyTipsVisible;

    private void ApplyWindowIcon() =>
        AvaloniaWindowIconLoader.TryApply(this, "FreeX.ico");

    private bool TryHandleRibbonKeyTips(KeyEventArgs args)
    {
        // Keep the generic Alt handler from re-opening the Data ribbon while the WPF-equivalent
        // worksheet-editing or Backstage exclusion is active. The legacy sequence dispatcher has
        // already reset any partial input; returning true here stops the fallback route without
        // marking the key handled.
        if (IsDataRibbonKeyTipAttempt(args) && !CanHandleRibbonKeyTipInput(args))
        {
            ResetRibbonKeyTipSequence();
            args.Handled = false;
            return true;
        }

        var transition = AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
            args.Key,
            args.KeyModifiers,
            _ribbonKeyTipsVisible,
            acceptDirectAltToken: true);
        if (transition.ModeVisible is { } modeVisible)
            SetRibbonKeyTipsVisible(modeVisible);
        if (!transition.ShouldRouteToken)
        {
            if (transition.Handled)
                args.Handled = true;
            return transition.Handled;
        }

        if (_ribbonControl is null)
            return false;

        var activated = AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, transition.Token!);
        SetRibbonKeyTipsVisible(false);
        args.Handled = activated;
        return activated;
    }

    private void SetRibbonKeyTipsVisible(bool visible)
    {
        _ribbonKeyTipsVisible = visible;
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(_ribbonControl, visible);
        RefreshAvaloniaQuickAccessKeyTipBadges();
    }

}
