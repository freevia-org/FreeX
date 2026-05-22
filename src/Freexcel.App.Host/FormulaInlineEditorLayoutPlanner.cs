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
    private const double TextSurfaceTrailingBuffer = 16;
    private const double SelectionLikeBorderThickness = 1;
    private const double HiddenBorderCover = 2;

    public static FormulaInlineEditorLayout Create(
        double cellLeft,
        double cellTop,
        double cellWidth,
        double cellHeight,
        double desiredTextWidth = 0,
        double availableRight = double.PositiveInfinity)
    {
        var editorRect = new Rect(
            cellLeft,
            cellTop,
            cellWidth,
            cellHeight);

        var textLeft = editorRect.Left + 4;
        var textWidth = Math.Max(
            Math.Max(editorRect.Width - 8, MinimumTextSurfaceWidth),
            desiredTextWidth + TextSurfaceTrailingBuffer);

        if (double.IsFinite(availableRight))
            textWidth = Math.Min(textWidth, Math.Max(0, availableRight - textLeft));

        var textOverlayRect = new Rect(
            textLeft,
            editorRect.Top,
            textWidth,
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
