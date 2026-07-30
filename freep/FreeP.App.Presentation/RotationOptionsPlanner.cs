using System.Globalization;

namespace FreeP.App.Compositor;

public sealed record RotationOptionsSurfacePlan(
    string Title,
    string RotationLabel,
    string Hint,
    string OkLabel,
    string CancelLabel);

/// <summary>Shared policy for PowerPoint-style exact shape rotation entry.</summary>
public static class RotationOptionsPlanner
{
    public const string CommandId = "freep.arrange.rotation-options";
    public const double MinimumDegrees = -360;
    public const double MaximumDegrees = 360;

    public static RotationOptionsSurfacePlan BuildSurfacePlan() => new(
        "Rotation Options",
        "Rotation (degrees)",
        "Enter an angle from -360 to 360 degrees. Positive values rotate clockwise.",
        "OK",
        "Cancel");

    public static bool TryParse(string? text, out double degrees)
    {
        degrees = 0;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value) || value < MinimumDegrees || value > MaximumDegrees)
            return false;

        degrees = Normalize(value);
        return true;
    }

    public static double Normalize(double degrees)
    {
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
