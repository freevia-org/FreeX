using System.Windows;

namespace Freexcel.App.Host;

public sealed record FormulaInlineEditorLayout(Rect EditorRect, Rect TextOverlayRect);
public readonly record struct FormulaInlineEditorOverflow(bool Left, bool Right)
{
    public static FormulaInlineEditorOverflow None => new(false, false);
}

public static class FormulaInlineEditorLayoutPlanner
{
    private const double MinimumTextSurfaceWidth = 160;
    private const double SelectionLikeBorderThickness = 1;
    private const double HiddenBorderCover = 2;

    public static FormulaInlineEditorLayout Create(double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var editorRect = new Rect(
            cellLeft,
            cellTop,
            cellWidth,
            cellHeight);

        var textOverlayRect = new Rect(
            editorRect.Left + 4,
            editorRect.Top,
            Math.Max(editorRect.Width - 8, MinimumTextSurfaceWidth),
            editorRect.Height);

        return new FormulaInlineEditorLayout(editorRect, textOverlayRect);
    }

    public static Rect GetChromeRect(Rect editorRect, FormulaInlineEditorOverflow overflow)
    {
        var left = editorRect.Left;
        var width = editorRect.Width;

        if (overflow.Left)
        {
            left -= HiddenBorderCover;
            width += HiddenBorderCover;
        }

        if (overflow.Right)
            width += HiddenBorderCover;

        return new Rect(left, editorRect.Top, width, editorRect.Height);
    }

    public static Thickness GetChromeBorderThickness(FormulaInlineEditorOverflow overflow) =>
        new(
            overflow.Left ? 0 : SelectionLikeBorderThickness,
            SelectionLikeBorderThickness,
            overflow.Right ? 0 : SelectionLikeBorderThickness,
            SelectionLikeBorderThickness);
}
