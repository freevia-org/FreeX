using Avalonia.Input;
using Avalonia.Threading;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// Parity for the WPF host's r79 CAPS LOCK / NUM LOCK status-bar indicators (mirrors the WPF
/// shell's key-lock indicator refresh in its grid-status partial). The WPF shell reads the OS-level
/// toggle state directly on every refresh. This shell queries Windows toggle state and macOS Caps Lock
/// state too, keeping a key-down-tracked fallback only where a native query is unavailable. Avalonia's
/// macOS backend does not forward Caps Lock flagsChanged events as key-downs, so an active window also
/// refreshes its indicators periodically. Both shells use <see cref="KeyLockIndicatorPlanner"/> for
/// the visibility rule.
/// </summary>
public sealed partial class MainWindow
{
    private bool _isCapsLockToggleOnForShell;
    private bool _isNumLockToggleOnForShell;
    private DispatcherTimer? _macOsKeyLockRefreshTimer;

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
        EnsureMacOsKeyLockRefresh();
        if (TryGetOsKeyToggleState(Key.CapsLock) is { } capsLockOn)
            _isCapsLockToggleOnForShell = capsLockOn;
        if (TryGetOsKeyToggleState(Key.NumLock) is { } numLockOn)
            _isNumLockToggleOnForShell = numLockOn;
    }

    private void EnsureMacOsKeyLockRefresh()
    {
        if (!OperatingSystem.IsMacOS() || _macOsKeyLockRefreshTimer is not null)
            return;

        // Avalonia.Native 12.0.4's flagsChanged forwards Shift/Control/Option/Command, but
        // not Caps Lock. Poll only while this window is active; no global input monitor or
        // accessibility permission is needed, and a closed window never retains a live timer.
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += (_, _) => RefreshKeyLockIndicators();
        _macOsKeyLockRefreshTimer = timer;
        Activated += (_, _) => timer.Start();
        Deactivated += (_, _) => timer.Stop();
        Closed += (_, _) => timer.Stop();
        if (IsActive)
            timer.Start();
    }

    /// <summary>
    /// Best-effort direct OS toggle-state query for <paramref name="key"/> (CapsLock/NumLock only).
    /// Returns null when no such query is available (or it fails), so callers keep whatever value they
    /// already had instead of clobbering it with a false negative. Windows exposes this directly via
    /// user32's GetKeyState (the low-order bit reflects the toggle state). macOS provides Caps Lock
    /// through Quartz's combined-session modifier flags; its numeric-pad flag is not a Num Lock toggle.
    /// </summary>
    private bool? TryGetOsKeyToggleState(Key key)
    {
        Func<Key, bool?>? overrideHandler = null;
        ResolveKeyLockToggleStateOverride(ref overrideHandler);
        if (overrideHandler is not null)
            return overrideHandler(key);

        if (OperatingSystem.IsMacOS())
            return key == Key.CapsLock ? TryGetMacOsCapsLockToggleState() : null;

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

    internal static bool? TryGetMacOsCapsLockToggleState()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        try
        {
            return (KeyLockNativeMethods.CGEventSourceFlagsState(0) & 0x00010000UL) != 0;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    static partial void ResolveKeyLockToggleStateOverride(ref Func<Key, bool?>? handler);

    private static class KeyLockNativeMethods
    {
        public const int VK_CAPITAL = 0x14;
        public const int VK_NUMLOCK = 0x90;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        // CGEventSourceStateID is int32_t; CGEventFlags is uint64_t. 0 selects
        // kCGEventSourceStateCombinedSessionState; 0x00010000 is kCGEventFlagMaskAlphaShift.
        [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        public static extern ulong CGEventSourceFlagsState(int stateId);
    }

    /// <summary>
    /// Applies the shared <see cref="KeyLockIndicatorPlanner"/> to the currently-tracked toggle state,
    /// showing/hiding <see cref="_statusCapsLockText"/> and <see cref="_statusNumLockText"/>. Called both
    /// right after a CapsLock/NumLock key-down and from every <see cref="RefreshShell"/> pass (mirroring
    /// the WPF host calling RefreshKeyLockIndicators from RefreshStatusBar).
    /// </summary>
    private void RefreshKeyLockIndicators()
    {
        // The OS is authoritative even when a synthetic/repeated KeyDown was delivered.
        ResyncKeyLockToggleStateFromOs();
        var plan = KeyLockIndicatorPlanner.Build(_isCapsLockToggleOnForShell, _isNumLockToggleOnForShell);
        _statusCapsLockText.IsVisible = plan.CapsLockVisible;
        _statusNumLockText.IsVisible = plan.NumLockVisible;
    }
}
