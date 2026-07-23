using Avalonia.Input;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// Parity for the WPF host's r79 CAPS LOCK / NUM LOCK status-bar indicators (mirrors the WPF
/// shell's key-lock indicator refresh in its grid-status partial). The WPF shell reads the OS-level
/// toggle state directly via a Win32-only keyboard toggle-state API, which has no
/// cross-platform Avalonia equivalent; the Avalonia shell instead tracks each key's on/off state by
/// flipping it every time a CapsLock/NumLock key-down reaches <see cref="MainWindow_KeyDownAsync"/>
/// while the window has focus -- the same alternate-on-each-press semantics a physical toggle key
/// has. Both shells still funnel through the shared, fully-tested <see cref="KeyLockIndicatorPlanner"/>
/// so the visibility rule itself (only while the key is toggled on) is identical.
/// </summary>
public sealed partial class MainWindow
{
    private bool _isCapsLockToggleOnForShell;
    private bool _isNumLockToggleOnForShell;

    /// <summary>Flips the tracked toggle state for a CapsLock/NumLock key-down; no-op for any other key.</summary>
    private void UpdateKeyLockToggleState(Key key)
    {
        if (key == Key.CapsLock)
            _isCapsLockToggleOnForShell = !_isCapsLockToggleOnForShell;
        else if (key == Key.NumLock)
            _isNumLockToggleOnForShell = !_isNumLockToggleOnForShell;
    }

    /// <summary>
    /// Applies the shared <see cref="KeyLockIndicatorPlanner"/> to the currently-tracked toggle state,
    /// showing/hiding <see cref="_statusCapsLockText"/> and <see cref="_statusNumLockText"/>. Called both
    /// right after a CapsLock/NumLock key-down and from every <see cref="RefreshShell"/> pass (mirroring
    /// the WPF host calling RefreshKeyLockIndicators from RefreshStatusBar).
    /// </summary>
    private void RefreshKeyLockIndicators()
    {
        var plan = KeyLockIndicatorPlanner.Build(_isCapsLockToggleOnForShell, _isNumLockToggleOnForShell);
        _statusCapsLockText.IsVisible = plan.CapsLockVisible;
        _statusNumLockText.IsVisible = plan.NumLockVisible;
    }

    /// <summary>Test-only seam exposing the CAPS LOCK indicator's live visibility.</summary>
    internal bool IsCapsLockIndicatorVisibleForTest => _statusCapsLockText.IsVisible;

    /// <summary>Test-only seam exposing the NUM LOCK indicator's live visibility.</summary>
    internal bool IsNumLockIndicatorVisibleForTest => _statusNumLockText.IsVisible;
}
