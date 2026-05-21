using System.Windows.Input;

namespace Freexcel.App.Host;

internal sealed class StandaloneAltKeyTipTracker
{
    private const int VkMenu = 0x12;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;

    private bool _pending;

    public void BeginStandaloneAltCandidate()
    {
        _pending = true;
    }

    public void CancelStandaloneAltCandidate()
    {
        _pending = false;
    }

    public bool ShouldToggleOnKeyUp(Key key)
    {
        if (!_pending)
            return false;

        _pending = false;
        return IsStandaloneAltKey(key);
    }

    public static bool IsStandaloneAltKey(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.System;

    public static bool IsAltVirtualKey(int virtualKey) =>
        virtualKey is VkMenu or VkLeftMenu or VkRightMenu;
}
