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

/// <summary>Result of reducing a pointer position to one preset guide value.</summary>
public sealed record ShapeGeometryAdjustmentMutationPlan(
    bool ShouldApply,
    string? Name,
    double? Value,
    string? DisabledReason);

/// <summary>
/// Shared planning for PowerPoint-style preset-shape edit points.
/// The first supported geometry is Chord because its two DrawingML guides are explicit angles
/// and the compositor already consumes both <c>adj1</c> and <c>adj2</c>.
/// </summary>
public static class ShapeGeometryAdjustmentPlanner
{
    private const double AngleUnitsPerDegree = 60000.0;
    private const double FullCircle = 360.0 * AngleUnitsPerDegree;
    private const double DefaultStartAngle = 0;
    private const double DefaultEndAngle = 180 * AngleUnitsPerDegree;

    public const string UnsupportedShapeMessage =
        "This preset shape does not expose shared edit points yet.";
    public const string InvalidHandleMessage = "Select a valid shape edit point.";

    public static ShapeGeometryAdjustmentPlan Build(SlideShape shape, LayoutRect boundsDip)
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (shape.Kind != SlideShapeKind.AutoShape || shape.AutoShapeKind != DrawingShapeKind.Chord)
        {
            return new ShapeGeometryAdjustmentPlan(
                shape.Id,
                CanEdit: false,
                UnsupportedShapeMessage,
                Array.Empty<ShapeGeometryAdjustmentHandlePlan>());
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
}
