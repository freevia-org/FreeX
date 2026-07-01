namespace FreeX.App.Presentation.SheetUI;

public static class SheetTabViewportScrollPlanner
{
    public const double DefaultEpsilon = 0.5;

    public static double CalculateOffsetForSelectedTab(
        double currentOffset,
        double selectedTabViewportLeft,
        double selectedTabViewportRight,
        double visibleViewportRight,
        double scrollableWidth,
        double epsilon = DefaultEpsilon)
    {
        if (visibleViewportRight <= epsilon ||
            selectedTabViewportRight <= selectedTabViewportLeft)
        {
            return ClampOffset(currentOffset, scrollableWidth);
        }

        var targetOffset = currentOffset;
        if (selectedTabViewportLeft < -epsilon)
        {
            targetOffset = currentOffset + selectedTabViewportLeft;
        }
        else if (selectedTabViewportRight > visibleViewportRight + epsilon)
        {
            targetOffset = currentOffset + selectedTabViewportRight - visibleViewportRight;
        }

        return ClampOffset(targetOffset, scrollableWidth);
    }

    private static double ClampOffset(double offset, double scrollableWidth) =>
        Math.Clamp(offset, 0, Math.Max(0, scrollableWidth));
}
