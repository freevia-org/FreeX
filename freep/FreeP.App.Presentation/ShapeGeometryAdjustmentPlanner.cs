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
    double Y);

/// <summary>Result of reducing a pointer position to one preset guide value.</summary>
public sealed record ShapeGeometryAdjustmentMutationPlan(
    bool ShouldApply,
    string? Name,
    double? Value,
    string? DisabledReason,
    ShapeGeometryCustomPointMutationPlan? CustomPoint = null);

/// <summary>
/// Shared planning for PowerPoint-style preset-shape edit points.
/// Supported geometries are imported/custom line vertices, Chord (two explicit angle guides),
/// and Rounded Rectangle (one explicit corner-radius guide). The compositor already consumes
/// these geometry representations.
/// </summary>
public static class ShapeGeometryAdjustmentPlanner
{
    private const double AngleUnitsPerDegree = 60000.0;
    private const double FullCircle = 360.0 * AngleUnitsPerDegree;
    private const double DefaultStartAngle = 0;
    private const double DefaultEndAngle = 180 * AngleUnitsPerDegree;
    private const double DefaultCornerAdjustment = 18000;
    private const double MaxCornerAdjustment = 50000;

    public const string UnsupportedShapeMessage =
        "This preset shape does not expose shared edit points yet.";
    public const string InvalidHandleMessage = "Select a valid shape edit point.";

    public static ShapeGeometryAdjustmentPlan Build(SlideShape shape, LayoutRect boundsDip)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (shape.CustomGeometry.Count > 0)
            return BuildCustomGeometryPlan(shape, boundsDip);

        if (shape.Kind != SlideShapeKind.AutoShape ||
            shape.AutoShapeKind is not (DrawingShapeKind.Chord or DrawingShapeKind.RoundedRectangle))
        {
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: false,
                UnsupportedShapeMessage,
                Array.Empty<ShapeGeometryAdjustmentHandlePlan>());
        }

        if (shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
        {
            var adjustment = ReadAdjustment(shape, "adj", DefaultCornerAdjustment);
            var minDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var radius = minDimension * adjustment / 100000.0;
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
            if (!TryParseCustomHandle(handleName, out var pathIndex, out var segmentIndex) ||
                pathIndex < 0 || pathIndex >= shape.CustomGeometry.Count)
            {
                return new(false, null, null, InvalidHandleMessage);
            }

            var path = shape.CustomGeometry[pathIndex];
            if (segmentIndex < 0 || segmentIndex >= path.Segments.Count ||
                path.Segments[segmentIndex].Kind is not (CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo))
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
                new ShapeGeometryCustomPointMutationPlan(pathIndex, segmentIndex, x, y));
        }

        if (shape.AutoShapeKind == DrawingShapeKind.RoundedRectangle)
        {
            if (handleName != "adj")
                return new(false, null, null, InvalidHandleMessage);

            var minDimension = Math.Min(boundsDip.Width, boundsDip.Height);
            var adjustment = (pointerDip.X - boundsDip.Left) / minDimension * 100000.0;
            return new(true, "adj", Math.Clamp(adjustment, 0, MaxCornerAdjustment), null);
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

    private static double ReadAdjustment(SlideShape shape, string name, double fallback) =>
        shape.PresetGeometryAdjustments.TryGetValue(name, out var value)
            ? Math.Clamp(value, 0, MaxCornerAdjustment)
            : fallback;

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
                if (segment.Kind is not (CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo))
                    continue;

                handles.Add(new ShapeGeometryAdjustmentHandlePlan(
                    CustomHandleName(pathIndex, segmentIndex),
                    "Vertex",
                    new LayoutPoint(
                        boundsDip.Left + segment.X / pathWidth * boundsDip.Width,
                        boundsDip.Top + segment.Y / pathHeight * boundsDip.Height),
                    segment.X,
                    0,
                    pathWidth));
            }
        }

        var canEdit = boundsDip.Width > 0 && boundsDip.Height > 0 && handles.Count > 0;
        return new ShapeGeometryAdjustmentPlan(
            shape.Id,
            canEdit,
            canEdit ? null : UnsupportedShapeMessage,
            handles);
    }

    private static string CustomHandleName(int pathIndex, int segmentIndex) =>
        $"custom:{pathIndex}:{segmentIndex}";

    private static bool TryParseCustomHandle(
        string handleName,
        out int pathIndex,
        out int segmentIndex)
    {
        pathIndex = -1;
        segmentIndex = -1;
        var parts = handleName.Split(':');
        return parts.Length == 3 &&
               parts[0] == "custom" &&
               int.TryParse(parts[1], out pathIndex) &&
               int.TryParse(parts[2], out segmentIndex);
    }

    private static double PathWidth(CustomGeometryPath path, LayoutRect boundsDip) =>
        path.PathW > 0 ? path.PathW : Math.Max(1, boundsDip.Width);

    private static double PathHeight(CustomGeometryPath path, LayoutRect boundsDip) =>
        path.PathH > 0 ? path.PathH : Math.Max(1, boundsDip.Height);
}
