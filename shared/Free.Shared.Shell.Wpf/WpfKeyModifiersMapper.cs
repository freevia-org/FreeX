using System.Windows.Input;

namespace Free.Shared.Shell.Wpf;

/// <summary>The single WPF <see cref="ModifierKeys"/> to <see cref="FreeKeyModifiers"/> mapper.</summary>
public static class WpfKeyModifiersMapper
{
    /// <summary>
    /// Maps WPF modifiers; <see cref="ModifierKeys.Windows"/> is the Meta key and follows
    /// <paramref name="metaPolicy"/>.
    /// </summary>
    public static FreeKeyModifiers FromWpf(
        ModifierKeys modifiers,
        KeyModifierMetaPolicy metaPolicy = KeyModifierMetaPolicy.Keep)
    {
        var result = FreeKeyModifiers.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= FreeKeyModifiers.Shift;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= FreeKeyModifiers.Control;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= FreeKeyModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            result |= metaPolicy switch
            {
                KeyModifierMetaPolicy.Keep => FreeKeyModifiers.Meta,
                KeyModifierMetaPolicy.AsControl => FreeKeyModifiers.Control,
                _ => FreeKeyModifiers.None,
            };
        }

        return result;
    }

    /// <summary>Maps neutral modifiers back to WPF (Meta becomes the Windows key).</summary>
    public static ModifierKeys ToWpf(FreeKeyModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if (modifiers.HasFlag(FreeKeyModifiers.Control))
            result |= ModifierKeys.Control;
        if (modifiers.HasFlag(FreeKeyModifiers.Shift))
            result |= ModifierKeys.Shift;
        if (modifiers.HasFlag(FreeKeyModifiers.Alt))
            result |= ModifierKeys.Alt;
        if (modifiers.HasFlag(FreeKeyModifiers.Meta))
            result |= ModifierKeys.Windows;
        return result;
    }
}
