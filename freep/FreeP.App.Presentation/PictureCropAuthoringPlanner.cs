using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct PictureCropValues(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public bool IsDefault => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>
/// Validates picture source-crop edits before they enter the shared command bus. Values are
/// normalized fractions of the source image, matching the model and the a:srcRect contract.
/// </summary>
public static class PictureCropAuthoringPlanner
{
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
}
