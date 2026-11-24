using Avalonia.Input;

namespace Free.Shared.Shell.Avalonia;

/// <summary>The single Avalonia <see cref="KeyModifiers"/> to <see cref="FreeKeyModifiers"/> mapper.</summary>
public static class AvaloniaKeyModifiersMapper
{
    /// <summary>
    /// Maps Avalonia modifiers; <see cref="KeyModifiers.Meta"/> (Command on macOS) follows
    /// <paramref name="metaPolicy"/>. Control is kept as Control (never merged into Meta) under every policy.
    /// </summary>
    public static FreeKeyModifiers FromAvalonia(
        KeyModifiers modifiers,
        KeyModifierMetaPolicy metaPolicy = KeyModifierMetaPolicy.Keep)
    {
        var result = FreeKeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= FreeKeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= FreeKeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= FreeKeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta))
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

    /// <summary>Maps neutral modifiers back to Avalonia.</summary>
    public static KeyModifiers ToAvalonia(FreeKeyModifiers modifiers)
    {
        var result = KeyModifiers.None;
        if (modifiers.HasFlag(FreeKeyModifiers.Control))
            result |= KeyModifiers.Control;
        if (modifiers.HasFlag(FreeKeyModifiers.Alt))
            result |= KeyModifiers.Alt;
        if (modifiers.HasFlag(FreeKeyModifiers.Shift))
            result |= KeyModifiers.Shift;
        if (modifiers.HasFlag(FreeKeyModifiers.Meta))
            result |= KeyModifiers.Meta;
        return result;
    }
}
