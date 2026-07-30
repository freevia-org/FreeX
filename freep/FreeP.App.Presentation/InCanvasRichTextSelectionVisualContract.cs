namespace FreeP.App.Compositor;

/// <summary>
/// Shared selection chrome for the WPF RichTextBox authority and the Avalonia
/// rich-text realization. WPF exposes these values through its native selection
/// properties; Avalonia uses them when painting the synchronized surface.
/// </summary>
public static class InCanvasRichTextSelectionVisualContract
{
    public const byte BackgroundRed = 0x00;
    public const byte BackgroundGreen = 0x78;
    public const byte BackgroundBlue = 0xD7;
    public const byte BackgroundAlpha = 0xFF;

    public const byte ForegroundRed = 0xFF;
    public const byte ForegroundGreen = 0xFF;
    public const byte ForegroundBlue = 0xFF;
    public const byte ForegroundAlpha = 0xFF;

    public static InCanvasRichTextSelectionRange NormalizeRange(
        int start,
        int end,
        int textLength)
    {
        int clampedStart = Math.Clamp(start, 0, Math.Max(0, textLength));
        int clampedEnd = Math.Clamp(end, 0, Math.Max(0, textLength));
        return clampedStart <= clampedEnd
            ? new(clampedStart, clampedEnd)
            : new(clampedEnd, clampedStart);
    }
}

public readonly record struct InCanvasRichTextSelectionRange(int Start, int End)
{
    public bool IsEmpty => Start == End;

    public int Length => End - Start;
}
