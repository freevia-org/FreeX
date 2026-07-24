namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// Pure pointer mechanics shared by the sheet-tab hosts. Keeping the midpoint and scroll
/// calculations out of the platform event handlers makes the WPF and Avalonia routes agree on
/// the same boundary behavior.
/// </summary>
public static class SheetTabPointerPlanner
{
    public static int CalculateDropIndex(int fromIndex, int targetIndex, bool insertAfterTarget)
    {
        if (fromIndex < 0 || targetIndex < 0)
            return -1;

        var insertBeforeIndex = insertAfterTarget ? targetIndex + 1 : targetIndex;
        return fromIndex < insertBeforeIndex
            ? insertBeforeIndex - 1
            : insertBeforeIndex;
    }

    public static double CalculateHorizontalScrollOffset(
        double currentOffset,
        double extentWidth,
        double viewportWidth,
        double delta)
    {
        var maximumOffset = Math.Max(0, extentWidth - viewportWidth);
        return Math.Clamp(currentOffset + delta, 0, maximumOffset);
    }

    public static bool CanScrollLeft(double currentOffset) => currentOffset > 0.5;

    public static bool CanScrollRight(double currentOffset, double extentWidth, double viewportWidth) =>
        currentOffset < Math.Max(0, extentWidth - viewportWidth) - 0.5;
}
