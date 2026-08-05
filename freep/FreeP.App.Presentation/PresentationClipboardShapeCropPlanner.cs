using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct PresentationClipboardPixelCrop(
    int X,
    int Y,
    int Width,
    int Height)
{
    public bool IsFullFrame(int frameWidth, int frameHeight) =>
        X == 0 && Y == 0 && Width == frameWidth && Height == frameHeight;
}

/// <summary>
/// Converts the selected shape envelope from presentation EMUs to an outer,
/// frame-clamped pixel crop shared by native clipboard renderers.
/// </summary>
public static class PresentationClipboardShapeCropPlanner
{
    public static PresentationClipboardPixelCrop Plan(
        Presentation presentation,
        IReadOnlyList<SlideShape> shapes,
        int frameWidth,
        int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);

        if (shapes.Count == 0 ||
            presentation.SlideSizeCxEmu <= 0 ||
            presentation.SlideSizeCyEmu <= 0)
        {
            return FullFrame(frameWidth, frameHeight);
        }

        var scaleX = frameWidth / (double)presentation.SlideSizeCxEmu;
        var scaleY = frameHeight / (double)presentation.SlideSizeCyEmu;
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var shape in shapes)
        {
            var firstX = shape.OffsetXEmu * scaleX;
            var secondX = (shape.OffsetXEmu + (double)shape.ExtentCxEmu) * scaleX;
            var firstY = shape.OffsetYEmu * scaleY;
            var secondY = (shape.OffsetYEmu + (double)shape.ExtentCyEmu) * scaleY;
            left = Math.Min(left, Math.Min(firstX, secondX));
            top = Math.Min(top, Math.Min(firstY, secondY));
            right = Math.Max(right, Math.Max(firstX, secondX));
            bottom = Math.Max(bottom, Math.Max(firstY, secondY));
        }

        var x = ClampFloor(left, frameWidth - 1);
        var y = ClampFloor(top, frameHeight - 1);
        var rightExclusive = ClampCeiling(right, x + 1, frameWidth);
        var bottomExclusive = ClampCeiling(bottom, y + 1, frameHeight);
        return new PresentationClipboardPixelCrop(
            x,
            y,
            rightExclusive - x,
            bottomExclusive - y);
    }

    private static PresentationClipboardPixelCrop FullFrame(int width, int height) =>
        new(0, 0, width, height);

    private static int ClampFloor(double value, int maximum) =>
        (int)Math.Clamp(Math.Floor(value), 0, maximum);

    private static int ClampCeiling(double value, int minimum, int maximum) =>
        (int)Math.Clamp(Math.Ceiling(value), minimum, maximum);
}
