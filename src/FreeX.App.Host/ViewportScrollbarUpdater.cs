using System.Windows.Controls.Primitives;

namespace FreeX.App.Host;

internal static class ViewportScrollbarUpdater
{
    public static bool TryExtendFromArrowSmallIncrement(ScrollBar scrollBar, uint absoluteLimit)
    {
        var (maximum, value) = ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
            scrollBar.Value,
            scrollBar.Maximum,
            scrollBar.SmallChange,
            scrollBar.ViewportSize,
            absoluteLimit);
        if (maximum <= scrollBar.Maximum && value <= scrollBar.Value)
            return false;

        scrollBar.Maximum = maximum;
        scrollBar.Value = value;
        return true;
    }
}
