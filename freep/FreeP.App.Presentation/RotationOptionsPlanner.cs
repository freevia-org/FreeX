using System.Globalization;

namespace FreeP.App.Compositor;

public enum RotationOptionsDialogField
{
    Rotation,
    Hint,
}

public enum RotationOptionsDialogAction
{
    Accept,
    Cancel,
}

public sealed record RotationOptionsSurfacePlan(
    PresentationDialogSurfacePlan<RotationOptionsDialogField, RotationOptionsDialogAction> Schema)
{
    public string Title => Schema.Title;

    public string RotationLabel => Field(RotationOptionsDialogField.Rotation).Label;

    public string Hint => Field(RotationOptionsDialogField.Hint).Label;

    public string OkLabel => Action(RotationOptionsDialogAction.Accept).Label;

    public string CancelLabel => Action(RotationOptionsDialogAction.Cancel).Label;

    public PresentationDialogFieldPlan<RotationOptionsDialogField> Field(
        RotationOptionsDialogField field) => Schema.Field(field);

    public PresentationDialogActionPlan<RotationOptionsDialogAction> Action(
        RotationOptionsDialogAction action) => Schema.Action(action);
}

/// <summary>Shared policy for PowerPoint-style exact shape rotation entry.</summary>
public static class RotationOptionsPlanner
{
    public const string CommandId = "freep.arrange.rotation-options";
    public const double MinimumDegrees = -360;
    public const double MaximumDegrees = 360;

    public static RotationOptionsSurfacePlan Surface { get; } = new(
        new PresentationDialogSurfacePlan<RotationOptionsDialogField, RotationOptionsDialogAction>(
            "Rotation Options",
            "Rotation Options dialog",
            "FreeP.RotationOptions.Window",
            [
                new(
                    RotationOptionsDialogField.Rotation,
                    PresentationDialogControlKind.Text,
                    "Rotation (degrees)",
                    "Rotation angle in degrees",
                    "FreeP.RotationOptions.Rotation",
                    "Enter a finite angle from -360 to 360 degrees."),
                new(
                    RotationOptionsDialogField.Hint,
                    PresentationDialogControlKind.Label,
                    "Enter an angle from -360 to 360 degrees. Positive values rotate clockwise.",
                    "Rotation angle guidance",
                    "FreeP.RotationOptions.Hint"),
            ],
            [
                new(
                    RotationOptionsDialogAction.Accept,
                    "OK",
                    "Apply rotation",
                    "FreeP.RotationOptions.Accept",
                    IsDefault: true),
                new(
                    RotationOptionsDialogAction.Cancel,
                    "Cancel",
                    "Cancel rotation",
                    "FreeP.RotationOptions.Cancel",
                    IsCancel: true),
            ]));

    public static RotationOptionsSurfacePlan BuildSurfacePlan() => Surface;

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
