using System.Globalization;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>A shape's portable geometry and appearance on a print page.</summary>
public sealed record PageShapeBlock(
    Guid Id,
    LayoutRect Bounds,
    ShapeGeometry Geometry,
    DrawingShapeRenderMetadata Appearance,
    string? Text,
    PageTextFont TextFont,
    DrawingShapeTextHAlign TextHorizontalAlignment,
    DrawingShapeTextVAnchor TextVerticalAlignment,
    bool TextWrap)
{
    public LayoutRect BodyClip { get; init; }
    public IReadOnlyList<ShapeGeometry> Arrowheads { get; init; } = [];
    public bool BlackAndWhite { get; init; }
}

/// <summary>Resolves printable drawing shapes into page-space geometry.</summary>
public static class PageShapeLayoutPlanner
{
    public static IReadOnlyList<PageShapeBlock> Build(
        IReadOnlyList<DrawingShapeModel> shapes,
        Sheet sheet,
        WorkbookTheme theme,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement,
        double scaleRatio = 1,
        IReadOnlyList<uint>? bodyRows = null,
        IReadOnlyList<uint>? bodyColumns = null,
        bool uniformGrid = false)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(pageRows);
        ArgumentNullException.ThrowIfNull(pageColumns);
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(sheet);
        if (!double.IsFinite(scaleRatio) || scaleRatio <= 0) return [];

        bodyRows ??= pageRows;
        bodyColumns ??= pageColumns;
        if (bodyRows.Count == 0 || bodyColumns.Count == 0) return [];
        var titleRows = pageRows.Count - bodyRows.Count;
        var titleColumns = pageColumns.Count - bodyColumns.Count;
        var bodyLeft = gridLeft + measurement.ColumnOffset(titleColumns);
        var bodyTop = gridTop + measurement.RowOffset(titleRows);
        var bodyWidth = measurement.ColumnOffset(titleColumns + bodyColumns.Count) - measurement.ColumnOffset(titleColumns);
        var bodyHeight = measurement.RowOffset(titleRows + bodyRows.Count) - measurement.RowOffset(titleRows);
        var pageColumnOffset = uniformGrid ? 0 : ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet,
            ChartAnchorGeometry.SumColumnPixels(sheet, 1, bodyColumns[0] - 1));
        var pageRowOffset = uniformGrid ? 0 : ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet,
            ChartAnchorGeometry.SumRowPixels(sheet, 1, bodyRows[0] - 1));
        var result = new List<PageShapeBlock>();
        var columnPositions = new Dictionary<uint, double>();
        var rowPositions = new Dictionary<uint, double>();
        foreach (var shape in shapes)
        {
            if (!shape.IsVisible || !shape.PrintObject || shape.Anchor.Row == 0 || shape.Anchor.Col == 0 ||
                !double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) ||
                !double.IsFinite(shape.AnchorOffsetX) || !double.IsFinite(shape.AnchorOffsetY) ||
                !double.IsFinite(shape.RotationDegrees))
                continue;
            var width = Math.Max(0, shape.Width);
            var height = Math.Max(0, shape.Height);
            LayoutRect bounds;
            if (uniformGrid)
                bounds = new LayoutRect(bodyLeft + VisibleDistance(bodyColumns[0], shape.Anchor.Col, sheet.IsColEffectivelyHidden) * measurement.ColumnWidth + shape.AnchorOffsetX * scaleRatio,
                    bodyTop + VisibleDistance(bodyRows[0], shape.Anchor.Row, sheet.IsRowEffectivelyHidden) * measurement.RowHeight + shape.AnchorOffsetY * scaleRatio,
                    width * scaleRatio, height * scaleRatio);
            else
            {
                if (!columnPositions.TryGetValue(shape.Anchor.Col, out var columnPosition))
                {
                    var anchor = ChartAnchorGeometry.SumColumnPixels(sheet, 1, shape.Anchor.Col - 1);
                    columnPositions[shape.Anchor.Col] = columnPosition = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, anchor);
                }
                if (!rowPositions.TryGetValue(shape.Anchor.Row, out var rowPosition))
                {
                    var anchor = ChartAnchorGeometry.SumRowPixels(sheet, 1, shape.Anchor.Row - 1);
                    rowPositions[shape.Anchor.Row] = rowPosition = ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet, anchor);
                }
                var x = columnPosition + shape.AnchorOffsetX;
                var y = rowPosition + shape.AnchorOffsetY;
                bounds = new LayoutRect(bodyLeft + (x - pageColumnOffset) * scaleRatio,
                    bodyTop + (y - pageRowOffset) * scaleRatio, width * scaleRatio, height * scaleRatio);
            }
            if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)) continue;
            // Conservative rotated AABB: retain every shape that could intersect the body clip.
            var radians = shape.RotationDegrees * Math.PI / 180;
            var rotatedWidth = Math.Abs(bounds.Width * Math.Cos(radians)) + Math.Abs(bounds.Height * Math.Sin(radians));
            var rotatedHeight = Math.Abs(bounds.Width * Math.Sin(radians)) + Math.Abs(bounds.Height * Math.Cos(radians));
            var centerX = bounds.Left + bounds.Width / 2;
            var centerY = bounds.Top + bounds.Height / 2;
            if (!double.IsFinite(rotatedWidth) || !double.IsFinite(rotatedHeight) ||
                !double.IsFinite(centerX) || !double.IsFinite(centerY)) continue;
            if (centerX + rotatedWidth / 2 < bodyLeft || centerX - rotatedWidth / 2 > bodyLeft + bodyWidth ||
                centerY + rotatedHeight / 2 < bodyTop || centerY - rotatedHeight / 2 > bodyTop + bodyHeight)
                continue;
            var appearance = DrawingObjectRenderMetadataPlanner.ResolveDrawingShapeRenderMetadata(shape, theme);
            var thickness = appearance.Outline.ThicknessDip * scaleRatio;
            if (!double.IsFinite(thickness)) continue;
            appearance = appearance with { Outline = appearance.Outline with { ThicknessDip = thickness } };
            var textColor = shape.ResolveShapeTextColor(theme) ?? new CellColor(0, 0, 0);
            var fontSize = double.IsFinite(shape.ShapeTextFontSizePoints) && shape.ShapeTextFontSizePoints > 0
                ? shape.ShapeTextFontSizePoints * (96.0 / 72.0) : PageContentRenderModelBuilder.PrintFontSize;
            if (!double.IsFinite(fontSize * scaleRatio)) continue;
            result.Add(new PageShapeBlock(shape.Id, bounds,
                ShapeGeometryBuilder.Build(shape.Kind, bounds, ResolveAdjustments(shape.AdjustValues)),
                appearance, shape.ShapeText,
                new PageTextFont(PageContentRenderModelBuilder.PrintFontFamily, fontSize * scaleRatio,
                    shape.ShapeTextBold, shape.ShapeTextItalic, PresentationRgb.FromCellColor(textColor)),
                shape.ShapeTextHAlign, shape.ShapeTextVAnchor, shape.ShapeTextWrap)
            {
                BodyClip = new LayoutRect(bodyLeft, bodyTop, bodyWidth, bodyHeight),
                Arrowheads = BuildArrowheads(shape, bounds, appearance, scaleRatio),
                BlackAndWhite = sheet.PrintBlackAndWhite
            });
        }
        return result;
    }

    private static int VisibleDistance(uint origin, uint target, Func<uint, bool> isHidden)
    {
        var lower = Math.Min(origin, target);
        var upper = Math.Max(origin, target);
        var count = 0;
        for (var index = lower; index < upper; index++)
            if (!isHidden(index)) count++;
        return target < origin ? -count : count;
    }

    private static IReadOnlyList<ShapeGeometry> BuildArrowheads(DrawingShapeModel shape, LayoutRect bounds, DrawingShapeRenderMetadata appearance, double scaleRatio)
    {
        if (!appearance.IsLineLike || !appearance.Outline.HasOutline || !appearance.HasArrowheads) return [];
        var (start, end, direction) = ArrowheadGeometry.LineEndpoints(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            false, false, shape.Kind);
        var result = new List<ShapeGeometry>(2);
        Add(shape.HeadArrowhead, start, direction + Math.PI);
        Add(shape.TailArrowhead, end, direction);
        return result;

        void Add(DrawingArrowhead? arrow, LayoutPoint tip, double angle)
        {
            if (arrow?.IsPresent != true) return;
            if (arrow.Type == DrawingArrowheadType.Oval)
            {
                var (localCenter, localRadius) = ArrowheadGeometry.OvalCenter(arrow, new LayoutPoint(0, 0), angle, appearance.Outline.ThicknessDip / scaleRatio);
                var center = new LayoutPoint(tip.X + localCenter.X * scaleRatio, tip.Y + localCenter.Y * scaleRatio);
                var radius = localRadius * scaleRatio;
                result.Add(ShapeGeometryBuilder.Build(DrawingShapeKind.Ellipse,
                    new LayoutRect(center.X - radius, center.Y - radius, radius * 2, radius * 2)));
                return;
            }
            var points = ArrowheadGeometry.PolygonPoints(arrow, new LayoutPoint(0, 0), angle, appearance.Outline.ThicknessDip / scaleRatio)
                .Select(point => new LayoutPoint(tip.X + point.X * scaleRatio, tip.Y + point.Y * scaleRatio)).ToArray();
            if (points.Length >= 3)
                result.Add(new ShapeGeometry([new ShapeContour(points[0], points.Skip(1).Select(ShapeSegment.LineTo).ToArray(), true, true)]));
        }
    }

    private static IReadOnlyDictionary<string, double>? ResolveAdjustments(IReadOnlyList<DrawingShapeAdjustValue>? values)
    {
        if (values is null || values.Count == 0) return null;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var formula = value.Formula.AsSpan().Trim();
            if (formula.StartsWith("val ".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(formula[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                result[value.Name] = number;
        }
        return result;
    }
}
