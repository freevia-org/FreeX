namespace FreeP.App.Compositor;

/// <summary>
/// Shared selection chrome for the WPF RichTextBox authority and the Avalonia
/// rich-text realization. WPF exposes these values through its native selection
/// properties; Avalonia uses them when painting the synchronized surface.
/// </summary>
public static class InCanvasRichTextSelectionVisualContract
{
    public const double SelectionOpacity = 0.4;

    public const byte BackgroundRed = 0x00;
    public const byte BackgroundGreen = 0x78;
    public const byte BackgroundBlue = 0xD7;
    public const byte BackgroundAlpha = 0xFF;

    public const byte ForegroundRed = 0xFF;
    public const byte ForegroundGreen = 0xFF;
    public const byte ForegroundBlue = 0xFF;
    public const byte ForegroundAlpha = 0xFF;

    // WPF composites the nominal selection brushes through SelectionOpacity.
    // Avalonia paints these shared 96-DPI realized colors directly because its
    // custom rich-text surface does not have an equivalent native selection layer.
    public const byte RealizedBackgroundRed = 0x99;
    public const byte RealizedBackgroundGreen = 0xC9;
    public const byte RealizedBackgroundBlue = 0xEF;
    public const byte RealizedBackgroundAlpha = 0xFF;

    public const byte RealizedForegroundRed = 0x1C;
    public const byte RealizedForegroundGreen = 0x63;
    public const byte RealizedForegroundBlue = 0xB1;
    public const byte RealizedForegroundAlpha = 0xFF;

    // Avalonia's TextLayout selection bounds include the full platform line box, while WPF's
    // RichTextBox selection layer paints the tighter glyph-selection band. Keep this calibration
    // in the shared contract so the realized surface does not grow an unexplained local palette.
    public const double RealizedSelectionTopInsetDip = 2;
    public const double RealizedSelectionBottomInsetDip = 2;
    public const double RealizedSelectionLeadingExpandDip = 1;

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
