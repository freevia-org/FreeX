using System.Text.RegularExpressions;

namespace Free.Shared.Ribbon;

/// <summary>
/// Platform-neutral shortcut text mapping. Ribbon definitions author Windows shortcut text
/// ("Ctrl+S"); on macOS the command modifier is Cmd and redo is Shift+Cmd+Z. With
/// <c>macOs: false</c> every input is returned unchanged so Windows and Linux keep Ctrl.
/// </summary>
public static partial class RibbonShortcutText
{
    public static string ForPlatform(string? text, bool macOs)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;
        if (!macOs)
            return text;

        var mapped = RedoRegex().Replace(text, "Shift+Cmd+Z");
        mapped = CtrlShiftRegex().Replace(mapped, "Shift+Cmd+");
        return CtrlRegex().Replace(mapped, "Cmd+");
    }

    /// <summary>Same mapping for an Avalonia-parsable gesture string ("Cmd" parses to the Meta key).</summary>
    public static string ToGestureForPlatform(string? gesture, bool macOs) => ForPlatform(gesture, macOs);

    [GeneratedRegex(@"\bCtrl\+Y\b")]
    private static partial Regex RedoRegex();

    [GeneratedRegex(@"\bCtrl\+Shift\+")]
    private static partial Regex CtrlShiftRegex();

    [GeneratedRegex(@"\bCtrl\+")]
    private static partial Regex CtrlRegex();
}
