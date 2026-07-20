namespace FreeW.App.Presentation.Dialogs;

public readonly record struct ScreenPixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ScreenClipDisplaySize(
    double WidthPt,
    double HeightPt,
    int OriginalPixelWidth,
    int OriginalPixelHeight);

/// <summary>
/// Toolkit-neutral geometry and image-sizing policy for the screen-clipping surface.
/// </summary>
public static class ScreenClipPlanner
{
    public const double MaxWidthPt = 400;

    public static ScreenPixelRect? BuildPhysicalSelection(
        double startX,
        double startY,
        double endX,
        double endY,
        int overlayOriginX,
        int overlayOriginY,
        double renderScale)
    {
        if (!double.IsFinite(renderScale) || renderScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderScale));

        var left = Math.Min(startX, endX);
        var top = Math.Min(startY, endY);
        var right = Math.Max(startX, endX);
        var bottom = Math.Max(startY, endY);

        var x = overlayOriginX + (int)Math.Round(left * renderScale);
        var y = overlayOriginY + (int)Math.Round(top * renderScale);
        var width = (int)Math.Round((right - left) * renderScale);
        var height = (int)Math.Round((bottom - top) * renderScale);
        return width <= 0 || height <= 0
            ? null
            : new ScreenPixelRect(x, y, width, height);
    }

    public static ScreenClipDisplaySize BuildDisplaySize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var widthPt = pixelWidth * 72.0 / 96.0;
        var heightPt = pixelHeight * 72.0 / 96.0;
        if (widthPt > MaxWidthPt)
        {
            heightPt *= MaxWidthPt / widthPt;
            widthPt = MaxWidthPt;
        }

        return new ScreenClipDisplaySize(widthPt, heightPt, pixelWidth, pixelHeight);
    }
}
