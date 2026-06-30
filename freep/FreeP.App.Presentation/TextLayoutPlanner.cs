using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct TextLayoutArea(
    double X,
    double Y,
    double Width,
    double Height);

public readonly record struct TextParagraphMeasure(
    int ParagraphIndex,
    double HeightDip,
    double SpaceBeforeDip,
    double SpaceAfterDip)
{
    public double TotalHeightDip => HeightDip + SpaceBeforeDip + SpaceAfterDip;
}

public readonly record struct TextParagraphPlacement(
    int ParagraphIndex,
    int ColumnIndex,
    double X,
    double Y,
    double MaxWidthDip);

public readonly record struct TextColumnLayout(
    TextLayoutArea Area,
    int ColumnCount,
    double ColumnSpacingDip,
    double ColumnWidthDip,
    double LineSpacingScale);

public sealed record TextBlockLayoutPlan(
    TextLayoutArea Area,
    IReadOnlyList<TextParagraphPlacement> Paragraphs);

public static class TextLayoutPlanner
{
    public const double DipPerPoint = 96.0 / 72.0;
    public const double DefaultColumnSpacingDip = 48.5;

    public static double PointsToDip(double points) => points * DipPerPoint;

    public static TextLayoutArea GetTextArea(ResolvedTextLayout text, LayoutRect bounds)
    {
        double width = Math.Max(0, bounds.Width - text.InsetLeftDip - text.InsetRightDip);
        double height = Math.Max(0, bounds.Height - text.InsetTopDip - text.InsetBottomDip);

        return new TextLayoutArea(
            bounds.X + text.InsetLeftDip,
            bounds.Y + text.InsetTopDip,
            width,
            height);
    }

    public static double GetLineSpacingScale(ResolvedTextLayout text) =>
        1.0 - text.LnSpcReduction;

    public static TextParagraphMeasure CreateParagraphMeasure(
        int paragraphIndex,
        double heightDip,
        double spaceBeforePt,
        double spaceAfterPt,
        double lineSpacingScale = 1.0) =>
        new(
            paragraphIndex,
            heightDip * lineSpacingScale,
            PointsToDip(spaceBeforePt) * lineSpacingScale,
            PointsToDip(spaceAfterPt) * lineSpacingScale);

    public static TextBlockLayoutPlan PlanTableCellText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        TableCellAnchor anchor,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        var area = GetTextArea(text, bounds);
        double totalHeight = paragraphs.Sum(p => p.TotalHeightDip);
        double currentY = ComputeStartY(area, totalHeight, anchor);

        var placements = new List<TextParagraphPlacement>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            currentY += paragraph.SpaceBeforeDip;
            placements.Add(new TextParagraphPlacement(
                paragraph.ParagraphIndex,
                0,
                area.X,
                currentY,
                area.Width));
            currentY += paragraph.HeightDip + paragraph.SpaceAfterDip;
        }

        return new TextBlockLayoutPlan(area, placements);
    }

    public static TextColumnLayout GetColumnLayout(ResolvedTextLayout text, LayoutRect bounds)
    {
        var area = GetTextArea(text, bounds);
        int columnCount = Math.Max(1, text.ColumnCount);
        double spacingDip = text.ColumnSpacingDip > 0
            ? text.ColumnSpacingDip
            : DefaultColumnSpacingDip;
        double columnWidth = Math.Max(
            1,
            (area.Width - (columnCount - 1) * spacingDip) / columnCount);

        return new TextColumnLayout(
            area,
            columnCount,
            spacingDip,
            columnWidth,
            GetLineSpacingScale(text));
    }

    public static TextBlockLayoutPlan PlanColumns(
        ResolvedTextLayout text,
        TextColumnLayout layout,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        int column = 0;
        double currentY = layout.Area.Y;
        double columnX = layout.Area.X;
        double columnBottom = layout.Area.Y + layout.Area.Height;

        var placements = new List<TextParagraphPlacement>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            if ((uint)paragraph.ParagraphIndex >= (uint)text.Paragraphs.Count)
                continue;

            if (currentY + paragraph.TotalHeightDip > columnBottom &&
                column < layout.ColumnCount - 1)
            {
                column++;
                columnX = layout.Area.X + column * (layout.ColumnWidthDip + layout.ColumnSpacingDip);
                currentY = layout.Area.Y;
            }

            currentY += paragraph.SpaceBeforeDip;
            var resolvedParagraph = text.Paragraphs[paragraph.ParagraphIndex];
            double paragraphX = columnX + resolvedParagraph.IndentDip;
            placements.Add(new TextParagraphPlacement(
                paragraph.ParagraphIndex,
                column,
                paragraphX,
                currentY,
                Math.Max(1, layout.ColumnWidthDip - resolvedParagraph.IndentDip)));
            currentY += paragraph.HeightDip + paragraph.SpaceAfterDip;
        }

        return new TextBlockLayoutPlan(layout.Area, placements);
    }

    private static double ComputeStartY(
        TextLayoutArea area,
        double totalHeight,
        TableCellAnchor anchor) =>
        anchor switch
        {
            TableCellAnchor.Middle => area.Y + Math.Max(0, (area.Height - totalHeight) / 2),
            TableCellAnchor.Bottom => area.Y + Math.Max(0, area.Height - totalHeight),
            _ => area.Y
        };
}
