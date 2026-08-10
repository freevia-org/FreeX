using Avalonia.Input;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// Parity for the WPF host's r79 CAPS LOCK / NUM LOCK status-bar indicators (mirrors the WPF
/// shell's key-lock indicator refresh in its grid-status partial). The WPF shell reads the OS-level
/// toggle state directly via a Win32-only keyboard toggle-state API on every refresh, so it is always
/// correct there. Avalonia has no cross-platform equivalent of that API, so this shell instead tracks
/// each key's on/off state by flipping it every time a CapsLock/NumLock key-down reaches
/// <see cref="MainWindow_KeyDownAsync"/> while the window has focus -- the same alternate-on-each-press
/// semantics a physical toggle key has. That tracking alone desyncs from the real toggle whenever the
/// key was pressed before this window existed or while some other window had focus (a key-down that
/// never reaches this shell), so <see cref="ResyncKeyLockToggleStateFromOs"/> additionally
/// initializes/resyncs the tracked state from a direct OS query where one is available (today: Windows,
/// via <see cref="TryGetOsKeyToggleState"/>) both at construction and on every window activation --
/// the two moments a desync could have crept in unnoticed. Platforms without such a query keep the
/// prior key-down-tracked approximation unchanged (a still-accepted, pre-existing limitation). Both
/// shells still funnel through the shared, fully-tested <see cref="KeyLockIndicatorPlanner"/> so the
/// visibility rule itself (only while the key is toggled on) is identical.
/// </summary>
public sealed partial class MainWindow
{
    private bool _isCapsLockToggleOnForShell;
    private bool _isNumLockToggleOnForShell;

    /// <summary>
    /// Test-only override for <see cref="TryGetOsKeyToggleState"/>, so tests can simulate a real OS
    /// toggle state (e.g. "CapsLock is on but no key-down ever reached this shell") without depending on
    /// the actual keyboard LED state of the machine running the test. Static rather than an instance
    /// field because <see cref="ResyncKeyLockToggleStateFromOs"/> runs from the constructor itself,
    /// before a test can reach an instance to install an override -- tests MUST reset this to null when
    /// done since it is shared static state (the AvaloniaHeadless test collection disables
    /// parallelization, so this is safe within it).
    /// </summary>
    internal static Func<Key, bool?>? KeyLockOsToggleStateOverrideForTest;

    /// <summary>Flips the tracked toggle state for a CapsLock/NumLock key-down; no-op for any other key.</summary>
    private void UpdateKeyLockToggleState(Key key)
    {
        if (key == Key.CapsLock)
            _isCapsLockToggleOnForShell = !_isCapsLockToggleOnForShell;
        else if (key == Key.NumLock)
            _isNumLockToggleOnForShell = !_isNumLockToggleOnForShell;
    }

    /// <summary>
    /// R82-meta-3: initializes/resyncs the tracked toggle state from <see cref="TryGetOsKeyToggleState"/>
    /// wherever that query succeeds, leaving the previously-tracked value untouched otherwise (so a
    /// platform/key combination with no OS query never regresses to a hardcoded false). Called once at
    /// construction (fixing the "Caps Lock was already on before launch" case) and again on every window
    /// Activated (fixing the "toggled while another window had focus" case) -- see the type-level doc
    /// comment for why key-down tracking alone cannot resync either case on its own.
    /// </summary>
    private void ResyncKeyLockToggleStateFromOs()
    {
        if (TryGetOsKeyToggleState(Key.CapsLock) is { } capsLockOn)
            _isCapsLockToggleOnForShell = capsLockOn;
        if (TryGetOsKeyToggleState(Key.NumLock) is { } numLockOn)
            _isNumLockToggleOnForShell = numLockOn;
    }

    /// <summary>Test-only seam for <see cref="ResyncKeyLockToggleStateFromOs"/>.</summary>
    internal void ResyncKeyLockToggleStateFromOsForTest() => ResyncKeyLockToggleStateFromOs();

    /// <summary>
    /// Best-effort direct OS toggle-state query for <paramref name="key"/> (CapsLock/NumLock only).
    /// Returns null when no such query is available (or it fails), so callers keep whatever value they
    /// already had instead of clobbering it with a false negative. Windows exposes this directly via
    /// user32's GetKeyState (the low-order bit reflects the toggle state); there is no Avalonia-visible
    /// equivalent on other platforms today, so this returns null there.
    /// </summary>
    private bool? TryGetOsKeyToggleState(Key key)
    {
        if (KeyLockOsToggleStateOverrideForTest is { } testOverride)
            return testOverride(key);

        if (!OperatingSystem.IsWindows())
            return null;

        var virtualKey = key switch
        {
            Key.CapsLock => KeyLockNativeMethods.VK_CAPITAL,
            Key.NumLock => KeyLockNativeMethods.VK_NUMLOCK,
            _ => (int?)null,
        };
        if (virtualKey is null)
            return null;

        try
        {
            return (KeyLockNativeMethods.GetKeyState(virtualKey.Value) & 0x0001) != 0;
        }
        catch
        {
            // Mirrors the wheel-scroll-lines lookup's own try/catch fallback (GetSystemWheelScrollLines
            // in MainWindow.cs): never let an OS toggle-state query failure break the indicator refresh.
            return null;
        }
    }

    private static class KeyLockNativeMethods
    {
        public const int VK_CAPITAL = 0x14;
        public const int VK_NUMLOCK = 0x90;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);
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
