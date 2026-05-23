using System.Windows;
using System.Windows.Media;

using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public sealed record ConditionalIconCellLayout(Rect IconRect, Rect TextRect, bool ShouldDrawText);

public enum ConditionalIconGlyphKind
{
    Arrow,
    TrafficLight,
    Sign,
    Symbol,
    Flag,
    Rating,
    Quarter,
    Box
}

public partial class GridView
{
    public static ConditionalIconCellLayout CalculateConditionalIconCellLayout(
        Rect cellRect,
        ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.CalculateCellLayout(cellRect, icon);

    public static bool ShouldDrawCellContent(DisplayCell cell, CellAddress? editingCell)
    {
        if (editingCell is { } address && address.Row == cell.Row && address.Col == cell.Col)
            return false;

        return !string.IsNullOrEmpty(cell.DisplayText) || cell.ConditionalIcon is not null;
    }

    public static HashSet<(uint Row, uint Col)> BuildOccupiedCellSet(IEnumerable<DisplayCell> cells, CellAddress? editingCell)
    {
        var occupied = new HashSet<(uint Row, uint Col)>(
            cells
                .Where(c => !string.IsNullOrEmpty(c.DisplayText) || c.ConditionalIcon is not null)
                .Select(c => (c.Row, c.Col)));

        if (editingCell is { } address)
            occupied.Add((address.Row, address.Col));

        return occupied;
    }

    private static void DrawConditionalIcon(DrawingContext dc, ConditionalFormatIcon icon, Rect rect) =>
        ConditionalIconGlyphRenderer.Draw(dc, icon, rect);

    public static ConditionalIconGlyphKind ResolveConditionalIconGlyphKind(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveGlyphKind(icon);

    public static string ResolveConditionalIconColor(ConditionalFormatIcon icon) =>
        ConditionalIconLayoutPlanner.ResolveColor(icon);
}
