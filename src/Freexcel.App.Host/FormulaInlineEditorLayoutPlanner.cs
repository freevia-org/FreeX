using System.Windows;

namespace Freexcel.App.Host;

public sealed record FormulaInlineEditorLayout(Rect EditorRect, Rect TextOverlayRect);

public static class FormulaInlineEditorLayoutPlanner
{
    public static FormulaInlineEditorLayout Create(double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var editorRect = new Rect(
            cellLeft - 2,
            cellTop - 2,
            cellWidth + 4,
            Math.Max(cellHeight + 4, 20));

        var textOverlayRect = new Rect(
            cellLeft + 1,
            cellTop + 1,
            Math.Max(cellWidth - 2, 0),
            Math.Max(cellHeight, 18));

        return new FormulaInlineEditorLayout(editorRect, textOverlayRect);
    }
}
