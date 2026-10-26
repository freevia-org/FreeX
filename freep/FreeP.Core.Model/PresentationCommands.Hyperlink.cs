namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// WAVE 11A COMMANDS — shape-level and run-level hyperlinks
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sets (or removes) the shape-level hyperlink on a specific shape.
/// Undoable: stores the previous hyperlink for revert.
/// </summary>
public sealed class SetShapeHyperlinkCommand : IPresentationCommand
{
    private readonly int      _slideIndex;
    private readonly uint     _shapeId;
    private readonly Hyperlink? _newLink;
    private Hyperlink?        _prevLink;

    public SetShapeHyperlinkCommand(int slideIndex, uint shapeId, Hyperlink? link)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newLink    = link;
    }

    public string Label => _newLink is null ? "Remove Shape Link" : "Set Shape Link";

    // r200: Remove Link on a shape that has none, or setting the link it already has, changes
    // nothing -- and the undo entry the bus would push clears the redo stack.
    public bool HasEffect(Presentation p) =>
        FindShape(p) is { } shape && !Equals(shape.Hyperlink, _newLink);

    public void Apply(Presentation p)
    {
        var shape = FindShape(p);
        if (shape is null) return;
        _prevLink       = shape.Hyperlink;
        shape.Hyperlink = _newLink;
    }

    public void Revert(Presentation p)
    {
        var shape = FindShape(p);
        if (shape is null) return;
        shape.Hyperlink = _prevLink;
    }

    private SlideShape? FindShape(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return null;
        return FindShapeById(p.Slides[_slideIndex].Shapes, _shapeId);
    }

    private static SlideShape? FindShapeById(IEnumerable<SlideShape> shapes, uint id)
    {
        foreach (var s in shapes)
        {
            if (s.Id == id) return s;
            var found = FindShapeById(s.Children, id);
            if (found is not null) return found;
        }
        return null;
    }
}
