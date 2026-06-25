using Free.Shared.Commands;

namespace FreeP.Core.Model;

/// <summary>A reversible edit to a <see cref="Presentation"/>. Mirrors FreeW's IDocumentCommand shape.</summary>
public interface IPresentationCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    void Apply(Presentation presentation);
    void Revert(Presentation presentation);
}

/// <summary>
/// FreeP's undo/redo command bus. As in FreeW, the mechanics — paired stacks, depth/byte budget, redo
/// invalidation — are the shared <see cref="UndoRedoStack{TCommand,TPayload}"/>; this bus only adds the
/// presentation-command apply/revert and a change notification.
/// </summary>
public sealed class PresentationCommandBus
{
    private const int MaxDepth = 200;
    private const int MaxBytes = 50 * 1024 * 1024;

    private readonly UndoRedoStack<IPresentationCommand, object?> _stack = new(MaxDepth, MaxBytes);
    private readonly Presentation _presentation;

    public PresentationCommandBus(Presentation presentation) => _presentation = presentation;

    /// <summary>Raised after any execute/undo/redo so a view can refresh.</summary>
    public event Action? Changed;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    /// <summary>Applies a command and records it for undo (invalidating the redo history).</summary>
    public void Execute(IPresentationCommand command)
    {
        command.Apply(_presentation);
        _stack.Push(command, command.EstimatedBytes, payload: null, command.Label);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!_stack.CanUndo)
            return;
        var entry = _stack.PopUndo();
        entry.Command.Revert(_presentation);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_stack.CanRedo)
            return;
        var entry = _stack.PopRedo();
        entry.Command.Apply(_presentation);
        _stack.PushWithoutClearingRedo(entry);
        Changed?.Invoke();
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SLIDE COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inserts a slide at <paramref name="index"/> (appends at end if index == Count).
/// Revert removes it by reference.
/// </summary>
public sealed class InsertSlideCommand : IPresentationCommand
{
    private readonly int _index;
    private readonly Slide _slide;

    public InsertSlideCommand(int index, Slide slide)
    {
        _index = index;
        _slide = slide;
    }

    public string Label => "Insert Slide";

    public void Apply(Presentation p)
    {
        var idx = Math.Clamp(_index, 0, p.Slides.Count);
        p.Slides.Insert(idx, _slide);
    }

    public void Revert(Presentation p) => p.Slides.Remove(_slide);
}

/// <summary>
/// Appends a new blank slide. Kept for backward compatibility with existing callers.
/// </summary>
public sealed class AddSlideCommand : IPresentationCommand
{
    private readonly Slide _slide;
    public AddSlideCommand(Slide slide) => _slide = slide;
    public string Label => "Add Slide";
    public void Apply(Presentation p) => p.Slides.Add(_slide);
    public void Revert(Presentation p) => p.Slides.Remove(_slide);
}

/// <summary>
/// Deletes the slide at <paramref name="index"/>. Captures the slide instance + its original
/// index for undo.
/// </summary>
public sealed class DeleteSlideCommand : IPresentationCommand
{
    private readonly int _index;
    private Slide? _captured;

    public DeleteSlideCommand(int index) => _index = index;

    public string Label => "Delete Slide";

    public void Apply(Presentation p)
    {
        if (_index < 0 || _index >= p.Slides.Count)
            return;
        _captured = p.Slides[_index];
        p.Slides.RemoveAt(_index);
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        var idx = Math.Clamp(_index, 0, p.Slides.Count);
        p.Slides.Insert(idx, _captured);
    }
}

/// <summary>
/// Deep-clones the slide at <paramref name="sourceIndex"/> and inserts it immediately after.
/// Undo removes the duplicate.
/// </summary>
public sealed class DuplicateSlideCommand : IPresentationCommand
{
    private readonly int _sourceIndex;
    private Slide? _duplicate;

    public DuplicateSlideCommand(int sourceIndex) => _sourceIndex = sourceIndex;

    public string Label => "Duplicate Slide";

    public void Apply(Presentation p)
    {
        if (_sourceIndex < 0 || _sourceIndex >= p.Slides.Count)
            return;
        _duplicate = SlideCloner.CloneSlide(p.Slides[_sourceIndex]);
        p.Slides.Insert(_sourceIndex + 1, _duplicate);
    }

    public void Revert(Presentation p)
    {
        if (_duplicate is not null)
            p.Slides.Remove(_duplicate);
    }
}

/// <summary>
/// Moves the slide at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
/// Both indices are clamped to valid range. Revert moves it back.
/// </summary>
public sealed class MoveSlideCommand : IPresentationCommand
{
    private readonly int _from;
    private readonly int _to;

    public MoveSlideCommand(int from, int to)
    {
        _from = from;
        _to   = to;
    }

    public string Label => "Move Slide";

    public void Apply(Presentation p) => MoveInList(p.Slides, _from, _to);
    public void Revert(Presentation p) => MoveInList(p.Slides, _to, _from);

    private static void MoveInList<T>(List<T> list, int from, int to)
    {
        if (from == to || from < 0 || from >= list.Count) return;
        var item = list[from];
        list.RemoveAt(from);
        var dest = Math.Clamp(to, 0, list.Count);
        list.Insert(dest, item);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SHAPE COMMANDS — helpers
// ════════════════════════════════════════════════════════════════════════════════

file static class ShapeHelper
{
    internal static SlideShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return p.Slides[slideIndex].Shapes.FirstOrDefault(s => s.Id == shapeId);
    }

    internal static List<SlideShape>? Shapes(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return p.Slides[slideIndex].Shapes;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// SHAPE COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>Adds <paramref name="shape"/> to the slide at <paramref name="slideIndex"/>.</summary>
public sealed class AddShapeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly SlideShape _shape;

    public AddShapeCommand(int slideIndex, SlideShape shape)
    {
        _slideIndex = slideIndex;
        _shape      = shape;
    }

    public string Label => "Add Shape";
    public void Apply(Presentation p)  => ShapeHelper.Shapes(p, _slideIndex)?.Add(_shape);
    public void Revert(Presentation p) => ShapeHelper.Shapes(p, _slideIndex)?.Remove(_shape);
}

/// <summary>
/// Removes the shape identified by <paramref name="shapeId"/> from the slide.
/// Captures the shape + its z-index for undo.
/// </summary>
public sealed class DeleteShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private SlideShape? _captured;
    private int         _capturedIndex;

    public DeleteShapeCommand(int slideIndex, uint shapeId)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
    }

    public string Label => "Delete Shape";

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null) return;
        _capturedIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_capturedIndex < 0) return;
        _captured = shapes[_capturedIndex];
        shapes.RemoveAt(_capturedIndex);
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null) return;
        var idx = Math.Clamp(_capturedIndex, 0, shapes.Count);
        shapes.Insert(idx, _captured);
    }
}

/// <summary>
/// Translates a shape by (<paramref name="dxEmu"/>, <paramref name="dyEmu"/>).
/// Revert subtracts the same delta.
/// </summary>
public sealed class MoveShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly long _dx;
    private readonly long _dy;

    public MoveShapeCommand(int slideIndex, uint shapeId, long dxEmu, long dyEmu)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _dx         = dxEmu;
        _dy         = dyEmu;
    }

    public string Label => "Move Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu += _dx;
        s.OffsetYEmu += _dy;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu -= _dx;
        s.OffsetYEmu -= _dy;
    }
}

/// <summary>
/// Sets the absolute position and size of a shape, capturing prior values for undo.
/// </summary>
public sealed class ResizeShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly long _newOffsetX;
    private readonly long _newOffsetY;
    private readonly long _newCx;
    private readonly long _newCy;
    private long _oldOffsetX, _oldOffsetY, _oldCx, _oldCy;

    public ResizeShapeCommand(int slideIndex, uint shapeId, long newOffsetX, long newOffsetY, long newCx, long newCy)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newOffsetX = newOffsetX;
        _newOffsetY = newOffsetY;
        _newCx      = newCx;
        _newCy      = newCy;
    }

    public string Label => "Resize Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldOffsetX = s.OffsetXEmu;
        _oldOffsetY = s.OffsetYEmu;
        _oldCx      = s.ExtentCxEmu;
        _oldCy      = s.ExtentCyEmu;
        s.OffsetXEmu  = _newOffsetX;
        s.OffsetYEmu  = _newOffsetY;
        s.ExtentCxEmu = _newCx;
        s.ExtentCyEmu = _newCy;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu  = _oldOffsetX;
        s.OffsetYEmu  = _oldOffsetY;
        s.ExtentCxEmu = _oldCx;
        s.ExtentCyEmu = _oldCy;
    }
}

/// <summary>Sets the rotation of a shape; captures old rotation for undo.</summary>
public sealed class RotateShapeCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly double _newRotationDeg;
    private double          _oldRotationDeg;

    public RotateShapeCommand(int slideIndex, uint shapeId, double newRotationDeg)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _newRotationDeg = newRotationDeg;
    }

    public string Label => "Rotate Shape";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldRotationDeg = s.RotationDeg;
        s.RotationDeg   = _newRotationDeg;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.RotationDeg = _oldRotationDeg;
    }
}

/// <summary>Replaces the fill of a shape; captures old fill for undo.</summary>
public sealed class SetShapeFillCommand : IPresentationCommand
{
    private readonly int        _slideIndex;
    private readonly uint       _shapeId;
    private readonly ShapeFill? _newFill;
    private ShapeFill?          _oldFill;

    public SetShapeFillCommand(int slideIndex, uint shapeId, ShapeFill? newFill)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newFill    = newFill;
    }

    public string Label => "Set Fill";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldFill = s.Fill;
        s.Fill   = _newFill;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.Fill = _oldFill;
    }
}

/// <summary>Replaces the outline of a shape; captures old outline for undo.</summary>
public sealed class SetShapeOutlineCommand : IPresentationCommand
{
    private readonly int           _slideIndex;
    private readonly uint          _shapeId;
    private readonly ShapeOutline? _newOutline;
    private ShapeOutline?          _oldOutline;

    public SetShapeOutlineCommand(int slideIndex, uint shapeId, ShapeOutline? newOutline)
    {
        _slideIndex  = slideIndex;
        _shapeId     = shapeId;
        _newOutline  = newOutline;
    }

    public string Label => "Set Outline";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldOutline = s.Outline;
        s.Outline   = _newOutline;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.Outline = _oldOutline;
    }
}

/// <summary>
/// Moves a shape to a specific z-index (position in the Shapes list).
/// The shape list is painter's order (index 0 = back). Captures old index for undo.
/// </summary>
public sealed class ReorderShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _newZIndex;
    private int           _oldZIndex;

    public ReorderShapeCommand(int slideIndex, uint shapeId, int newZIndex)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newZIndex  = newZIndex;
    }

    public string Label => "Reorder Shape";

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null) return;
        _oldZIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_oldZIndex < 0) return;
        MoveInList(shapes, _oldZIndex, _newZIndex);
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper.Shapes(p, _slideIndex);
        if (shapes is null || _oldZIndex < 0) return;
        MoveInList(shapes, _newZIndex, _oldZIndex);
    }

    private static void MoveInList<T>(List<T> list, int from, int to)
    {
        if (from == to || from < 0 || from >= list.Count) return;
        var item = list[from];
        list.RemoveAt(from);
        var dest = Math.Clamp(to, 0, list.Count);
        list.Insert(dest, item);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// TEXT / RUN-FORMAT COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the entire <see cref="TextBody"/> of a shape, capturing the old body for undo.
/// Used for whole-body replace (e.g. paste rich text).
/// </summary>
public sealed class SetShapeTextCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly uint      _shapeId;
    private readonly TextBody? _newBody;
    private TextBody?          _oldBody;

    public SetShapeTextCommand(int slideIndex, uint shapeId, TextBody? newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newBody    = newBody;
    }

    public string Label => "Set Text";

    public void Apply(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        _oldBody   = s.TextBody;
        s.TextBody = _newBody;
    }

    public void Revert(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.TextBody = _oldBody;
    }
}

/// <summary>
/// Base for run-format toggle commands that operate over a single run identified by
/// (slideIndex, shapeId, paragraphIndex, runIndex).
/// Apply/Revert are symmetric (toggle). Captures old value for non-toggle set commands.
/// </summary>
public abstract class RunFormatCommandBase : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _paragraphIndex;
    private readonly int  _runIndex;

    protected RunFormatCommandBase(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
    }

    public abstract string Label { get; }

    public void Apply(Presentation p)   => WithRun(p, ApplyToRun);
    public void Revert(Presentation p)  => WithRun(p, RevertFromRun);

    protected abstract void ApplyToRun(Run run);
    protected abstract void RevertFromRun(Run run);

    private void WithRun(Presentation p, Action<Run> action)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return;
        action(para.Runs[_runIndex]);
    }
}

/// <summary>Toggles bold on a single run.</summary>
public sealed class ToggleRunBoldCommand : RunFormatCommandBase
{
    public ToggleRunBoldCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Bold";
    protected override void ApplyToRun(Run r)   => r.Bold = !r.Bold;
    protected override void RevertFromRun(Run r) => r.Bold = !r.Bold;
}

/// <summary>Toggles italic on a single run.</summary>
public sealed class ToggleRunItalicCommand : RunFormatCommandBase
{
    public ToggleRunItalicCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Italic";
    protected override void ApplyToRun(Run r)   => r.Italic = !r.Italic;
    protected override void RevertFromRun(Run r) => r.Italic = !r.Italic;
}

/// <summary>Toggles underline on a single run.</summary>
public sealed class ToggleRunUnderlineCommand : RunFormatCommandBase
{
    public ToggleRunUnderlineCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Underline";
    protected override void ApplyToRun(Run r)   => r.Underline = !r.Underline;
    protected override void RevertFromRun(Run r) => r.Underline = !r.Underline;
}

/// <summary>Sets the font family on a single run; captures old value for undo.</summary>
public sealed class SetRunFontCommand : IPresentationCommand
{
    private readonly int     _slideIndex;
    private readonly uint    _shapeId;
    private readonly int     _paragraphIndex;
    private readonly int     _runIndex;
    private readonly string? _newFont;
    private string?          _oldFont;

    public SetRunFontCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, string? newFont)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newFont        = newFont;
    }

    public string Label => "Set Font";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldFont        = run.FontFamily;
        run.FontFamily  = _newFont;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.FontFamily = _oldFont;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}

/// <summary>Sets the font size on a single run; captures old value for undo.</summary>
public sealed class SetRunFontSizeCommand : IPresentationCommand
{
    private readonly int     _slideIndex;
    private readonly uint    _shapeId;
    private readonly int     _paragraphIndex;
    private readonly int     _runIndex;
    private readonly double? _newSize;
    private double?          _oldSize;

    public SetRunFontSizeCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, double? newSizePt)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newSize        = newSizePt;
    }

    public string Label => "Set Font Size";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldSize       = run.FontSizePt;
        run.FontSizePt = _newSize;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.FontSizePt = _oldSize;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}

/// <summary>Sets the color on a single run; captures old value for undo.</summary>
public sealed class SetRunColorCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly uint             _shapeId;
    private readonly int              _paragraphIndex;
    private readonly int              _runIndex;
    private readonly ThemeAwareColor? _newColor;
    private ThemeAwareColor?          _oldColor;

    public SetRunColorCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex, ThemeAwareColor? newColor)
    {
        _slideIndex     = slideIndex;
        _shapeId        = shapeId;
        _paragraphIndex = paragraphIndex;
        _runIndex       = runIndex;
        _newColor       = newColor;
    }

    public string Label => "Set Color";

    public void Apply(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        _oldColor  = run.Color;
        run.Color  = _newColor;
    }

    public void Revert(Presentation p)
    {
        var run = GetRun(p);
        if (run is null) return;
        run.Color = _oldColor;
    }

    private Run? GetRun(Presentation p)
    {
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s?.TextBody is null) return null;
        if (_paragraphIndex < 0 || _paragraphIndex >= s.TextBody.Paragraphs.Count) return null;
        var para = s.TextBody.Paragraphs[_paragraphIndex];
        if (_runIndex < 0 || _runIndex >= para.Runs.Count) return null;
        return para.Runs[_runIndex];
    }
}
