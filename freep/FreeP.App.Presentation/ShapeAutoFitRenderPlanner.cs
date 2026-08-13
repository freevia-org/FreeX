using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct ShapeAutoFitMeasurementRequest(
    int ParagraphIndex,
    ResolvedParagraph Paragraph,
    double MaximumWidthDip,
    bool Wrap,
    TextAutoFitKind AutoFitKind);

public readonly record struct ShapeAutoFitRenderPlan(
    LayoutRect Bounds,
    ShapeAffineTransform RenderTransform,
    ShapeAffineTransform GeometryTransform);

/// <summary>Coordinates shape auto-fit while leaving glyph measurement with the native host.</summary>
public static class ShapeAutoFitRenderPlanner
{
    private const double GrowthTolerance = 0.5;
    private const double MinimumScalableHeight = 0.001;

    public static LayoutRect Plan(
        DrawOp.Shape shape,
        Func<ShapeAutoFitMeasurementRequest, double> measureParagraphHeight)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(measureParagraphHeight);

        var text = shape.Text;
        var bounds = shape.BoundsDip;
        if (!IsEligible(shape))
            return bounds;

        var area = TextLayoutPlanner.GetTextArea(text!, bounds);
        var measures = new List<TextParagraphMeasure>();
        for (int index = 0; index < text!.Paragraphs.Count; index++)
        {
            var paragraph = text.Paragraphs[index];
            if (paragraph.Runs.Count == 0)
                continue;

            double height = measureParagraphHeight(new ShapeAutoFitMeasurementRequest(
                index,
                paragraph,
                area.Width,
                text.Wrap,
                text.AutoFitKind));
            measures.Add(TextLayoutPlanner.CreateParagraphMeasure(
                index,
                height,
                paragraph.SpaceBeforePt,
                paragraph.SpaceAfterPt));
        }

        return TextLayoutPlanner.PlanShapeAutoFitBounds(text, bounds, measures);
    }

    public static ShapeAutoFitRenderPlan PlanRender(
        DrawOp.Shape shape,
        Func<ShapeAutoFitMeasurementRequest, double> measureParagraphHeight)
    {
        var sourceBounds = shape.BoundsDip;
        var bounds = Plan(shape, measureParagraphHeight);
        bool grew = bounds.Height > sourceBounds.Height + GrowthTolerance;
        var renderTransform = grew
            ? ShapeAffineTransform.Identity
            : ShapeTransformPlanner.PlanShapeRenderTransform(shape);

        var geometryTransform = ShapeAffineTransform.Identity;
        if (grew && sourceBounds.Height > MinimumScalableHeight)
        {
            double scaleY = bounds.Height / sourceBounds.Height;
            geometryTransform = new ShapeAffineTransform(
                1,
                0,
                0,
                scaleY,
                0,
                bounds.Y - scaleY * sourceBounds.Y);
        }

        return new ShapeAutoFitRenderPlan(bounds, renderTransform, geometryTransform);
    }

    public static bool IsEligible(DrawOp.Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.Text is { } text
            && text.AutoFitKind == TextAutoFitKind.Shape
            && text.ColumnCount <= 1
            && text.VerticalType == TextVerticalType.Horizontal
            && Math.Abs(shape.RotationDeg) <= 0.001
            && !shape.FlipH
            && !shape.FlipV;
    }
}
