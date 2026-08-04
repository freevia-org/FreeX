using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal enum LegacyDataFilterSequenceState
    {
        None,
        AwaitingFirstFilterKey,
        AwaitingSecondFilterKey,
    }

    internal enum LegacyEditPasteSpecialSequenceState
    {
        None,
        AwaitingPasteSpecialKey,
    }

    private LegacyDataFilterSequenceState _legacyDataFilterSequenceState;
    private LegacyEditPasteSpecialSequenceState _legacyEditPasteSpecialSequenceState;
    private string _ribbonKeyTipInput = "";

    internal LegacyDataFilterSequenceState LegacyDataFilterSequenceStateForTest =>
        _legacyDataFilterSequenceState;
    internal LegacyEditPasteSpecialSequenceState LegacyEditPasteSpecialSequenceStateForTest =>
        _legacyEditPasteSpecialSequenceState;
    internal string RibbonKeyTipInputForTest => _ribbonKeyTipInput;

    internal static IReadOnlySet<string> InteractiveValidationLegacyDataFilterInteractionIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "shortcut.data.filter-toggle-reapply:0",
            "shortcut.data.filter-toggle-reapply:1",
            "shortcut.data.filter-toggle-reapply:2",
        };

    /// <summary>
    /// Handles Excel's legacy Data &gt; Filter &gt; AutoFilter access-key sequence. The modern
    /// ribbon reserves A for the Data tab, but D remains a compatibility alias for this sequence.
    /// </summary>
    private bool TryHandleLegacyDataFilterSequence(KeyEventArgs args)
    {
        if (TryHandleLegacyEditPasteSpecialSequence(args))
            return true;

        if (TryHandleCataloguedRibbonKeyTipSequence(args))
            return true;

        var sequenceActive = _legacyDataFilterSequenceState != LegacyDataFilterSequenceState.None;
        var startsSequence = IsLegacyDataFilterSequenceStart(args);

        if (!CanHandleLegacyDataFilterSequence(args))
        {
            ResetLegacyDataFilterSequence();
            return sequenceActive;
        }

        if (!sequenceActive)
        {
            if (!startsSequence)
                return false;

            // WPF treats D as a legacy alias for Data even though its visible modern keytip is A.
            if (_ribbonControl is null ||
                !AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, "A"))
            {
                ResetLegacyDataFilterSequence();
                return false;
            }

            SetRibbonKeyTipsVisible(false);
            _legacyDataFilterSequenceState = LegacyDataFilterSequenceState.AwaitingFirstFilterKey;
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            ResetLegacyDataFilterSequence();
            args.Handled = true;
            return true;
        }

        if (args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))
        {
            // Let direct shortcuts continue through the normal dispatcher after abandoning the
            // incomplete legacy sequence.
            ResetLegacyDataFilterSequence();
            return false;
        }

        if (args.Key == Key.F)
        {
            args.Handled = true;
            if (_legacyDataFilterSequenceState == LegacyDataFilterSequenceState.AwaitingFirstFilterKey)
            {
                _legacyDataFilterSequenceState = LegacyDataFilterSequenceState.AwaitingSecondFilterKey;
                return true;
            }

            ResetLegacyDataFilterSequence();
            ToggleAutoFilter();
            return true;
        }

        // WPF consumes an invalid keytip continuation and exits keytip mode. Do the same so an
        // accidental prefix cannot leak a character or worksheet command into the active sheet.
        ResetLegacyDataFilterSequence();
        args.Handled = true;
        return true;
    }

    private bool CanHandleLegacyDataFilterSequence(KeyEventArgs args) =>
        !_backstageOverlay.IsVisible &&
        _session.FormulaEditAddress is null &&
        _inlineCellEditor is null &&
        !IsTextEditingEventSource(args);

    private bool TryHandleLegacyEditPasteSpecialSequence(KeyEventArgs args)
    {
        var sequenceActive = _legacyEditPasteSpecialSequenceState !=
            LegacyEditPasteSpecialSequenceState.None;
        var startsSequence = args.Key == Key.E &&
            (args.KeyModifiers == KeyModifiers.Alt ||
             _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None);

        if (!CanHandleLegacyDataFilterSequence(args))
        {
            if (sequenceActive)
                ResetLegacyEditPasteSpecialSequence();
            return sequenceActive;
        }

        if (!sequenceActive)
        {
            if (!startsSequence)
                return false;

            // WPF preserves Excel's legacy Edit > Paste Special access-key route even though
            // the current ribbon has no visible Edit tab.
            SetRibbonKeyTipsVisible(false);
            _ribbonKeyTipInput = "E";
            _legacyEditPasteSpecialSequenceState =
                LegacyEditPasteSpecialSequenceState.AwaitingPasteSpecialKey;
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            ResetLegacyEditPasteSpecialSequence();
            args.Handled = true;
            return true;
        }

        if (args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Alt))
        {
            ResetLegacyEditPasteSpecialSequence();
            return false;
        }

        if (args.Key == Key.S)
        {
            ResetLegacyEditPasteSpecialSequence();
            _ = ShowPasteSpecialDialogAsync();
            args.Handled = true;
            return true;
        }

        // Match WPF: an invalid continuation is consumed and exits the legacy keytip scope.
        ResetLegacyEditPasteSpecialSequence();
        args.Handled = true;
        return true;
    }

    private bool IsLegacyDataFilterSequenceStart(KeyEventArgs args) =>
        args.Key == Key.D &&
        (args.KeyModifiers == KeyModifiers.Alt ||
            _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None);

    private void ResetLegacyDataFilterSequence()
    {
        _legacyDataFilterSequenceState = LegacyDataFilterSequenceState.None;
        ResetRibbonKeyTipSequence();
    }

    private void ResetLegacyEditPasteSpecialSequence()
    {
        _legacyEditPasteSpecialSequenceState = LegacyEditPasteSpecialSequenceState.None;
        ResetRibbonKeyTipSequence();
    }

    private bool TryHandleCataloguedRibbonKeyTipSequence(KeyEventArgs args)
    {
        var sequenceActive = _ribbonKeyTipInput.Length > 0;
        if (sequenceActive &&
            (args.Key is Key.LeftAlt or Key.RightAlt ||
             args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None))
        {
            ResetRibbonKeyTipSequence();
            // Let the existing top-level handler reopen its badges. This also makes a fresh Alt
            // naturally recover from any abandoned nested path.
            return false;
        }

        var directAltToken = args.KeyModifiers == KeyModifiers.Alt
            ? ToRibbonKeyTipToken(args.Key)
            : null;
        var visibleContinuation = _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None;
        if (!sequenceActive && directAltToken is null && !visibleContinuation)
            return false;

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            var closeBackstage = _backstageOverlay.IsVisible;
            ResetRibbonKeyTipSequence();
            if (closeBackstage)
                HideBackstageOverlay();
            args.Handled = true;
            return true;
        }

        if (sequenceActive && args.KeyModifiers != KeyModifiers.None)
        {
            ResetRibbonKeyTipSequence();
            return false;
        }

        var token = directAltToken ?? ToRibbonKeyTipToken(args.Key);
        if (token is null)
        {
            if (!sequenceActive)
                return false;

            ResetRibbonKeyTipSequence();
            args.Handled = true;
            return true;
        }

        var nextInput = sequenceActive ? _ribbonKeyTipInput + token : token;
        var match = AvaloniaRibbonKeyTipRoutes.Match(nextInput);
        if (!match.IsMatch)
        {
            // Alt+D and Alt, D belong to the legacy Data > Filter compatibility sequence below.
            if (!sequenceActive && token == "D")
                return false;

            var consume = sequenceActive || visibleContinuation;
            var closeBackstage = sequenceActive && _backstageOverlay.IsVisible;
            ResetRibbonKeyTipSequence();
            if (closeBackstage)
                HideBackstageOverlay();
            args.Handled = consume;
            return consume;
        }

        _ribbonKeyTipInput = nextInput;
        SetRibbonKeyTipsVisible(false);
        args.Handled = true;

        if (match.ExactRoute is { } route)
            ExecuteRibbonKeyTipRoute(route);

        if (!match.HasLongerRoute)
            ResetRibbonKeyTipSequence();
        return true;
    }

    private void ExecuteRibbonKeyTipRoute(AvaloniaRibbonKeyTipRoute route)
    {
        switch (route.Kind)
        {
            case AvaloniaRibbonKeyTipRouteKind.RibbonTab:
                if (_ribbonControl is not null && route.TabKeyTip is { } keyTip)
                    AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, keyTip);
                break;
            case AvaloniaRibbonKeyTipRouteKind.Backstage:
                ShowBackstageOverlay();
                break;
            case AvaloniaRibbonKeyTipRouteKind.BackstagePane:
                if (route.BackstagePane is { } pane)
                    TryActivateBackstagePane(pane);
                break;
            case AvaloniaRibbonKeyTipRouteKind.BackstageCommand:
                if (route.BackstageCommand is { } command)
                    TryActivateBackstageCommand(command);
                break;
            case AvaloniaRibbonKeyTipRouteKind.QuickAccessToolbar:
                ExecuteQuickAccessKeyTip(route.QuickAccessIndex);
                break;
            case AvaloniaRibbonKeyTipRouteKind.RibbonCommand:
                if (_ribbonControl is not null)
                    AvaloniaRibbonRenderer.TryActivateKeyTip(_ribbonControl, route.Input);
                break;
            case AvaloniaRibbonKeyTipRouteKind.Scope:
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
        _ribbonKeyTipInput = "";
        SetRibbonKeyTipsVisible(false);
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.CloseKeyTipFlyouts(_ribbonControl);
    }
}
