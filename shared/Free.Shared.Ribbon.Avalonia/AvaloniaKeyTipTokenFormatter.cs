using Avalonia.Input;
using Free.Shared.Ribbon.KeyTips;

namespace Free.Shared.Ribbon.Avalonia;

public static class AvaloniaKeyTipTokenFormatter
{
    public static string? Format(Key key)
    {
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return RibbonKeyTipText.Normalize(name);
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            return name[1].ToString();
        return null;
    }
}
