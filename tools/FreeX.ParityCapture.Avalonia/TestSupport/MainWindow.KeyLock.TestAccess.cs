using Avalonia.Input;
using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Test-only seam for <see cref="ResyncKeyLockToggleStateFromOs"/>.</summary>
    internal void ResyncKeyLockToggleStateFromOsForTest() => ResyncKeyLockToggleStateFromOs();

    /// <summary>Test-only seam exposing the CAPS LOCK indicator's live visibility.</summary>
    internal bool IsCapsLockIndicatorVisibleForTest => _statusCapsLockText.IsVisible;

    /// <summary>Test-only seam exposing the NUM LOCK indicator's live visibility.</summary>
    internal bool IsNumLockIndicatorVisibleForTest => _statusNumLockText.IsVisible;

    internal static Func<Key, bool?>? KeyLockOsToggleStateOverrideForTest;

    static partial void ResolveKeyLockToggleStateOverride(ref Func<Key, bool?>? handler) =>
        handler = KeyLockOsToggleStateOverrideForTest;

}
