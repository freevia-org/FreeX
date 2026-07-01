namespace FreeP.Core.Model;

/// <summary>
/// Sets the persistent alternative text description on a specific shape.
/// </summary>
public sealed class SetShapeAlternativeTextCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _newAlternativeText;
    private string _previousAlternativeText = string.Empty;

    public SetShapeAlternativeTextCommand(int slideIndex, uint shapeId, string? alternativeText)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newAlternativeText = Normalize(alternativeText);
    }

    public string Label => string.IsNullOrEmpty(_newAlternativeText)
        ? "Clear Alternative Text"
        : "Set Alternative Text";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null && shape.AlternativeText != _newAlternativeText;
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
        {
            return;
        }

        _previousAlternativeText = shape.AlternativeText;
        shape.AlternativeText = _newAlternativeText;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
        {
            return;
        }

        shape.AlternativeText = _previousAlternativeText;
    }

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
        {
            return null;
        }

        return FindShapeById(presentation.Slides[_slideIndex].Shapes, _shapeId);
    }

    private static SlideShape? FindShapeById(IEnumerable<SlideShape> shapes, uint id)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == id)
            {
                return shape;
            }

            var child = FindShapeById(shape.Children, id);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
