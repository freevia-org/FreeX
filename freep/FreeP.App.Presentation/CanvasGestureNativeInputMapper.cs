namespace FreeP.App.Compositor;

public static class CanvasGestureNativeInputMapper
{
    public static CanvasGestureKey MapKeyName(string? keyName) => keyName switch
    {
        "Escape" => CanvasGestureKey.Escape,
        "Left" => CanvasGestureKey.Left,
        "Right" => CanvasGestureKey.Right,
        "Up" => CanvasGestureKey.Up,
        "Down" => CanvasGestureKey.Down,
        "Delete" => CanvasGestureKey.Delete,
        "Back" or "Backspace" => CanvasGestureKey.Backspace,
        "Insert" => CanvasGestureKey.Insert,
        _ => CanvasGestureKey.None,
    };

    public static CanvasGestureModifiers MapModifiers(
        bool shift,
        bool control,
        bool alt,
        bool meta)
    {
        var modifiers = CanvasGestureModifiers.None;
        if (shift)
            modifiers |= CanvasGestureModifiers.Shift;
        if (control)
            modifiers |= CanvasGestureModifiers.Control;
        if (alt)
            modifiers |= CanvasGestureModifiers.Alt;
        if (meta)
            modifiers |= CanvasGestureModifiers.Meta;
        return modifiers;
    }
}
