namespace Free.Shared.Pdf;

/// <summary>
/// Fits one PDF content page into top-down destination bounds and maps its draw operations into
/// the destination page's PDF coordinate space.
/// </summary>
public static class PdfContentPagePlacement
{
    public static IReadOnlyList<PdfDrawOp> MapOps(
        PdfContentPage sourcePage,
        double destinationX,
        double destinationY,
        double destinationWidth,
        double destinationHeight,
        double destinationPageHeight)
    {
        ArgumentNullException.ThrowIfNull(sourcePage);
        if (sourcePage.WidthPoints <= 0 ||
            sourcePage.HeightPoints <= 0 ||
            destinationWidth <= 0 ||
            destinationHeight <= 0)
            return [];

        var scale = Math.Min(
            destinationWidth / sourcePage.WidthPoints,
            destinationHeight / sourcePage.HeightPoints);
        if (!double.IsFinite(scale) || scale <= 0)
            return [];

        var contentWidth = sourcePage.WidthPoints * scale;
        var contentHeight = sourcePage.HeightPoints * scale;
        var fittedX = destinationX + ((destinationWidth - contentWidth) / 2);
        var fittedTop = destinationY + ((destinationHeight - contentHeight) / 2);
        var fittedPdfBottom = destinationPageHeight - fittedTop - contentHeight;

        return sourcePage.Ops.SelectMany(MapOp).ToArray();

        IEnumerable<PdfDrawOp> MapOp(PdfDrawOp op)
        {
            switch (op)
            {
                case PdfFillRect fill:
                    yield return MapRect(fill, (x, y, width, height) =>
                        new PdfFillRect(x, y, width, height, fill.Color));
                    break;
                case PdfFillRectPattern fill:
                    yield return MapRect(fill, (x, y, width, height) =>
                        new PdfFillRectPattern(x, y, width, height, MapPattern(fill.Pattern)));
                    break;
                case PdfFillRectLinearGradient fill:
                    yield return MapRect(fill, (x, y, width, height) =>
                        new PdfFillRectLinearGradient(
                            x,
                            y,
                            width,
                            height,
                            MapGradient(fill.Gradient),
                            fill.FallbackColor));
                    break;
                case PdfStrokeRect stroke:
                    yield return MapRect(stroke, (x, y, width, height) =>
                        new PdfStrokeRect(
                            x,
                            y,
                            width,
                            height,
                            stroke.Color,
                            MapLength(stroke.LineWidth),
                            MapDash(stroke.Dash)));
                    break;
                case PdfStrokeRectLinearGradient stroke:
                    yield return MapRect(stroke, (x, y, width, height) =>
                        new PdfStrokeRectLinearGradient(
                            x,
                            y,
                            width,
                            height,
                            MapGradient(stroke.Gradient),
                            stroke.FallbackColor,
                            MapLength(stroke.LineWidth),
                            MapDash(stroke.Dash)));
                    break;
                case PdfFillEllipse fill:
                    yield return MapEllipse(fill, (x, y, width, height) =>
                        new PdfFillEllipse(x, y, width, height, fill.Color));
                    break;
                case PdfFillEllipsePattern fill:
                    yield return MapEllipse(fill, (x, y, width, height) =>
                        new PdfFillEllipsePattern(x, y, width, height, MapPattern(fill.Pattern)));
                    break;
                case PdfFillEllipseLinearGradient fill:
                    yield return MapEllipse(fill, (x, y, width, height) =>
                        new PdfFillEllipseLinearGradient(
                            x,
                            y,
                            width,
                            height,
                            MapGradient(fill.Gradient),
                            fill.FallbackColor));
                    break;
                case PdfStrokeEllipse stroke:
                    yield return MapEllipse(stroke, (x, y, width, height) =>
                        new PdfStrokeEllipse(
                            x,
                            y,
                            width,
                            height,
                            stroke.Color,
                            MapLength(stroke.LineWidth),
                            MapDash(stroke.Dash)));
                    break;
                case PdfStrokeEllipseLinearGradient stroke:
                    yield return MapEllipse(stroke, (x, y, width, height) =>
                        new PdfStrokeEllipseLinearGradient(
                            x,
                            y,
                            width,
                            height,
                            MapGradient(stroke.Gradient),
                            stroke.FallbackColor,
                            MapLength(stroke.LineWidth),
                            MapDash(stroke.Dash)));
                    break;
                case PdfText text:
                    yield return text with
                    {
                        X = MapX(text.X),
                        Y = MapY(text.Y),
                        FontSize = MapLength(text.FontSize),
                    };
                    break;
                case PdfLine line:
                    yield return line with
                    {
                        X1 = MapX(line.X1),
                        Y1 = MapY(line.Y1),
                        X2 = MapX(line.X2),
                        Y2 = MapY(line.Y2),
                        LineWidth = MapLength(line.LineWidth),
                    };
                    break;
                case PdfLineLinearGradient line:
                    yield return line with
                    {
                        X1 = MapX(line.X1),
                        Y1 = MapY(line.Y1),
                        X2 = MapX(line.X2),
                        Y2 = MapY(line.Y2),
                        Gradient = MapGradient(line.Gradient),
                        LineWidth = MapLength(line.LineWidth),
                    };
                    break;
                case PdfFilledTriangle triangle:
                    yield return triangle with
                    {
                        X1 = MapX(triangle.X1),
                        Y1 = MapY(triangle.Y1),
                        X2 = MapX(triangle.X2),
                        Y2 = MapY(triangle.Y2),
                        X3 = MapX(triangle.X3),
                        Y3 = MapY(triangle.Y3),
                    };
                    break;
                case PdfPath path:
                    yield return path with
                    {
                        Contours = MapContours(path.Contours),
                        StrokeWidth = MapLength(path.StrokeWidth),
                        StrokeDash = MapDash(path.StrokeDash),
                    };
                    break;
                case PdfPathPattern path:
                    yield return path with
                    {
                        Contours = MapContours(path.Contours),
                        Pattern = MapPattern(path.Pattern),
                        StrokeWidth = MapLength(path.StrokeWidth),
                        StrokeDash = MapDash(path.StrokeDash),
                    };
                    break;
                case PdfPathLinearGradient path:
                    yield return path with
                    {
                        Contours = MapContours(path.Contours),
                        FillGradient = path.FillGradient is { } fillGradient
                            ? MapGradient(fillGradient)
                            : null,
                        StrokeGradient = path.StrokeGradient is { } strokeGradient
                            ? MapGradient(strokeGradient)
                            : null,
                        StrokeWidth = MapLength(path.StrokeWidth),
                        StrokeDash = MapDash(path.StrokeDash),
                    };
                    break;
                case PdfRotationGroup group:
                {
                    var children = group.Ops.SelectMany(MapOp).ToArray();
                    if (children.Length > 0)
                    {
                        yield return group with
                        {
                            CenterX = MapX(group.CenterX),
                            CenterY = MapY(group.CenterY),
                            Ops = children,
                        };
                    }

                    break;
                }
                case PdfClipGroup group:
                {
                    var children = group.Ops.SelectMany(MapOp).ToArray();
                    if (children.Length > 0)
                    {
                        var bounds = MapBounds(group.X, group.Y, group.Width, group.Height);
                        yield return group with
                        {
                            X = bounds.X,
                            Y = bounds.Y,
                            Width = bounds.Width,
                            Height = bounds.Height,
                            Ops = children,
                        };
                    }

                    break;
                }
                case PdfOpacityGroup group:
                {
                    var children = group.Ops.SelectMany(MapOp).ToArray();
                    if (children.Length > 0)
                        yield return group with { Ops = children };
                    break;
                }
                case PdfEffectGroup group:
                {
                    var children = group.Ops.SelectMany(MapOp).ToArray();
                    if (children.Length > 0)
                    {
                        var bounds = MapBounds(
                            group.BoundsX,
                            group.BoundsY,
                            group.BoundsWidth,
                            group.BoundsHeight);
                        yield return group with
                        {
                            BoundsX = bounds.X,
                            BoundsY = bounds.Y,
                            BoundsWidth = bounds.Width,
                            BoundsHeight = bounds.Height,
                            Parameters = MapEffectParameters(group.Parameters),
                            Ops = children,
                        };
                    }

                    break;
                }
                case PdfImage image:
                {
                    var bounds = MapBounds(image.X, image.Y, image.Width, image.Height);
                    yield return image with
                    {
                        X = bounds.X,
                        Y = bounds.Y,
                        Width = bounds.Width,
                        Height = bounds.Height,
                    };
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported PDF draw operation: {op.GetType().FullName}");
            }
        }

        PdfDrawOp MapRect(PdfDrawOp op, Func<double, double, double, double, PdfDrawOp> factory)
        {
            var bounds = op switch
            {
                PdfFillRect value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfFillRectPattern value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfFillRectLinearGradient value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfStrokeRect value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfStrokeRectLinearGradient value => MapBounds(value.X, value.Y, value.Width, value.Height),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
            return factory(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        PdfDrawOp MapEllipse(PdfDrawOp op, Func<double, double, double, double, PdfDrawOp> factory)
        {
            var bounds = op switch
            {
                PdfFillEllipse value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfFillEllipsePattern value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfFillEllipseLinearGradient value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfStrokeEllipse value => MapBounds(value.X, value.Y, value.Width, value.Height),
                PdfStrokeEllipseLinearGradient value => MapBounds(value.X, value.Y, value.Width, value.Height),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };
            return factory(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        (double X, double Y, double Width, double Height) MapBounds(
            double x,
            double y,
            double width,
            double height) =>
            (MapX(x), MapY(y), MapLength(width), MapLength(height));

        PdfPathContour[] MapContours(IReadOnlyList<PdfPathContour> contours) =>
            contours.Select(contour => contour with
            {
                Start = MapPoint(contour.Start),
                Segments = contour.Segments.Select(MapSegment).ToArray(),
            }).ToArray();

        PdfPathSegment MapSegment(PdfPathSegment segment) =>
            segment.Kind switch
            {
                PdfPathSegmentKind.CubicBezier => PdfPathSegment.BezierTo(
                    MapPoint(segment.Control1),
                    MapPoint(segment.Control2),
                    MapPoint(segment.End)),
                _ => PdfPathSegment.LineTo(MapPoint(segment.End)),
            };

        PdfLinearGradient MapGradient(PdfLinearGradient gradient) =>
            gradient with
            {
                StartX = MapX(gradient.StartX),
                StartY = MapY(gradient.StartY),
                EndX = MapX(gradient.EndX),
                EndY = MapY(gradient.EndY),
            };

        PdfPatternFill MapPattern(PdfPatternFill pattern) =>
            pattern with { UnitScale = MapLength(pattern.UnitScale) };

        PdfDashPattern? MapDash(PdfDashPattern? dash) =>
            dash is null
                ? null
                : new PdfDashPattern(
                    dash.Segments.Select(MapLength).ToArray(),
                    MapLength(dash.Phase));

        PdfEffectParameters MapEffectParameters(PdfEffectParameters parameters) =>
            parameters with
            {
                Radius = MapLength(parameters.Radius),
                OffsetX = MapLength(parameters.OffsetX),
                OffsetY = MapLength(parameters.OffsetY),
                ReflectionGap = MapLength(parameters.ReflectionGap),
                BevelWidth = MapLength(parameters.BevelWidth),
                BevelHeight = MapLength(parameters.BevelHeight),
            };

        PdfPathPoint MapPoint(PdfPathPoint point) => new(MapX(point.X), MapY(point.Y));

        double MapX(double x) => fittedX + MapLength(x);

        double MapY(double y) => fittedPdfBottom + MapLength(y);

        double MapLength(double value) => value * scale;
    }
}
