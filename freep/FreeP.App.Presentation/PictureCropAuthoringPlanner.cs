using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public readonly record struct PictureCropValues(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public bool IsDefault => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

public sealed record PictureCropHandlePlan(string Name, LayoutPoint PositionDip);

public sealed record PictureCropHandleSet(
    uint ShapeId,
    bool CanEdit,
    string? DisabledReason,
    IReadOnlyList<PictureCropHandlePlan> Handles);

public sealed record PictureCropMutationPlan(
    bool ShouldApply,
    PictureCropValues? Values,
    string? DisabledReason);

/// <summary>
/// Validates picture source-crop edits before they enter the shared command bus. Values are
/// normalized fractions of the source image, matching the model and the a:srcRect contract.
/// </summary>
public static class PictureCropAuthoringPlanner
{
    public const string LeftHandleName = "crop-left";
    public const string TopHandleName = "crop-top";
    public const string RightHandleName = "crop-right";
    public const string BottomHandleName = "crop-bottom";

    private const double MinimumVisibleFraction = 0.000001;

    public const string InsetCommandId = "freep.picture.crop-inset";
    public const string ResetCommandId = "freep.picture.crop-reset";

    public static bool TryPlan(
        double left,
        double top,
        double right,
        double bottom,
        out PictureCropValues values)
    {
        values = default;
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom) ||
            double.IsInfinity(left) || double.IsInfinity(top) || double.IsInfinity(right) || double.IsInfinity(bottom) ||
            left < 0 || top < 0 || right < 0 || bottom < 0 ||
            left + right >= 1 || top + bottom >= 1)
            return false;

        values = new PictureCropValues(left, top, right, bottom);
        return true;
    }

    public static PictureCropValues Reset() => default;

    public static PictureCropValues Inset(double fraction = 0.1)
    {
        if (!TryPlan(fraction, fraction, fraction, fraction, out var values))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        return values;
    }

    /// <summary>Builds the four edge handles used by the interactive crop gesture.</summary>
    public static PictureCropHandleSet Build(SlideShape shape, LayoutRect boundsDip)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.Kind != SlideShapeKind.Picture || shape.Picture is null ||
            boundsDip.Width <= 0 || boundsDip.Height <= 0)
        {
            return new PictureCropHandleSet(
                shape.Id,
                CanEdit: false,
                "Select a picture with a valid frame.",
                Array.Empty<PictureCropHandlePlan>());
        }

        var values = ReadValues(shape);
        return new PictureCropHandleSet(
            shape.Id,
            CanEdit: true,
            DisabledReason: null,
            [
                new(LeftHandleName, PositionFor(boundsDip, values, LeftHandleName)),
                new(TopHandleName, PositionFor(boundsDip, values, TopHandleName)),
                new(RightHandleName, PositionFor(boundsDip, values, RightHandleName)),
                new(BottomHandleName, PositionFor(boundsDip, values, BottomHandleName)),
            ]);
    }

    /// <summary>Reduces a slide-space pointer position to one validated crop command.</summary>
    public static PictureCropMutationPlan BuildMutationPlan(
        SlideShape shape,
        LayoutRect boundsDip,
        string handleName,
        LayoutPoint pointerDip)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var handles = Build(shape, boundsDip);
        if (!handles.CanEdit || !IsHandle(handleName))
            return new(false, null, handles.DisabledReason ?? "Select a valid picture crop handle.");

        var current = ReadValues(shape);
        var next = handleName switch
        {
            // The opposite edge's stored fraction can legally already be as high as 1.0 (a
            // file-authored srcRect can crop 100% from one side). Without the Math.Max(0, ...)
            // floor, "1 - opposite - MinimumVisibleFraction" would go negative and Math.Clamp's
            // min(0) > max would throw ArgumentException on the very first pointer move.
            // PowerPoint's own crop handles stop dead at that minimum instead of letting the
            // rectangle invert, so this edge is pinned to 0 (fully open) rather than crossing
            // the opposite edge.
            LeftHandleName => current with
            {
                Left = Math.Clamp(
                    (pointerDip.X - boundsDip.Left) / boundsDip.Width,
                    0,
                    Math.Max(0, 1 - current.Right - MinimumVisibleFraction))
            },
            TopHandleName => current with
            {
                Top = Math.Clamp(
                    (pointerDip.Y - boundsDip.Top) / boundsDip.Height,
                    0,
                    Math.Max(0, 1 - current.Bottom - MinimumVisibleFraction))
            },
            RightHandleName => current with
            {
                Right = Math.Clamp(
                    (boundsDip.Right - pointerDip.X) / boundsDip.Width,
                    0,
                    Math.Max(0, 1 - current.Left - MinimumVisibleFraction))
            },
            BottomHandleName => current with
            {
                Bottom = Math.Clamp(
                    (boundsDip.Bottom - pointerDip.Y) / boundsDip.Height,
                    0,
                    Math.Max(0, 1 - current.Top - MinimumVisibleFraction))
            },
            _ => current,
        };

        return new PictureCropMutationPlan(!next.Equals(current), next, null);
    }

    public static bool IsHandle(string? handleName) => handleName is
        LeftHandleName or TopHandleName or RightHandleName or BottomHandleName;

    public static LayoutPoint PositionFor(
        LayoutRect boundsDip,
        PictureCropValues values,
        string handleName) => handleName switch
        {
            LeftHandleName => new LayoutPoint(boundsDip.Left + values.Left * boundsDip.Width, boundsDip.Top + boundsDip.Height / 2),
            TopHandleName => new LayoutPoint(boundsDip.Left + boundsDip.Width / 2, boundsDip.Top + values.Top * boundsDip.Height),
            RightHandleName => new LayoutPoint(boundsDip.Right - values.Right * boundsDip.Width, boundsDip.Top + boundsDip.Height / 2),
            BottomHandleName => new LayoutPoint(boundsDip.Left + boundsDip.Width / 2, boundsDip.Bottom - values.Bottom * boundsDip.Height),
            _ => new LayoutPoint(boundsDip.Left, boundsDip.Top),
        };

    private static PictureCropValues ReadValues(SlideShape shape)
    {
        var format = shape.PictureFormat;
        return new PictureCropValues(
            format?.CropLeft ?? 0,
            format?.CropTop ?? 0,
            format?.CropRight ?? 0,
            format?.CropBottom ?? 0);
    }
}
