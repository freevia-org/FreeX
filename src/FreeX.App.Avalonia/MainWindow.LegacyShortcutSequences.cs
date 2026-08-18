using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.KeyTips;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly FreeXRibbonKeyTipInputSession _ribbonKeyTipSession = new();

    internal static IReadOnlySet<string> InteractiveValidationLegacyDataFilterInteractionIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "shortcut.data.filter-toggle-reapply:0",
            "shortcut.data.filter-toggle-reapply:1",
            "shortcut.data.filter-toggle-reapply:2",
        };

    private bool TryHandleRibbonKeyTipInput(KeyEventArgs args)
    {
        if (TryHandleQuickAccessKeyTipSequence(args))
            return true;

        var sequenceActive = _ribbonKeyTipSession.IsActive &&
            _ribbonKeyTipSession.Scope != FreeXRibbonKeyTipInputScope.QuickAccess;
        var directAltToken = args.KeyModifiers == KeyModifiers.Alt
            ? AvaloniaKeyTipTokenFormatter.Format(args.Key)
            : null;

        if (_ribbonKeyTipSession.LegacySequence != FreeXRibbonLegacyKeyTipSequence.None &&
            !CanHandleRibbonKeyTipInput(args))
        {
            ResetRibbonKeyTipSequence();
            return true;
        }

        if (IsDataRibbonKeyTipAttempt(args, directAltToken) &&
            !CanHandleRibbonKeyTipInput(args))
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        if (!sequenceActive &&
            directAltToken is "D" or "E" &&
            !CanHandleRibbonKeyTipInput(args))
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        if (sequenceActive &&
            (args.Key is Key.LeftAlt or Key.RightAlt ||
             args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None))
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        var visibleContinuation = _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None;
        if (!sequenceActive && directAltToken is null && !visibleContinuation)
            return false;

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            var closeBackstage = _backstageOverlay.IsVisible;
            _ribbonKeyTipSession.HandleEscape();
            ResetRibbonKeyTipSequence();
            if (closeBackstage)
                HideBackstageOverlay();
            args.Handled = true;
            return true;
        }

        var acceptsAltContinuation =
            _ribbonKeyTipSession.LegacySequence != FreeXRibbonLegacyKeyTipSequence.None;
        if (sequenceActive &&
            args.KeyModifiers != KeyModifiers.None &&
            (!acceptsAltContinuation || args.KeyModifiers != KeyModifiers.Alt))
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        var token = directAltToken ?? AvaloniaKeyTipTokenFormatter.Format(args.Key);
        if (token is null)
        {
            if (!sequenceActive)
                return false;

            ResetRibbonKeyTipSequence();
            args.Handled = true;
            return true;
        }

        if (!sequenceActive)
            _ribbonKeyTipSession.Enter(FreeXRibbonKeyTipInputScope.Catalog);

        var step = _ribbonKeyTipSession.HandleToken(token);
        switch (step.Intent)
        {
            case FreeXRibbonKeyTipInputIntent.EnterLegacyDataFilter:
                if (_ribbonControl is null ||
                    !AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, "A"))
                {
                    ResetRibbonKeyTipSequence();
                    return false;
                }

                SetRibbonKeyTipsVisible(false);
                args.Handled = true;
                return true;
            case FreeXRibbonKeyTipInputIntent.EnterLegacyEditPasteSpecial:
            case FreeXRibbonKeyTipInputIntent.WaitForContinuation:
                SetRibbonKeyTipsVisible(false);
                args.Handled = true;
                return true;
            case FreeXRibbonKeyTipInputIntent.InvokeLegacyDataFilter:
                ResetRibbonKeyTipSequence();
                ToggleAutoFilter();
                args.Handled = true;
                return true;
            case FreeXRibbonKeyTipInputIntent.InvokeLegacyEditPasteSpecial:
                ResetRibbonKeyTipSequence();
                RunGuarded(() => ShowPasteSpecialDialogAsync());
                args.Handled = true;
                return true;
            case FreeXRibbonKeyTipInputIntent.Cancel:
                ResetRibbonKeyTipSequence();
                args.Handled = true;
                return true;
        }

        var match = AvaloniaRibbonKeyTipRoutes.Match(step.Input);
        if (!match.IsMatch)
        {
            var consume = sequenceActive || visibleContinuation;
            var closeBackstage = sequenceActive && _backstageOverlay.IsVisible;
            ResetRibbonKeyTipSequence();
            if (closeBackstage)
                HideBackstageOverlay();
            args.Handled = consume;
            return consume;
        }

        SetRibbonKeyTipsVisible(false);
        args.Handled = true;
        if (match.ExactRoute is { } route)
            ExecuteRibbonKeyTipRoute(route);

        if (!match.HasLongerRoute)
            ResetRibbonKeyTipSequence();
        return true;
    }

    private bool CanHandleRibbonKeyTipInput(KeyEventArgs args) =>
        !_backstageOverlay.IsVisible &&
        _session.FormulaEditAddress is null &&
        _inlineCellEditor is null &&
        !IsTextEditingEventSource(args);

    private bool IsDataRibbonKeyTipAttempt(KeyEventArgs args, string? directAltToken = null)
    {
        directAltToken ??= args.KeyModifiers == KeyModifiers.Alt
            ? AvaloniaKeyTipTokenFormatter.Format(args.Key)
            : null;
        var sequenceActive = _ribbonKeyTipSession.Scope == FreeXRibbonKeyTipInputScope.Catalog &&
            _ribbonKeyTipSession.Input.Length > 0;
        return sequenceActive &&
                _ribbonKeyTipSession.Input.StartsWith("A", StringComparison.OrdinalIgnoreCase) ||
            !sequenceActive && directAltToken is "A" or "D" ||
            _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None &&
                args.Key is Key.A or Key.D;
    }

    private void ExecuteRibbonKeyTipRoute(FreeXRibbonKeyTipRoute route)
    {
        switch (route.Kind)
        {
            case FreeXRibbonKeyTipRouteKind.RibbonTab:
                if (_ribbonControl is not null && route.TabKeyTip is { } keyTip)
                    AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, keyTip);
                break;
            case FreeXRibbonKeyTipRouteKind.Backstage:
                ShowBackstageOverlay();
                break;
            case FreeXRibbonKeyTipRouteKind.BackstagePane:
                if (route.BackstagePane is { } pane)
                    TryActivateBackstagePane(pane);
                break;
            case FreeXRibbonKeyTipRouteKind.BackstageCommand:
                if (route.BackstageCommand is { } command)
                    TryActivateBackstageCommand(command);
                break;
            case FreeXRibbonKeyTipRouteKind.QuickAccessToolbar:
                ExecuteQuickAccessKeyTip(route.QuickAccessIndex);
                break;
            case FreeXRibbonKeyTipRouteKind.RibbonCommand:
                if (_ribbonControl is not null)
                    AvaloniaRibbonRenderer.TryActivateKeyTip(_ribbonControl, route.Input);
                break;
            case FreeXRibbonKeyTipRouteKind.Scope:
                if (_ribbonControl is not null)
                    AvaloniaRibbonRenderer.TryActivateKeyTip(_ribbonControl, route.Input);
                break;
        }
    }

    private void ExecuteQuickAccessKeyTip(int index)
    {
        var buttons = _avaloniaQuickAccessToolbar.Children
            .OfType<Button>()
            .Where(button => button.Tag is string tag &&
                !tag.EndsWith(".History", StringComparison.Ordinal) &&
                button.IsVisible)
            .ToArray();
        if (index < 0 || index >= buttons.Length || !buttons[index].IsEffectivelyEnabled)
            return;

        var button = buttons[index];
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private void ResetRibbonKeyTipSequence()
    {
        _ribbonKeyTipSession.Cancel();
        SetRibbonKeyTipsVisible(false);
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.CloseKeyTipFlyouts(_ribbonControl);
    }

    /// <summary>
    /// Routes the configured Avalonia Quick Access Toolbar using the WPF keytip policy: visible commands
    /// receive 1-9, then two-character 01, 02 ... tips, while Undo/Redo history buttons remain unkeyed.
    /// </summary>
    private bool TryHandleQuickAccessKeyTipSequence(KeyEventArgs args)
    {
        var sequenceActive =
            _ribbonKeyTipSession.Scope == FreeXRibbonKeyTipInputScope.QuickAccess;
        if (_ribbonKeyTipSession.IsActive && !sequenceActive)
            return false;

        var directAltToken = args.KeyModifiers == KeyModifiers.Alt
            ? AvaloniaKeyTipTokenFormatter.Format(args.Key)
            : null;
        var visibleContinuation = _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None;
        if (!sequenceActive && directAltToken is null && !visibleContinuation)
            return false;

        if (!CanHandleRibbonKeyTipInput(args))
        {
            if (sequenceActive)
                ResetRibbonKeyTipSequence();
            return false;
        }

        if (sequenceActive &&
            (args.Key is Key.LeftAlt or Key.RightAlt ||
             args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None))
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            if (!sequenceActive)
                return false;

            _ribbonKeyTipSession.HandleEscape();
            ResetRibbonKeyTipSequence();
            args.Handled = true;
            return true;
        }

        if (sequenceActive && args.KeyModifiers != KeyModifiers.None)
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        var token = directAltToken ?? AvaloniaKeyTipTokenFormatter.Format(args.Key);
        if (token is null)
        {
            if (!sequenceActive)
                return false;

            ResetRibbonKeyTipSequence();
            args.Handled = true;
            return true;
        }

        var nextInput = sequenceActive ? _ribbonKeyTipSession.Input + token : token;
        var match = MatchAvaloniaQuickAccessKeyTip(nextInput);
        if (!match.IsMatch)
        {
            if (!sequenceActive)
                return false;

            ResetRibbonKeyTipSequence();
            args.Handled = true;
            return true;
        }

        if (!sequenceActive)
            _ribbonKeyTipSession.Enter(FreeXRibbonKeyTipInputScope.QuickAccess);
        var step = _ribbonKeyTipSession.HandleToken(token, recognizeLegacyTopLevel: false);
        SetRibbonKeyTipsVisible(false);
        args.Handled = true;

        if (match.ExactKeyTip is { } exact)
        {
            ExecuteQuickAccessKeyTip(exact);
            if (!match.HasLongerKeyTip)
                ResetRibbonKeyTipSequence();
        }

        return step.Handled;
    }

    private (string? ExactKeyTip, bool HasLongerKeyTip, bool IsMatch) MatchAvaloniaQuickAccessKeyTip(
        string input)
    {
        var normalized = RibbonKeyTipText.NormalizeOrEmpty(input);
        var exact = _avaloniaQuickAccessKeyTipButtons.Keys
            .FirstOrDefault(keyTip => string.Equals(keyTip, normalized, StringComparison.OrdinalIgnoreCase));
        var hasLonger = _avaloniaQuickAccessKeyTipButtons.Keys.Any(keyTip =>
            keyTip.Length > normalized.Length &&
            keyTip.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
        return (exact, hasLonger, exact is not null || hasLonger);
    }

    private void ExecuteQuickAccessKeyTip(string keyTip)
    {
        if (!_avaloniaQuickAccessKeyTipButtons.TryGetValue(keyTip, out var button))
            return;

        if (!button.IsEffectivelyEnabled)
            return;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
}
