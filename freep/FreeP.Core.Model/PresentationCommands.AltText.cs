namespace FreeP.Core.Model;

/// <summary>
/// Sets the persistent alternative text description on a specific shape.
/// </summary>
public sealed class SetShapeAlternativeTextCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string? _newTitle;
    private readonly string _newAlternativeText;
    private readonly bool? _newIsDecorative;
    private string _previousTitle = string.Empty;
    private string _previousAlternativeText = string.Empty;
    private bool _previousIsDecorative;

    public SetShapeAlternativeTextCommand(
        int slideIndex,
        uint shapeId,
        string? alternativeText,
        string? title = null,
        bool? isDecorative = null)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newTitle = title is null ? null : Normalize(title);
        _newAlternativeText = Normalize(alternativeText);
        _newIsDecorative = isDecorative;
    }

    public string Label => _newIsDecorative == true
        ? "Mark Decorative"
        : string.IsNullOrEmpty(_newAlternativeText)
        ? "Clear Alternative Text"
        : "Set Alternative Text";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null
            && ((_newTitle is not null && shape.AlternativeTextTitle != _newTitle)
                || shape.AlternativeText != _newAlternativeText
                || (_newIsDecorative.HasValue && shape.IsDecorative != _newIsDecorative.Value));
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
        {
            return;
        }

        _previousTitle = shape.AlternativeTextTitle;
        _previousAlternativeText = shape.AlternativeText;
        _previousIsDecorative = shape.IsDecorative;
        if (_newTitle is not null)
        {
            shape.AlternativeTextTitle = _newTitle;
        }

        shape.AlternativeText = _newAlternativeText;
        if (_newIsDecorative.HasValue)
        {
            shape.IsDecorative = _newIsDecorative.Value;
        }
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
        {
            return;
        }

        shape.AlternativeTextTitle = _previousTitle;
        shape.AlternativeText = _previousAlternativeText;
        shape.IsDecorative = _previousIsDecorative;
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
