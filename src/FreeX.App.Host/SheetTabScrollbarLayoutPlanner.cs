namespace FreeX.App.Host;

public readonly record struct SheetTabScrollbarLayout(double SheetTabsViewportWidth, double HorizontalScrollbarWidth);

public static class SheetTabScrollbarLayoutPlanner
{
    public const double MinimumSheetTabsViewportWidth = 80.0;
    public const double PreferredHorizontalScrollbarMinimumWidth = 180.0;
    public const double PreferredHorizontalScrollbarRatio = 0.36;
    public const double PreferredHorizontalScrollbarMaxWidth = 420.0;

    public static SheetTabScrollbarLayout Plan(double tabContentWidth, double rowHeaderWidth, double rowWidth)
    {
        var available = Math.Max(0, rowWidth - rowHeaderWidth);
        if (available <= 0)
            return new SheetTabScrollbarLayout(0, 0);

        var minimumTabsWidth = Math.Min(MinimumSheetTabsViewportWidth, available);
        var preferredScrollbarWidth = Math.Clamp(
            available * PreferredHorizontalScrollbarRatio,
            PreferredHorizontalScrollbarMinimumWidth,
            PreferredHorizontalScrollbarMaxWidth);

        if (tabContentWidth + preferredScrollbarWidth <= available)
            return new SheetTabScrollbarLayout(Math.Max(0, tabContentWidth), preferredScrollbarWidth);

        if (available - preferredScrollbarWidth >= minimumTabsWidth)
            return new SheetTabScrollbarLayout(available - preferredScrollbarWidth, preferredScrollbarWidth);

        return new SheetTabScrollbarLayout(minimumTabsWidth, available - minimumTabsWidth);
    }
}
