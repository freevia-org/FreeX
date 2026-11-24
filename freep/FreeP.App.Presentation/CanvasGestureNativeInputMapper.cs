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


}
