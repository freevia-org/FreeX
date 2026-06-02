using System.Windows;

namespace FreeX.App.UI;

internal static class RectHitTest
{
    public static bool ContainsInclusive(Rect rect, Point point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;
}
