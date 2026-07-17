using Avalonia.Input;

using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal enum LegacyDataFilterSequenceState
    {
        None,
        AwaitingFirstFilterKey,
        AwaitingSecondFilterKey,
    }

    private LegacyDataFilterSequenceState _legacyDataFilterSequenceState;

    internal LegacyDataFilterSequenceState LegacyDataFilterSequenceStateForTest =>
        _legacyDataFilterSequenceState;

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
        var sequenceActive = _legacyDataFilterSequenceState != LegacyDataFilterSequenceState.None;
        var startsSequence = IsLegacyDataFilterSequenceStart(args);

        if (!CanHandleLegacyDataFilterSequence(args))
        {
            ResetLegacyDataFilterSequence();
            return sequenceActive || startsSequence;
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

    private bool IsLegacyDataFilterSequenceStart(KeyEventArgs args) =>
        args.Key == Key.D &&
        (args.KeyModifiers == KeyModifiers.Alt ||
            _ribbonKeyTipsVisible && args.KeyModifiers == KeyModifiers.None);

    private void ResetLegacyDataFilterSequence()
    {
        _legacyDataFilterSequenceState = LegacyDataFilterSequenceState.None;
        SetRibbonKeyTipsVisible(false);
    }
}
