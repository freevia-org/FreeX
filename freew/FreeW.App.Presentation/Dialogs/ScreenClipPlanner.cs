using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public readonly record struct ScreenPixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ScreenClipImageInsertionPlan(
    ImageFormat Format,
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

        return BuildNonEmptyPhysicalSelection(
            overlayOriginX + (int)Math.Round(left * renderScale),
            overlayOriginY + (int)Math.Round(top * renderScale),
            (int)Math.Round((right - left) * renderScale),
            (int)Math.Round((bottom - top) * renderScale));
    }

    public static ScreenPixelRect? BuildPhysicalSelectionFromMappedEndpoints(
        double startScreenX,
        double startScreenY,
        double endScreenX,
        double endScreenY)
    {
        if (!double.IsFinite(startScreenX))
            throw new ArgumentOutOfRangeException(nameof(startScreenX));
        if (!double.IsFinite(startScreenY))
            throw new ArgumentOutOfRangeException(nameof(startScreenY));
        if (!double.IsFinite(endScreenX))
            throw new ArgumentOutOfRangeException(nameof(endScreenX));
        if (!double.IsFinite(endScreenY))
            throw new ArgumentOutOfRangeException(nameof(endScreenY));

        var left = Math.Min(startScreenX, endScreenX);
        var top = Math.Min(startScreenY, endScreenY);
        var right = Math.Max(startScreenX, endScreenX);
        var bottom = Math.Max(startScreenY, endScreenY);

        var x = (int)Math.Round(left);
        var y = (int)Math.Round(top);
        var width = (int)Math.Round(right - left);
        var height = (int)Math.Round(bottom - top);
        return BuildNonEmptyPhysicalSelection(x, y, width, height);
    }

    public static ScreenClipImageInsertionPlan BuildImageInsertionPlan(int pixelWidth, int pixelHeight)
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

        return new ScreenClipImageInsertionPlan(
            ImageFormat.Png,
            widthPt,
            heightPt,
            pixelWidth,
            pixelHeight);
    }

    private static ScreenPixelRect? BuildNonEmptyPhysicalSelection(int x, int y, int width, int height) =>
        width <= 0 || height <= 0
            ? null
            : new ScreenPixelRect(x, y, width, height);
}
