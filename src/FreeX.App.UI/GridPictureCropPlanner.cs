using System.Windows;

namespace FreeX.App.UI;

public enum PictureCropHandle
{
    None,
    CropNW,
    CropN,
    CropNE,
    CropE,
    CropSE,
    CropS,
    CropSW,
    CropW
}

public readonly record struct PictureCropRatios(double Left, double Top, double Right, double Bottom);

public static class GridPictureCropPlanner
{
    public const double MinimumVisibleFraction = 0.01;
    public const double DefaultHandleOffset = 10;
    public const double DefaultHandleSize = 10;
    public const double DefaultHandleHitPadding = 3;

    public static PictureCropHandle HitTestHandle(
        Point position,
        Rect pictureRect,
        double handleOffset = DefaultHandleOffset,
        double handleSize = DefaultHandleSize,
        double handleHitPadding = DefaultHandleHitPadding)
    {
        if (pictureRect.IsEmpty || pictureRect.Width <= 0 || pictureRect.Height <= 0)
            return PictureCropHandle.None;

        var hitRadius = handleSize / 2 + handleHitPadding;
        foreach (var (handle, center) in GetHandleCenters(pictureRect, handleOffset))
        {
            if (Math.Abs(position.X - center.X) <= hitRadius &&
                Math.Abs(position.Y - center.Y) <= hitRadius)
            {
                return handle;
            }
        }

        return PictureCropHandle.None;
    }

    public static PictureCropRatios CalculateCrop(
        PictureCropHandle handle,
        PictureCropRatios startCrop,
        Rect pictureRect,
        Point startPosition,
        Point currentPosition,
        double minimumVisibleFraction = MinimumVisibleFraction)
    {
        if (handle == PictureCropHandle.None ||
            pictureRect.IsEmpty ||
            pictureRect.Width <= 0 ||
            pictureRect.Height <= 0)
        {
            return startCrop;
        }

        var dx = (currentPosition.X - startPosition.X) / pictureRect.Width;
        var dy = (currentPosition.Y - startPosition.Y) / pictureRect.Height;
        var left = startCrop.Left;
        var top = startCrop.Top;
        var right = startCrop.Right;
        var bottom = startCrop.Bottom;

        var movesLeft = handle is PictureCropHandle.CropNW or PictureCropHandle.CropW or PictureCropHandle.CropSW;
        var movesRight = handle is PictureCropHandle.CropNE or PictureCropHandle.CropE or PictureCropHandle.CropSE;
        var movesTop = handle is PictureCropHandle.CropNW or PictureCropHandle.CropN or PictureCropHandle.CropNE;
        var movesBottom = handle is PictureCropHandle.CropSW or PictureCropHandle.CropS or PictureCropHandle.CropSE;

        if (movesLeft)
            left += dx;
        if (movesRight)
            right -= dx;
        if (movesTop)
            top += dy;
        if (movesBottom)
            bottom -= dy;

        (left, right) = ClampAxis(left, right, movesLeft, movesRight, minimumVisibleFraction);
        (top, bottom) = ClampAxis(top, bottom, movesTop, movesBottom, minimumVisibleFraction);
        return new PictureCropRatios(left, top, right, bottom);
    }

    public static Rect CalculateVisibleCropRect(Rect pictureRect, PictureCropRatios crop)
    {
        var left = pictureRect.Left + pictureRect.Width * crop.Left;
        var top = pictureRect.Top + pictureRect.Height * crop.Top;
        var right = pictureRect.Right - pictureRect.Width * crop.Right;
        var bottom = pictureRect.Bottom - pictureRect.Height * crop.Bottom;
        return new Rect(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    public static IReadOnlyList<(PictureCropHandle Handle, Point Center)> GetHandleCenters(
        Rect pictureRect,
        double handleOffset = DefaultHandleOffset)
    {
        if (pictureRect.IsEmpty || pictureRect.Width <= 0 || pictureRect.Height <= 0)
            return [];

        var offset = ResolveHandleOffset(pictureRect, handleOffset);
        var centerX = pictureRect.Left + pictureRect.Width / 2;
        var centerY = pictureRect.Top + pictureRect.Height / 2;
        return
        [
            (PictureCropHandle.CropNW, new Point(pictureRect.Left + offset, pictureRect.Top + offset)),
            (PictureCropHandle.CropN, new Point(centerX, pictureRect.Top + offset)),
            (PictureCropHandle.CropNE, new Point(pictureRect.Right - offset, pictureRect.Top + offset)),
            (PictureCropHandle.CropE, new Point(pictureRect.Right - offset, centerY)),
            (PictureCropHandle.CropSE, new Point(pictureRect.Right - offset, pictureRect.Bottom - offset)),
            (PictureCropHandle.CropS, new Point(centerX, pictureRect.Bottom - offset)),
            (PictureCropHandle.CropSW, new Point(pictureRect.Left + offset, pictureRect.Bottom - offset)),
            (PictureCropHandle.CropW, new Point(pictureRect.Left + offset, centerY))
        ];
    }

    private static double ResolveHandleOffset(Rect pictureRect, double requestedOffset)
    {
        var maxInset = Math.Min(pictureRect.Width, pictureRect.Height) / 4;
        return Math.Max(0, Math.Min(requestedOffset, maxInset));
    }

    private static (double Leading, double Trailing) ClampAxis(
        double leading,
        double trailing,
        bool movesLeading,
        bool movesTrailing,
        double minimumVisibleFraction)
    {
        leading = NormalizeCrop(leading);
        trailing = NormalizeCrop(trailing);
        var maxTotal = Math.Clamp(1 - minimumVisibleFraction, 0, 1);
        if (leading + trailing <= maxTotal)
            return (leading, trailing);

        if (movesLeading && !movesTrailing)
            return (Math.Max(0, maxTotal - trailing), trailing);
        if (movesTrailing && !movesLeading)
            return (leading, Math.Max(0, maxTotal - leading));

        var scale = maxTotal / (leading + trailing);
        return (leading * scale, trailing * scale);
    }

    private static double NormalizeCrop(double value)
    {
        if (!double.IsFinite(value) || value < 0)
            return 0;

        return Math.Round(value, 12);
    }
}
