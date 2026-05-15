using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum FontToggleShortcut
{
    Bold,
    Italic,
    Underline,
    Strikethrough
}

public static class FontToggleShortcutService
{
    public static StyleDiff CreateDiff(FontToggleShortcut shortcut, bool enabled) => shortcut switch
    {
        FontToggleShortcut.Bold => new StyleDiff(Bold: enabled),
        FontToggleShortcut.Italic => new StyleDiff(Italic: enabled),
        FontToggleShortcut.Underline => new StyleDiff(Underline: enabled),
        FontToggleShortcut.Strikethrough => new StyleDiff(Strikethrough: enabled),
        _ => new StyleDiff()
    };
}
