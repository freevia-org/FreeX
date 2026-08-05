using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed class RotationOptionsDialogSession
{
    public const string InvalidInputMessage =
        "Enter a finite angle from -360 to 360 degrees.";

    private readonly EditingSession _editor;

    public RotationOptionsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Surface = RotationOptionsPlanner.BuildSurfacePlan();
        InitialRotation = editor.SelectedShapeIds
            .Select(id => editor.CurrentSlide is { } slide
                ? SlideShapeTraversal.FindById(slide, id)
                : null)
            .FirstOrDefault(shape => shape is not null)?.RotationDeg ?? 0;
        InitialRotationText = InitialRotation.ToString(
            "G",
            CultureInfo.CurrentCulture);
    }

    public RotationOptionsSurfacePlan Surface { get; }

    public double InitialRotation { get; }

    public string InitialRotationText { get; }

    public bool TryParse(string? text, out double degrees) =>
        RotationOptionsPlanner.TryParse(text, out degrees);

    public bool TryApply(string? text)
    {
        if (!TryParse(text, out var degrees))
            return false;

        _editor.SetSelectedRotation(degrees);
        return true;
    }
}
