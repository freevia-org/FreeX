using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>One authorable preset-geometry guide exposed as an edit-point handle.</summary>
public sealed record ShapeGeometryAdjustmentHandlePlan(
    string Name,
    string Label,
    LayoutPoint PositionDip,
    double Value,
    double Minimum,
    double Maximum);

/// <summary>Renderer-neutral edit-point state for one selected shape.</summary>
public sealed record ShapeGeometryAdjustmentPlan(
    uint ShapeId,
    bool CanEdit,
    string? DisabledReason,
    IReadOnlyList<ShapeGeometryAdjustmentHandlePlan> Handles);

/// <summary>Raw path-space vertex mutation produced by a custom-geometry edit point.</summary>
public sealed record ShapeGeometryCustomPointMutationPlan(
    int PathIndex,
    int SegmentIndex,
    double X,
    double Y,
    CustomGeometryPointSlot Slot = CustomGeometryPointSlot.Endpoint);

/// <summary>Raw ArcTo parameter mutation produced by a custom-geometry edit point.</summary>
public sealed record ShapeGeometryArcPointMutationPlan(
    int PathIndex,
    int SegmentIndex,
    double Value,
    CustomGeometryArcPointSlot Slot);

/// <summary>Result of reducing a pointer position to one preset guide value.</summary>
public sealed record ShapeGeometryAdjustmentMutationPlan(
    bool ShouldApply,
    string? Name,
    double? Value,
    string? DisabledReason,
    ShapeGeometryCustomPointMutationPlan? CustomPoint = null,
    ShapeGeometryArcPointMutationPlan? ArcPoint = null);

/// <summary>
/// Shared planning for PowerPoint-style preset-shape edit points.
/// Supported geometries are imported/custom line vertices, Chord (two explicit angle guides),
/// Rounded Rectangle (one explicit corner-radius guide), Triangle (one apex guide), Star5,
/// Star8, and Explosion (one point-depth guide each), directional
/// arrows (shaft and head guides), compound arrows (shaft and symmetric head guides), Chevron,
/// Home Plate, Ribbon, and Wave. The compositor already consumes these geometry representations.
/// </summary>
public static class ShapeGeometryAdjustmentPlanner
{
    private const double AngleUnitsPerDegree = 60000.0;
    private const double FullCircle = 360.0 * AngleUnitsPerDegree;
    private const double DefaultStartAngle = 0;
    private const double DefaultEndAngle = 180 * AngleUnitsPerDegree;
    private const double DefaultCornerAdjustment = 18000;
    private const double MaxCornerAdjustment = 50000;
    private const double DefaultTriangleAdjustment = 50000;
    private const double MaxTriangleAdjustment = 100000;
    private const double DefaultArrowAdjustment = 50000;
    private const double MaxArrowAdjustment = 100000;
    private const double DefaultStarAdjustment = 42000;
    private const double DefaultStar8Adjustment = 46000;
    private const double DefaultExplosionAdjustment = 62000;
    private const double DefaultCrossAdjustment = 35000;
    private const double DefaultSlantAdjustment = 20000;
    private const double MaxCrossAdjustment = 50000;
    private const double MaxStarAdjustment = 100000;
    private const double DefaultRibbonFoldAdjustment = 16667;
    private const double DefaultRibbonWidthAdjustment = 50000;
    private const double MaxRibbonFoldAdjustment = 33333;
    private const double MinRibbonWidthAdjustment = 25000;
    private const double MaxRibbonWidthAdjustment = 75000;
    private const double DefaultWaveAmplitudeAdjustment = 12500;
    private const double DefaultWavePhaseAdjustment = 0;
    private const double MaxWaveAmplitudeAdjustment = 20000;
    private const double MinWavePhaseAdjustment = -10000;
    private const double MaxWavePhaseAdjustment = 10000;
    private const double DefaultCylinderAdjustment = 25000;
    private const double MaxCylinderAdjustment = 50000;

    public const string UnsupportedShapeMessage =
        "This preset shape does not expose shared edit points yet.";
    public const string InvalidHandleMessage = "Select a valid shape edit point.";

    public static ShapeGeometryAdjustmentPlan Build(SlideShape shape, LayoutRect boundsDip)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (shape.CustomGeometry.Count > 0)
            return BuildCustomGeometryPlan(shape, boundsDip);

        if (shape.Kind != SlideShapeKind.AutoShape ||
            shape.AutoShapeKind is not (DrawingShapeKind.Chord or DrawingShapeKind.RoundedRectangle or DrawingShapeKind.Triangle or DrawingShapeKind.Star5 or DrawingShapeKind.Star8 or DrawingShapeKind.Explosion or
                DrawingShapeKind.RightArrow or DrawingShapeKind.LeftArrow or DrawingShapeKind.UpArrow or DrawingShapeKind.DownArrow or
                DrawingShapeKind.LeftRightArrow or DrawingShapeKind.UpDownArrow or
                DrawingShapeKind.Chevron or DrawingShapeKind.HomePlate or
                DrawingShapeKind.Parallelogram or DrawingShapeKind.Trapezoid or
                DrawingShapeKind.Cross or DrawingShapeKind.PlusSign or
                DrawingShapeKind.Ribbon or DrawingShapeKind.Wave or
                DrawingShapeKind.Cylinder))
        {
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: false,
                UnsupportedShapeMessage,
                Array.Empty<ShapeGeometryAdjustmentHandlePlan>());
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Triangle)
        {
            var adjustment = ReadAdjustment(
                shape,
                "adj",
                DefaultTriangleAdjustment,
                MaxTriangleAdjustment);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Apex position",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width * adjustment / MaxTriangleAdjustment,
                        boundsDip.Top),
                    adjustment,
                    0,
                    MaxTriangleAdjustment)]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
        {
            var adjustment = ReadAdjustment(
                shape,
                "adj",
                DefaultCornerAdjustment,
                MaxCornerAdjustment);
            var minDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var radius = PresetShapeAdjustmentMath.RoundedRectangleCornerRadius(
                minDimension,
                shape.PresetGeometryAdjustments.TryGetValue("adj", out var authoredAdjustment)
                    ? authoredAdjustment
                    : null);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Corner radius",
                    new LayoutPoint(boundsDip.Left + radius, boundsDip.Top),
                    adjustment,
                    0,
                    MaxCornerAdjustment)]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Star5)
        {
            var adjustment = ReadAdjustment(shape, "adj", DefaultStarAdjustment, MaxStarAdjustment);
            var radial = adjustment / MaxStarAdjustment / 2.0;
            var angle = -Math.PI / 2 + Math.PI / 5;
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Star point depth",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width * (0.5 + Math.Cos(angle) * radial),
                        boundsDip.Top + boundsDip.Height * (0.5 + Math.Sin(angle) * radial)),
                    adjustment,
                    0,
                    MaxStarAdjustment)]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Star8)
        {
            var adjustment = ReadAdjustment(shape, "adj", DefaultStar8Adjustment, MaxStarAdjustment);
            var radial = adjustment / MaxStarAdjustment / 2.0;
            var angle = -Math.PI / 2 + Math.PI / 8;
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Star point depth",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width * (0.5 + Math.Cos(angle) * radial),
                        boundsDip.Top + boundsDip.Height * (0.5 + Math.Sin(angle) * radial)),
                    adjustment,
                    0,
                    MaxStarAdjustment)]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Explosion)
        {
            var adjustment = ReadAdjustment(shape, "adj", DefaultExplosionAdjustment, MaxStarAdjustment);
            var radial = adjustment / MaxStarAdjustment / 2.0;
            var angle = -Math.PI / 2 + 0.08 + Math.PI / 12;
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Explosion spike depth",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width * (0.5 + Math.Cos(angle) * radial),
                        boundsDip.Top + boundsDip.Height * (0.5 + Math.Sin(angle) * radial)),
                    adjustment,
                    0,
                    MaxStarAdjustment)]);
        }

        if (IsDirectionalArrow(shape.AutoShapeKind))
        {
            var shaftAdjustment = ReadAdjustment(shape, "adj1", DefaultArrowAdjustment, MaxArrowAdjustment);
            var headAdjustment = ReadAdjustment(shape, "adj2", DefaultArrowAdjustment, MaxArrowAdjustment);
            var shaftHalf = shaftAdjustment / 200000.0;
            var headBase = 1 - headAdjustment / MaxArrowAdjustment;
            var vertical = shape.AutoShapeKind is DrawingShapeKind.UpArrow or DrawingShapeKind.DownArrow;
            var shaftPosition = vertical
                ? new LayoutPoint(
                    boundsDip.Left + boundsDip.Width * (0.5 + (shape.AutoShapeKind == DrawingShapeKind.UpArrow ? shaftHalf : -shaftHalf)),
                    boundsDip.Bottom)
                : new LayoutPoint(
                    boundsDip.Left,
                    boundsDip.Top + boundsDip.Height * (0.5 - shaftHalf));
            var headPosition = shape.AutoShapeKind switch
            {
                DrawingShapeKind.RightArrow => new LayoutPoint(boundsDip.Left + boundsDip.Width * headBase, boundsDip.Top),
                DrawingShapeKind.LeftArrow => new LayoutPoint(boundsDip.Left + boundsDip.Width * (1 - headBase), boundsDip.Top),
                DrawingShapeKind.UpArrow => new LayoutPoint(boundsDip.Left, boundsDip.Top + boundsDip.Height * (1 - headBase)),
                _ => new LayoutPoint(boundsDip.Left, boundsDip.Top + boundsDip.Height * headBase),
            };
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj1",
                        "Shaft thickness",
                        shaftPosition,
                        shaftAdjustment,
                        0,
                        MaxArrowAdjustment),
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj2",
                        "Head length",
                        headPosition,
                        headAdjustment,
                        0,
                        MaxArrowAdjustment),
                ]);
        }

        if (IsCompoundArrow(shape.AutoShapeKind))
        {
            var vertical = shape.AutoShapeKind == DrawingShapeKind.UpDownArrow;
            var minimumDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var maximumHeadAdjustment = CompoundArrowHeadMaximum(boundsDip, vertical);
            var shaftAdjustment = ReadAdjustment(shape, "adj1", DefaultArrowAdjustment, MaxArrowAdjustment);
            var headAdjustment = ReadAdjustment(shape, "adj2", DefaultArrowAdjustment, maximumHeadAdjustment);
            var shaftHalf = minimumDimension * shaftAdjustment / 200000.0;
            var headDepth = minimumDimension * headAdjustment / 100000.0;
            var shaftPosition = vertical
                ? new LayoutPoint(boundsDip.Left + boundsDip.Width / 2 + shaftHalf, boundsDip.Top)
                : new LayoutPoint(boundsDip.Left, boundsDip.Top + boundsDip.Height / 2 - shaftHalf);
            var headPosition = vertical
                ? new LayoutPoint(boundsDip.Left, boundsDip.Top + headDepth)
                : new LayoutPoint(boundsDip.Left + headDepth, boundsDip.Top);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj1",
                        "Shaft thickness",
                        shaftPosition,
                        shaftAdjustment,
                        0,
                        MaxArrowAdjustment),
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj2",
                        "Head length",
                        headPosition,
                        headAdjustment,
                        0,
                        maximumHeadAdjustment),
                ]);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Chevron or DrawingShapeKind.HomePlate)
        {
            var maximum = GuideMaximum(boundsDip);
            var adjustment = ReadAdjustment(shape, "adj", 50000, maximum);
            var depth = Math.Min(boundsDip.Width, boundsDip.Height) * adjustment / 100000.0;
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    shape.AutoShapeKind == DrawingShapeKind.Chevron ? "Chevron depth" : "Point depth",
                    new LayoutPoint(boundsDip.Right - depth, boundsDip.Top),
                    adjustment,
                    0,
                    maximum)]);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Parallelogram or DrawingShapeKind.Trapezoid)
        {
            var maximum = GuideMaximum(boundsDip);
            var adjustment = ReadAdjustment(
                shape,
                "adj",
                DefaultSlantAdjustment * boundsDip.Width / Math.Min(boundsDip.Width, boundsDip.Height),
                maximum);
            var inset = Math.Min(boundsDip.Width, boundsDip.Height) * adjustment / 100000.0;
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    shape.AutoShapeKind == DrawingShapeKind.Trapezoid ? "Trapezoid depth" : "Parallelogram slant",
                    new LayoutPoint(boundsDip.Left + Math.Min(inset, boundsDip.Width / 2), boundsDip.Top),
                    adjustment,
                    0,
                    maximum)]);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Cross or DrawingShapeKind.PlusSign)
        {
            var adjustment = ReadAdjustment(
                shape,
                "adj",
                DefaultCrossAdjustment,
                MaxCrossAdjustment);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Bar inset",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width * adjustment / 100000.0,
                        boundsDip.Top),
                    adjustment,
                    0,
                    MaxCrossAdjustment)]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Ribbon)
        {
            var fold = ReadAdjustment(shape, "adj1", DefaultRibbonFoldAdjustment, MaxRibbonFoldAdjustment);
            var width = ReadAdjustment(
                shape,
                "adj2",
                DefaultRibbonWidthAdjustment,
                MaxRibbonWidthAdjustment,
                MinRibbonWidthAdjustment);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj1",
                        "Ribbon fold depth",
                        new LayoutPoint(
                            boundsDip.Left + boundsDip.Width / 2,
                            boundsDip.Top + boundsDip.Height * PresetShapeAdjustmentMath.RibbonBandTop(fold)),
                        fold,
                        0,
                        MaxRibbonFoldAdjustment),
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj2",
                        "Ribbon fold width",
                        new LayoutPoint(boundsDip.Left + boundsDip.Width * (0.5 - width / 200000.0), boundsDip.Top),
                        width,
                        MinRibbonWidthAdjustment,
                        MaxRibbonWidthAdjustment),
                ]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Wave)
        {
            var amplitude = ReadAdjustment(shape, "adj1", DefaultWaveAmplitudeAdjustment, MaxWaveAmplitudeAdjustment);
            var phase = ReadAdjustment(
                shape,
                "adj2",
                DefaultWavePhaseAdjustment,
                MaxWavePhaseAdjustment,
                MinWavePhaseAdjustment);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj1",
                        "Wave amplitude",
                        new LayoutPoint(boundsDip.Left, boundsDip.Top + boundsDip.Height * amplitude / 100000.0),
                        amplitude,
                        0,
                        MaxWaveAmplitudeAdjustment),
                    new ShapeGeometryAdjustmentHandlePlan(
                        "adj2",
                        "Wave phase",
                        new LayoutPoint(boundsDip.Left + boundsDip.Width * (0.5 - phase / 200000.0), boundsDip.Bottom),
                        phase,
                        MinWavePhaseAdjustment,
                        MaxWavePhaseAdjustment),
                ]);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Cylinder)
        {
            var adjustment = ReadAdjustment(
                shape,
                "adj",
                DefaultCylinderAdjustment,
                MaxCylinderAdjustment);
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
                boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
                [new ShapeGeometryAdjustmentHandlePlan(
                    "adj",
                    "Cylinder cap height",
                    new LayoutPoint(
                        boundsDip.Left + boundsDip.Width / 2,
                        boundsDip.Top + boundsDip.Height * adjustment / 100000.0),
                    adjustment,
                    0,
                    MaxCylinderAdjustment)]);
        }

        var start = ReadAngle(shape, "adj1", DefaultStartAngle);
        var end = ReadAngle(shape, "adj2", DefaultEndAngle);
        return new ShapeGeometryAdjustmentPlan(
            shape.Id,
            CanEdit: boundsDip.Width > 0 && boundsDip.Height > 0,
            boundsDip.Width > 0 && boundsDip.Height > 0 ? null : UnsupportedShapeMessage,
            [
                BuildHandle("adj1", "Start angle", start, boundsDip),
                BuildHandle("adj2", "End angle", end, boundsDip),
            ]);
    }

    public static ShapeGeometryAdjustmentMutationPlan BuildMutationPlan(
        SlideShape shape,
        LayoutRect boundsDip,
        string handleName,
        LayoutPoint pointerDip)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var plan = Build(shape, boundsDip);
        if (!plan.CanEdit)
            return new(false, null, null, plan.DisabledReason);

        if (shape.CustomGeometry.Count > 0)
        {
            if (TryParseArcHandle(handleName, out var arcPathIndex, out var arcSegmentIndex, out var arcSlot) &&
                arcPathIndex >= 0 && arcPathIndex < shape.CustomGeometry.Count)
            {
                var arcPath = shape.CustomGeometry[arcPathIndex];
                if (arcSegmentIndex < 0 || arcSegmentIndex >= arcPath.Segments.Count ||
                    arcPath.Segments[arcSegmentIndex].Kind != CustomSegmentKind.ArcTo ||
                    !TryGetArcGeometry(arcPath, arcSegmentIndex, out var arcCenterX, out var arcCenterY,
                        out _, out _, out _, out _))
                {
                    return new(false, null, null, InvalidHandleMessage);
                }

                var arcWidth = PathWidth(arcPath, boundsDip);
                var arcHeight = PathHeight(arcPath, boundsDip);
                var rawX = Math.Clamp((pointerDip.X - boundsDip.Left) / boundsDip.Width * arcWidth, 0, arcWidth);
                var rawY = Math.Clamp((pointerDip.Y - boundsDip.Top) / boundsDip.Height * arcHeight, 0, arcHeight);

                // The arc's start point is not one of its own parameters: the renderer always
                // treats it as wherever the pen already sits (see TryGetArcStartTarget), so
                // dragging "start" must move that predecessor point directly instead of writing
                // StAng, which only relocates the centre/end while the rendered start stays put.
                if (arcSlot == CustomGeometryArcPointSlot.StartAngle)
                {
                    if (!TryGetArcStartTarget(arcPath, arcSegmentIndex, out var targetSegmentIndex, out var targetSlot))
                        return new(false, null, null, InvalidHandleMessage);

                    return new(
                        true,
                        handleName,
                        null,
                        null,
                        new ShapeGeometryCustomPointMutationPlan(arcPathIndex, targetSegmentIndex, rawX, rawY, targetSlot));
                }

                var segment = arcPath.Segments[arcSegmentIndex];
                var value = arcSlot switch
                {
                    CustomGeometryArcPointSlot.EndAngle => NearestEquivalentAngle(
                        AngleFromPoint(rawX - arcCenterX, rawY - arcCenterY), segment.StAng + segment.SwAng),
                    CustomGeometryArcPointSlot.RadiusX => Math.Max(1, Math.Abs(rawX - arcCenterX)),
                    CustomGeometryArcPointSlot.RadiusY => Math.Max(1, Math.Abs(rawY - arcCenterY)),
                    _ => 0,
                };
                return new(
                    true,
                    handleName,
                    null,
                    null,
                    ArcPoint: new ShapeGeometryArcPointMutationPlan(
                        arcPathIndex, arcSegmentIndex, value, arcSlot));
            }

            if (!TryParseCustomHandle(handleName, out var pathIndex, out var segmentIndex, out var slot) ||
                pathIndex < 0 || pathIndex >= shape.CustomGeometry.Count)
            {
                return new(false, null, null, InvalidHandleMessage);
            }

            var path = shape.CustomGeometry[pathIndex];
            if (segmentIndex < 0 || segmentIndex >= path.Segments.Count ||
                !TryGetSegmentPoint(path.Segments[segmentIndex], slot, out _, out _))
            {
                return new(false, null, null, InvalidHandleMessage);
            }

            var pathWidth = PathWidth(path, boundsDip);
            var pathHeight = PathHeight(path, boundsDip);
            var x = Math.Clamp((pointerDip.X - boundsDip.Left) / boundsDip.Width * pathWidth, 0, pathWidth);
            var y = Math.Clamp((pointerDip.Y - boundsDip.Top) / boundsDip.Height * pathHeight, 0, pathHeight);
            return new(
                true,
                handleName,
                null,
                null,
                new ShapeGeometryCustomPointMutationPlan(pathIndex, segmentIndex, x, y, slot));
        }

        if (shape.AutoShapeKind is DrawingShapeKind.RoundedRectangle or DrawingShapeKind.Triangle or DrawingShapeKind.Star5 or DrawingShapeKind.Star8 or DrawingShapeKind.Explosion ||
            IsDirectionalArrow(shape.AutoShapeKind))
        {
            if (IsDirectionalArrow(shape.AutoShapeKind))
            {
                if (handleName == "adj1")
                {
                    var vertical = shape.AutoShapeKind is DrawingShapeKind.UpArrow or DrawingShapeKind.DownArrow;
                    var center = vertical ? boundsDip.Left + boundsDip.Width / 2 : boundsDip.Top + boundsDip.Height / 2;
                    var pointer = vertical ? pointerDip.X : pointerDip.Y;
                    var normalizedHalf = Math.Abs(pointer - center) / (vertical ? boundsDip.Width : boundsDip.Height);
                    return new(true, "adj1", Math.Clamp(normalizedHalf * 200000.0, 0, MaxArrowAdjustment), null);
                }

                if (handleName == "adj2")
                {
                    var normalizedHeadBase = shape.AutoShapeKind is DrawingShapeKind.UpArrow or DrawingShapeKind.DownArrow
                        ? (pointerDip.Y - boundsDip.Top) / boundsDip.Height
                        : (pointerDip.X - boundsDip.Left) / boundsDip.Width;
                    var value = shape.AutoShapeKind is DrawingShapeKind.RightArrow or DrawingShapeKind.DownArrow
                        ? 1 - normalizedHeadBase
                        : normalizedHeadBase;
                    return new(true, "adj2", Math.Clamp(value * MaxArrowAdjustment, 0, MaxArrowAdjustment), null);
                }

                return new(false, null, null, InvalidHandleMessage);
            }

            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            if (shape.AutoShapeKind == DrawingShapeKind.Triangle)
            {
                var adjustment = (pointerDip.X - boundsDip.Left) / boundsDip.Width * MaxTriangleAdjustment;
                return new(true, "adj", Math.Clamp(adjustment, 0, MaxTriangleAdjustment), null);
            }

            if (shape.AutoShapeKind is DrawingShapeKind.Star5 or DrawingShapeKind.Star8 or DrawingShapeKind.Explosion)
            {
                var starNormalizedX = (pointerDip.X - (boundsDip.Left + boundsDip.Width / 2)) / (boundsDip.Width / 2);
                var starNormalizedY = (pointerDip.Y - (boundsDip.Top + boundsDip.Height / 2)) / (boundsDip.Height / 2);
                var angle = shape.AutoShapeKind switch
                {
                    DrawingShapeKind.Star8 => -Math.PI / 2 + Math.PI / 8,
                    DrawingShapeKind.Explosion => -Math.PI / 2 + 0.08 + Math.PI / 12,
                    _ => -Math.PI / 2 + Math.PI / 5,
                };
                var radial = starNormalizedX * Math.Cos(angle) + starNormalizedY * Math.Sin(angle);
                return new(true, "adj", Math.Clamp(radial * MaxStarAdjustment, 0, MaxStarAdjustment), null);
            }

            var minDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var cornerAdjustment = (pointerDip.X - boundsDip.Left) / minDimension * 100000.0;
            return new(true, "adj", Math.Clamp(cornerAdjustment, 0, MaxCornerAdjustment), null);
        }

        if (IsCompoundArrow(shape.AutoShapeKind))
        {
            var vertical = shape.AutoShapeKind == DrawingShapeKind.UpDownArrow;
            var minimumDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            if (handleName == "adj1")
            {
                var center = vertical
                    ? boundsDip.Left + boundsDip.Width / 2
                    : boundsDip.Top + boundsDip.Height / 2;
                var pointer = vertical ? pointerDip.X : pointerDip.Y;
                var normalizedHalf = Math.Abs(pointer - center) / minimumDimension;
                return new(true, "adj1", Math.Clamp(normalizedHalf * 200000.0, 0, MaxArrowAdjustment), null);
            }

            if (handleName == "adj2")
            {
                var normalizedDepth = vertical
                    ? (pointerDip.Y - boundsDip.Top) / minimumDimension
                    : (pointerDip.X - boundsDip.Left) / minimumDimension;
                return new(
                    true,
                    "adj2",
                    Math.Clamp(normalizedDepth * 100000.0, 0, CompoundArrowHeadMaximum(boundsDip, vertical)),
                    null);
            }

            return new(false, null, null, InvalidHandleMessage);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Chevron or DrawingShapeKind.HomePlate)
        {
            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            var minimumDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var maximum = GuideMaximum(boundsDip);
            var adjustment = (boundsDip.Right - pointerDip.X) / minimumDimension * 100000.0;
            return new(true, "adj", Math.Clamp(adjustment, 0, maximum), null);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Parallelogram or DrawingShapeKind.Trapezoid)
        {
            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            var minimumDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var maximum = GuideMaximum(boundsDip);
            var adjustment = (pointerDip.X - boundsDip.Left) / minimumDimension * 100000.0;
            return new(true, "adj", Math.Clamp(adjustment, 0, maximum), null);
        }

        if (shape.AutoShapeKind is DrawingShapeKind.Cross or DrawingShapeKind.PlusSign)
        {
            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            var adjustment = (pointerDip.X - boundsDip.Left) / boundsDip.Width * 100000.0;
            return new(true, "adj", Math.Clamp(adjustment, 0, MaxCrossAdjustment), null);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Ribbon)
        {
            if (handleName == "adj1")
            {
                var adjustment = (pointerDip.Y - boundsDip.Top) / boundsDip.Height * 100000.0;
                return new(true, "adj1", Math.Clamp(adjustment, 0, MaxRibbonFoldAdjustment), null);
            }

            if (handleName == "adj2")
            {
                var ribbonCenterX = boundsDip.Left + boundsDip.Width / 2;
                var adjustment = (ribbonCenterX - pointerDip.X) / boundsDip.Width * 200000.0;
                return new(
                    true,
                    "adj2",
                    Math.Clamp(adjustment, MinRibbonWidthAdjustment, MaxRibbonWidthAdjustment),
                    null);
            }

            return new(false, null, null, InvalidHandleMessage);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Wave)
        {
            if (handleName == "adj1")
            {
                var adjustment = (pointerDip.Y - boundsDip.Top) / boundsDip.Height * 100000.0;
                return new(true, "adj1", Math.Clamp(adjustment, 0, MaxWaveAmplitudeAdjustment), null);
            }

            if (handleName == "adj2")
            {
                var waveCenterX = boundsDip.Left + boundsDip.Width / 2;
                var adjustment = (waveCenterX - pointerDip.X) / boundsDip.Width * 200000.0;
                return new(
                    true,
                    "adj2",
                    Math.Clamp(adjustment, MinWavePhaseAdjustment, MaxWavePhaseAdjustment),
                    null);
            }

            return new(false, null, null, InvalidHandleMessage);
        }

        if (shape.AutoShapeKind == DrawingShapeKind.Cylinder)
        {
            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            var adjustment = (pointerDip.Y - boundsDip.Top) / boundsDip.Height * 100000.0;
            return new(
                true,
                "adj",
                Math.Clamp(adjustment, 0, MaxCylinderAdjustment),
                null);
        }

        if (handleName is not ("adj1" or "adj2"))
            return new(false, null, null, InvalidHandleMessage);

        var centerX = boundsDip.Left + boundsDip.Width / 2;
        var centerY = boundsDip.Top + boundsDip.Height / 2;
        var normalizedX = (pointerDip.X - centerX) / (boundsDip.Width / 2);
        var normalizedY = (pointerDip.Y - centerY) / (boundsDip.Height / 2);
        var radians = Math.Atan2(normalizedY, normalizedX);
        var degrees = radians * 180 / Math.PI;
        if (degrees < 0)
            degrees += 360;

        return new(
            true,
            handleName,
            degrees * AngleUnitsPerDegree,
            null);
    }

    /// <summary>Resolves a selected custom-geometry endpoint that can be deleted or extended.</summary>
    public static bool TryGetCustomVertexTarget(
        SlideShape shape,
        string handleName,
        out int pathIndex,
        out int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(shape);
        pathIndex = -1;
        segmentIndex = -1;
        if (!TryParseCustomHandle(handleName, out pathIndex, out segmentIndex, out var slot) ||
            slot != CustomGeometryPointSlot.Endpoint ||
            pathIndex < 0 || pathIndex >= shape.CustomGeometry.Count)
            return false;

        var path = shape.CustomGeometry[pathIndex];
        return segmentIndex >= 0 && segmentIndex < path.Segments.Count &&
            path.Segments[segmentIndex].Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo;
    }

    /// <summary>
    /// Computes the midpoint used by PowerPoint-style Add Point for a selected custom vertex.
    /// The new point is inserted on the following line, or on the closing line back to MoveTo.
    /// </summary>
    public static bool TryBuildCustomVertexInsertion(
        SlideShape shape,
        string handleName,
        out int pathIndex,
        out int segmentIndex,
        out double x,
        out double y)
    {
        x = 0;
        y = 0;
        if (!TryGetCustomVertexTarget(shape, handleName, out pathIndex, out segmentIndex))
            return false;

        var path = shape.CustomGeometry[pathIndex];
        var current = path.Segments[segmentIndex];
        if (!TryGetSegmentPoint(current, CustomGeometryPointSlot.Endpoint, out var currentX, out var currentY))
            return false;

        CustomSegment? next = null;
        for (var index = segmentIndex + 1; index < path.Segments.Count; index++)
        {
            if (path.Segments[index].Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo)
            {
                next = path.Segments[index];
                break;
            }

            if (path.Segments[index].Kind == CustomSegmentKind.Close)
                break;
        }

        if (next is null)
        {
            next = path.Segments.FirstOrDefault(segment =>
                segment.Kind == CustomSegmentKind.MoveTo);
        }

        if (next is null || !TryGetSegmentPoint(next, CustomGeometryPointSlot.Endpoint, out var nextX, out var nextY))
            return false;

        x = (currentX + nextX) / 2;
        y = (currentY + nextY) / 2;
        return true;
    }

    /// <summary>Returns whether removing the selected line vertex leaves a valid path skeleton.</summary>
    public static bool CanDeleteCustomVertex(SlideShape shape, string handleName)
    {
        if (!TryGetCustomVertexTarget(shape, handleName, out var pathIndex, out var segmentIndex))
            return false;

        var path = shape.CustomGeometry[pathIndex];
        if (path.Segments[segmentIndex].Kind != CustomSegmentKind.LineTo)
            return false;

        var endpointCount = path.Segments.Count(segment =>
            segment.Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo);
        return endpointCount > 2;
    }

    private static ShapeGeometryAdjustmentHandlePlan BuildHandle(
        string name,
        string label,
        double value,
        LayoutRect boundsDip)
    {
        var angle = value / AngleUnitsPerDegree * Math.PI / 180;
        var centerX = boundsDip.Left + boundsDip.Width / 2;
        var centerY = boundsDip.Top + boundsDip.Height / 2;
        var position = new LayoutPoint(
            centerX + boundsDip.Width / 2 * Math.Cos(angle),
            centerY + boundsDip.Height / 2 * Math.Sin(angle));
        return new(name, label, position, value, 0, FullCircle);
    }

    private static double ReadAngle(SlideShape shape, string name, double fallback) =>
        shape.PresetGeometryAdjustments.TryGetValue(name, out var value)
            ? Math.Clamp(value, 0, FullCircle)
            : fallback;

    private static double ReadAdjustment(
        SlideShape shape,
        string name,
        double fallback,
        double maximum,
        double minimum = 0) =>
        shape.PresetGeometryAdjustments.TryGetValue(name, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static bool IsDirectionalArrow(DrawingShapeKind kind) =>
        kind is DrawingShapeKind.RightArrow or DrawingShapeKind.LeftArrow or
            DrawingShapeKind.UpArrow or DrawingShapeKind.DownArrow;

    private static bool IsCompoundArrow(DrawingShapeKind kind) =>
        kind is DrawingShapeKind.LeftRightArrow or DrawingShapeKind.UpDownArrow;

    private static double CompoundArrowHeadMaximum(LayoutRect boundsDip, bool vertical) =>
        100000.0 * (vertical ? boundsDip.Height : boundsDip.Width) / Math.Min(boundsDip.Width, boundsDip.Height);

    private static double GuideMaximum(LayoutRect boundsDip) =>
        100000.0 * boundsDip.Width / Math.Min(boundsDip.Width, boundsDip.Height);

    private static ShapeGeometryAdjustmentPlan BuildCustomGeometryPlan(
        SlideShape shape,
        LayoutRect boundsDip)
    {
        var handles = new List<ShapeGeometryAdjustmentHandlePlan>();
        for (var pathIndex = 0; pathIndex < shape.CustomGeometry.Count; pathIndex++)
        {
            var path = shape.CustomGeometry[pathIndex];
            var pathWidth = PathWidth(path, boundsDip);
            var pathHeight = PathHeight(path, boundsDip);
            for (var segmentIndex = 0; segmentIndex < path.Segments.Count; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                foreach (var (slot, label, x, y, suffix) in SegmentPoints(segment))
                {
                    handles.Add(new ShapeGeometryAdjustmentHandlePlan(
                        CustomHandleName(pathIndex, segmentIndex, suffix),
                        label,
                        new LayoutPoint(
                            boundsDip.Left + x / pathWidth * boundsDip.Width,
                            boundsDip.Top + y / pathHeight * boundsDip.Height),
                        x,
                        0,
                        pathWidth));
                }

                if (segment.Kind == CustomSegmentKind.ArcTo &&
                    TryGetArcGeometry(path, segmentIndex, out var centerX, out var centerY,
                        out var startX, out var startY, out var endX, out var endY))
                {
                    // Only expose the "start" handle when it has somewhere to move: the rendered
                    // start point is the predecessor segment's endpoint, not an ArcTo parameter of
                    // its own (see TryGetArcStartTarget), so a malformed/unsupported predecessor
                    // means dragging it could never move anything.
                    if (TryGetArcStartTarget(path, segmentIndex, out _, out _))
                    {
                        handles.Add(BuildCustomHandle(
                            CustomArcHandleName(pathIndex, segmentIndex, "start"),
                            "Arc start",
                            startX, startY, segment.StAng, 0, 360, pathWidth, pathHeight, boundsDip));
                    }
                    handles.Add(BuildCustomHandle(
                        CustomArcHandleName(pathIndex, segmentIndex, "end"),
                        "Arc end",
                        endX, endY, segment.StAng + segment.SwAng, 0, 360, pathWidth, pathHeight, boundsDip));
                    handles.Add(BuildCustomHandle(
                        CustomArcHandleName(pathIndex, segmentIndex, "radius-x"),
                        "Arc horizontal radius",
                        centerX + Math.Abs(segment.WR), centerY, segment.WR, 1, pathWidth, pathWidth, pathHeight, boundsDip));
                    handles.Add(BuildCustomHandle(
                        CustomArcHandleName(pathIndex, segmentIndex, "radius-y"),
                        "Arc vertical radius",
                        centerX, centerY + Math.Abs(segment.HR), segment.HR, 1, pathHeight, pathWidth, pathHeight, boundsDip));
                }
            }
        }

        var canEdit = boundsDip.Width > 0 && boundsDip.Height > 0 && handles.Count > 0;
        return new ShapeGeometryAdjustmentPlan(
            shape.Id,
            canEdit,
            canEdit ? null : UnsupportedShapeMessage,
            handles);
    }

    private static string CustomHandleName(int pathIndex, int segmentIndex, string? suffix = null) =>
        string.IsNullOrEmpty(suffix) ? $"custom:{pathIndex}:{segmentIndex}" : $"custom:{pathIndex}:{segmentIndex}:{suffix}";

    private static string CustomArcHandleName(int pathIndex, int segmentIndex, string slot) =>
        $"arc:{pathIndex}:{segmentIndex}:{slot}";

    private static ShapeGeometryAdjustmentHandlePlan BuildCustomHandle(
        string name,
        string label,
        double x,
        double y,
        double value,
        double minimum,
        double maximum,
        double pathWidth,
        double pathHeight,
        LayoutRect boundsDip) =>
        new(
            name,
            label,
            new LayoutPoint(
                boundsDip.Left + x / pathWidth * boundsDip.Width,
                boundsDip.Top + y / pathHeight * boundsDip.Height),
            value,
            minimum,
            maximum);

    private static bool TryParseArcHandle(
        string handleName,
        out int pathIndex,
        out int segmentIndex,
        out CustomGeometryArcPointSlot slot)
    {
        pathIndex = -1;
        segmentIndex = -1;
        slot = CustomGeometryArcPointSlot.StartAngle;
        var parts = handleName.Split(':');
        if (parts.Length != 4 || parts[0] != "arc" ||
            !int.TryParse(parts[1], out pathIndex) || !int.TryParse(parts[2], out segmentIndex))
            return false;

        slot = parts[3] switch
        {
            "start" => CustomGeometryArcPointSlot.StartAngle,
            "end" => CustomGeometryArcPointSlot.EndAngle,
            "radius-x" => CustomGeometryArcPointSlot.RadiusX,
            "radius-y" => CustomGeometryArcPointSlot.RadiusY,
            _ => (CustomGeometryArcPointSlot)(-1),
        };
        return (int)slot >= 0;
    }

    private static bool TryParseCustomHandle(
        string handleName,
        out int pathIndex,
        out int segmentIndex,
        out CustomGeometryPointSlot slot)
    {
        pathIndex = -1;
        segmentIndex = -1;
        slot = CustomGeometryPointSlot.Endpoint;
        var parts = handleName.Split(':');
        if (parts.Length is not (3 or 4) || parts[0] != "custom" ||
            !int.TryParse(parts[1], out pathIndex) || !int.TryParse(parts[2], out segmentIndex))
            return false;

        if (parts.Length == 3)
            return true;

        slot = parts[3] switch
        {
            "c1" => CustomGeometryPointSlot.Control1,
            "c2" => CustomGeometryPointSlot.Control2,
            "end" => CustomGeometryPointSlot.Endpoint,
            _ => (CustomGeometryPointSlot)(-1),
        };
        return (int)slot >= 0;
    }

    private static IEnumerable<(CustomGeometryPointSlot Slot, string Label, double X, double Y, string Suffix)> SegmentPoints(CustomSegment segment)
    {
        switch (segment.Kind)
        {
            case CustomSegmentKind.MoveTo:
            case CustomSegmentKind.LineTo:
                yield return (CustomGeometryPointSlot.Endpoint, "Vertex", segment.X, segment.Y, "");
                break;
            case CustomSegmentKind.CubicBezTo:
                yield return (CustomGeometryPointSlot.Control1, "Curve control 1", segment.X, segment.Y, "c1");
                yield return (CustomGeometryPointSlot.Control2, "Curve control 2", segment.X1, segment.Y1, "c2");
                yield return (CustomGeometryPointSlot.Endpoint, "Vertex", segment.X2, segment.Y2, "end");
                break;
            case CustomSegmentKind.QuadBezTo:
                yield return (CustomGeometryPointSlot.Control1, "Curve control", segment.X, segment.Y, "c1");
                yield return (CustomGeometryPointSlot.Endpoint, "Vertex", segment.X1, segment.Y1, "end");
                break;
        }
    }

    private static bool TryGetArcGeometry(
        CustomGeometryPath path,
        int segmentIndex,
        out double centerX,
        out double centerY,
        out double startX,
        out double startY,
        out double endX,
        out double endY)
    {
        centerX = centerY = startX = startY = endX = endY = 0;
        if (segmentIndex < 0 || segmentIndex >= path.Segments.Count ||
            path.Segments[segmentIndex].Kind != CustomSegmentKind.ArcTo)
            return false;

        var currentX = 0.0;
        var currentY = 0.0;
        var figureStartX = 0.0;
        var figureStartY = 0.0;
        for (var index = 0; index <= segmentIndex; index++)
        {
            var segment = path.Segments[index];
            switch (segment.Kind)
            {
                case CustomSegmentKind.MoveTo:
                case CustomSegmentKind.LineTo:
                    currentX = segment.X;
                    currentY = segment.Y;
                    if (segment.Kind == CustomSegmentKind.MoveTo)
                    {
                        figureStartX = currentX;
                        figureStartY = currentY;
                    }
                    break;
                case CustomSegmentKind.CubicBezTo:
                    currentX = segment.X2;
                    currentY = segment.Y2;
                    break;
                case CustomSegmentKind.QuadBezTo:
                    currentX = segment.X1;
                    currentY = segment.Y1;
                    break;
                case CustomSegmentKind.ArcTo:
                {
                    var startAngle = segment.StAng * Math.PI / 180.0;
                    var endAngle = (segment.StAng + segment.SwAng) * Math.PI / 180.0;
                    var wR = segment.WR;
                    var hR = segment.HR;
                    var cx = currentX - wR * Math.Cos(startAngle);
                    var cy = currentY - hR * Math.Sin(startAngle);
                    var arcStartX = currentX;
                    var arcStartY = currentY;
                    var arcEndX = cx + wR * Math.Cos(endAngle);
                    var arcEndY = cy + hR * Math.Sin(endAngle);
                    if (index == segmentIndex)
                    {
                        centerX = cx;
                        centerY = cy;
                        startX = arcStartX;
                        startY = arcStartY;
                        endX = arcEndX;
                        endY = arcEndY;
                        return true;
                    }

                    currentX = arcEndX;
                    currentY = arcEndY;
                    break;
                }
                case CustomSegmentKind.Close:
                    currentX = figureStartX;
                    currentY = figureStartY;
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the writable point that determines an ArcTo segment's rendered start position.
    /// <see cref="CustomGeometryBuilder.BuildCustom"/> (the renderer) never reads this segment's
    /// own StAng to place its start point -- the start is always wherever the pen already sits
    /// when the ArcTo begins, i.e. the endpoint of the immediately preceding segment (or the
    /// enclosing figure's MoveTo, if the pen was just reset by a Close). Only that coordinate can
    /// move the rendered start point; StAng only relocates the ellipse's centre (and therefore the
    /// end point) around a start that stays fixed.
    /// </summary>
    private static bool TryGetArcStartTarget(
        CustomGeometryPath path,
        int segmentIndex,
        out int targetSegmentIndex,
        out CustomGeometryPointSlot targetSlot)
    {
        targetSegmentIndex = -1;
        targetSlot = CustomGeometryPointSlot.Endpoint;

        var predecessorIndex = segmentIndex - 1;
        if (predecessorIndex < 0 || predecessorIndex >= path.Segments.Count)
            return false;

        var predecessor = path.Segments[predecessorIndex];
        if (predecessor.Kind == CustomSegmentKind.Close)
        {
            // The pen resets to the enclosing figure's start: walk back to that MoveTo.
            var moveToIndex = -1;
            for (var i = predecessorIndex - 1; i >= 0; i--)
            {
                if (path.Segments[i].Kind == CustomSegmentKind.MoveTo)
                {
                    moveToIndex = i;
                    break;
                }
            }

            if (moveToIndex < 0)
                return false;

            predecessorIndex = moveToIndex;
            predecessor = path.Segments[moveToIndex];
        }

        if (!TryGetSegmentPoint(predecessor, CustomGeometryPointSlot.Endpoint, out _, out _))
            return false;

        targetSegmentIndex = predecessorIndex;
        targetSlot = CustomGeometryPointSlot.Endpoint;
        return true;
    }

    private static double AngleFromPoint(double x, double y)
    {
        var degrees = Math.Atan2(y, x) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double NearestEquivalentAngle(double angle, double reference)
    {
        while (angle - reference > 180)
            angle -= 360;
        while (angle - reference < -180)
            angle += 360;
        return angle;
    }

    private static bool TryGetSegmentPoint(
        CustomSegment segment,
        CustomGeometryPointSlot slot,
        out double x,
        out double y)
    {
        x = 0;
        y = 0;
        switch (segment.Kind, slot)
        {
            case (CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo, CustomGeometryPointSlot.Endpoint):
            case (CustomSegmentKind.QuadBezTo, CustomGeometryPointSlot.Control1):
            case (CustomSegmentKind.CubicBezTo, CustomGeometryPointSlot.Control1):
                x = segment.X;
                y = segment.Y;
                return true;
            case (CustomSegmentKind.QuadBezTo, CustomGeometryPointSlot.Endpoint):
                x = segment.X1;
                y = segment.Y1;
                return true;
            case (CustomSegmentKind.CubicBezTo, CustomGeometryPointSlot.Control2):
                x = segment.X1;
                y = segment.Y1;
                return true;
            case (CustomSegmentKind.CubicBezTo, CustomGeometryPointSlot.Endpoint):
                x = segment.X2;
                y = segment.Y2;
                return true;
            default:
                return false;
        }
    }

    private static double PathWidth(CustomGeometryPath path, LayoutRect boundsDip) =>
        path.PathW > 0 ? path.PathW : Math.Max(1, boundsDip.Width);

    private static double PathHeight(CustomGeometryPath path, LayoutRect boundsDip) =>
        path.PathH > 0 ? path.PathH : Math.Max(1, boundsDip.Height);
}
