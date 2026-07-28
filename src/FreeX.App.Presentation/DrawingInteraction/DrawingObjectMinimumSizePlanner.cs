namespace FreeX.App.Presentation.DrawingInteraction;

/// <summary>
/// Drawing-object minimum dimensions used by both desktop hosts when a resize is committed.
/// These values preserve the WPF worksheet interaction contract for objects whose chrome needs a
/// usable surface even when a drag is taken below the minimum.
/// </summary>
public enum DrawingObjectMinimumSizeKind
{
    Shape,
    Picture,
    TextBox,
    Chart
}

public static class DrawingObjectMinimumSizePlanner
{
    public static double MinimumWidth(DrawingObjectMinimumSizeKind kind) =>
        kind switch
        {
            DrawingObjectMinimumSizeKind.Shape => 8,
            DrawingObjectMinimumSizeKind.Picture => 24,
            DrawingObjectMinimumSizeKind.TextBox => 24,
            DrawingObjectMinimumSizeKind.Chart => 24,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown drawing object kind.")
        };

    public static double MinimumHeight(DrawingObjectMinimumSizeKind kind) =>
        kind switch
        {
            DrawingObjectMinimumSizeKind.Shape => 8,
            DrawingObjectMinimumSizeKind.Picture => 18,
            DrawingObjectMinimumSizeKind.TextBox => 18,
            DrawingObjectMinimumSizeKind.Chart => 18,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown drawing object kind.")
        };
}
