using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public readonly record struct ScreenPixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ScreenClipPoint(double X, double Y);

public readonly record struct ScreenClipSelectionBounds(
    double Left,
    double Top,
    double Width,
    double Height);

public readonly record struct ScreenClipSelectionUpdate(
    ScreenClipPoint Origin,
    ScreenClipPoint Current,
    ScreenClipSelectionBounds Bounds);

/// <summary>Owns the toolkit-neutral pointer lifecycle for a screen-clipping drag.</summary>
public sealed class ScreenClipSelectionSession
{
    private ScreenClipPoint? _origin;

    public bool IsDragging => _origin is not null;

    public ScreenClipSelectionUpdate Begin(double x, double y)
    {
        var origin = Point(x, y);
        _origin = origin;
        return BuildUpdate(origin, origin);
    }

    public ScreenClipSelectionUpdate? Update(double x, double y) =>
        _origin is { } origin
            ? BuildUpdate(origin, Point(x, y))
            : null;

    public ScreenClipSelectionUpdate? Complete(double x, double y)
    {
        if (_origin is not { } origin)
            return null;

        _origin = null;
        return BuildUpdate(origin, Point(x, y));
    }

    public void Cancel() => _origin = null;

    private static ScreenClipSelectionUpdate BuildUpdate(ScreenClipPoint origin, ScreenClipPoint current) =>
        new(
            origin,
            current,
            new ScreenClipSelectionBounds(
                Math.Min(origin.X, current.X),
                Math.Min(origin.Y, current.Y),
                Math.Abs(current.X - origin.X),
                Math.Abs(current.Y - origin.Y)));

    private static ScreenClipPoint Point(double x, double y)
    {
        if (!double.IsFinite(x))
            throw new ArgumentOutOfRangeException(nameof(x));
        if (!double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(y));
        return new ScreenClipPoint(x, y);
    }
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
