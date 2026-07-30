using System.Linq;
using Free.Shared.Commands;
using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>A reversible edit to a <see cref="Presentation"/>. Mirrors FreeW's IDocumentCommand shape.</summary>
public interface IPresentationCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    void Apply(Presentation presentation);
    void Revert(Presentation presentation);

    /// <summary>
    /// Whether executing this command would actually change the presentation. When false, the bus
    /// skips it entirely (no Apply, no undo entry) so no-op edits don't pollute the undo history.
    /// Defaults to true — commands that can be invoked on a target where they'd do nothing
    /// (e.g. splitting an unmerged cell) override this.
    /// </summary>
    bool HasEffect(Presentation presentation) => true;
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
        // Skip no-op commands entirely so they don't create an empty undo entry.
        if (!command.HasEffect(_presentation))
            return;
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

/// <summary>
/// Applies a prepared SmartArt state as one undoable operation. Hosts prepare the state through
/// the shared SmartArt planner (including data-part and drawing-cache regeneration), then this
/// command owns the model transition so Undo/Redo restores the complete payload together.
/// </summary>
public sealed class ReplaceSmartArtCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly SmartArtShape _before;
    private readonly SmartArtShape _after;

    public ReplaceSmartArtCommand(
        int slideIndex,
        uint shapeId,
        SmartArtShape before,
        SmartArtShape after)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _before = SlideCloner.CloneSmartArt(before);
        _after = SlideCloner.CloneSmartArt(after);
    }

    public string Label => "Edit SmartArt";

    public void Apply(Presentation presentation) => CopyState(presentation, _after);

    public void Revert(Presentation presentation) => CopyState(presentation, _before);

    private void CopyState(Presentation presentation, SmartArtShape state)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return;

        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.SmartArt is not null)
            SlideCloner.CopySmartArt(shape.SmartArt, state);
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

/// <summary>Sets whether a slide is skipped during slide-show playback.</summary>
public sealed class SetSlideHiddenCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly bool _newValue;
    private bool _oldValue;

    public SetSlideHiddenCommand(int slideIndex, bool hidden)
    {
        _slideIndex = slideIndex;
        _newValue = hidden;
    }

    public string Label => _newValue ? "Hide Slide" : "Show Slide";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        p.Slides[_slideIndex].IsHidden != _newValue;

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        _oldValue = p.Slides[_slideIndex].IsHidden;
        p.Slides[_slideIndex].IsHidden = _newValue;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex >= 0 && _slideIndex < p.Slides.Count)
            p.Slides[_slideIndex].IsHidden = _oldValue;
    }
}

/// <summary>Sets whether a slide object, including a grouped child, is hidden in the editing view.</summary>
public sealed class SetShapeHiddenCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly bool _newValue;
    private bool _oldValue;

    public SetShapeHiddenCommand(int slideIndex, uint shapeId, bool hidden)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newValue = hidden;
    }

    public string Label => _newValue ? "Hide Object" : "Show Object";

    public bool HasEffect(Presentation p) =>
        TryGetShape(p, out var shape) && shape.IsHidden != _newValue;

    public void Apply(Presentation p)
    {
        if (!TryGetShape(p, out var shape))
            return;

        _oldValue = shape.IsHidden;
        shape.IsHidden = _newValue;
    }

    public void Revert(Presentation p)
    {
        if (TryGetShape(p, out var shape))
            shape.IsHidden = _oldValue;
    }

    private bool TryGetShape(Presentation p, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        shape = FindShape(p.Slides[_slideIndex].Shapes, _shapeId)!;
        return shape is not null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>Renames a slide object, including a grouped child, as one undoable edit.</summary>
public sealed class SetShapeNameCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _newName;
    private string _oldName = string.Empty;

    public SetShapeNameCommand(int slideIndex, uint shapeId, string newName)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newName = newName.Trim();
    }

    public string Label => "Rename Object";

    public bool HasEffect(Presentation p) =>
        TryGetShape(p, out var shape) &&
        _newName.Length > 0 &&
        !string.Equals(shape.Name, _newName, StringComparison.Ordinal);

    public void Apply(Presentation p)
    {
        if (!TryGetShape(p, out var shape))
            return;

        _oldName = shape.Name;
        shape.Name = _newName;
    }

    public void Revert(Presentation p)
    {
        if (TryGetShape(p, out var shape))
            shape.Name = _oldName;
    }

    private bool TryGetShape(Presentation p, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        shape = FindShape(p.Slides[_slideIndex].Shapes, _shapeId)!;
        return shape is not null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}

/// <summary>
/// Sets the title metadata for a slide. Revert restores the previous title.
/// </summary>
public sealed class SetSlideTitleCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string _newTitle;
    private string? _oldTitle;

    public SetSlideTitleCommand(int slideIndex, string title)
    {
        _slideIndex = slideIndex;
        _newTitle = title;
    }

    public string Label => "Set Slide Title";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        !StringComparer.Ordinal.Equals(p.Slides[_slideIndex].Title, _newTitle);

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        _oldTitle = slide.Title;
        slide.Title = _newTitle;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        p.Slides[_slideIndex].Title = _oldTitle ?? string.Empty;
    }
}

/// <summary>
/// Assigns a slide to an existing presentation layout. Revert restores the prior layout id.
/// </summary>
public sealed class SetSlideLayoutCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string _newLayoutId;
    private string? _oldLayoutId;
    private bool _initialized;
    private readonly List<PlaceholderGeometryState> _updatedPlaceholders = new();
    private readonly List<SlideShape> _addedPlaceholders = new();

    public SetSlideLayoutCommand(int slideIndex, string layoutId)
    {
        _slideIndex = slideIndex;
        _newLayoutId = layoutId;
    }

    public string Label => "Set Slide Layout";

    public bool HasEffect(Presentation p) =>
        _slideIndex >= 0 &&
        _slideIndex < p.Slides.Count &&
        p.Layouts.Any(layout => StringComparer.Ordinal.Equals(layout.Id, _newLayoutId)) &&
        !StringComparer.Ordinal.Equals(p.Slides[_slideIndex].LayoutId, _newLayoutId);

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        if (!p.Layouts.Any(layout => StringComparer.Ordinal.Equals(layout.Id, _newLayoutId)))
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        var layout = p.Layouts.First(layout =>
            StringComparer.Ordinal.Equals(layout.Id, _newLayoutId));

        if (_initialized)
        {
            slide.LayoutId = _newLayoutId;
            foreach (var state in _updatedPlaceholders)
                state.ApplyTargetGeometry();

            foreach (var placeholder in _addedPlaceholders)
            {
                if (!slide.Shapes.Contains(placeholder))
                    slide.Shapes.Add(placeholder);
            }

            return;
        }

        _oldLayoutId = slide.LayoutId;
        slide.LayoutId = _newLayoutId;

        foreach (var shape in slide.Shapes.ToList())
        {
            var target = FindMatchingPlaceholder(layout, shape.Placeholder);
            if (target is null || !HasGeometry(target))
                continue;

            var state = new PlaceholderGeometryState(shape, target);
            _updatedPlaceholders.Add(state);
            state.ApplyTargetGeometry();
        }

        var nextShapeId = NextShapeId(slide);
        foreach (var target in layout.Placeholders)
        {
            if (!HasGeometry(target) || target.Placeholder is null ||
                slide.Shapes.Any(shape => MatchesPlaceholder(target.Placeholder, shape.Placeholder)))
            {
                continue;
            }

            var placeholder = SlideCloner.CloneShape(target);
            placeholder.Id = nextShapeId++;
            placeholder.TextBody = null;
            placeholder.Name = string.IsNullOrWhiteSpace(target.Name)
                ? $"Placeholder {placeholder.Id}"
                : target.Name;
            slide.Shapes.Add(placeholder);
            _addedPlaceholders.Add(placeholder);
        }

        _initialized = true;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
        {
            return;
        }

        var slide = p.Slides[_slideIndex];
        slide.LayoutId = _oldLayoutId;

        foreach (var state in _updatedPlaceholders)
            state.RestoreOriginalGeometry();

        foreach (var placeholder in _addedPlaceholders)
            slide.Shapes.Remove(placeholder);
    }

    private static SlideShape? FindMatchingPlaceholder(
        SlideLayout layout,
        Placeholder? target) =>
        target is null
            ? null
            : layout.Placeholders.FirstOrDefault(candidate =>
                MatchesPlaceholder(candidate.Placeholder, target));

    private static bool MatchesPlaceholder(Placeholder? candidate, Placeholder? target)
    {
        if (candidate is null || target is null || candidate.Idx != target.Idx)
            return false;

        if (candidate.Type == target.Type)
            return true;

        var title = candidate.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle &&
                    target.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle;
        if (title)
            return true;

        return IsContentPlaceholder(candidate.Type) && IsContentPlaceholder(target.Type);
    }

    private static bool IsContentPlaceholder(PlaceholderType type) => type is
        PlaceholderType.Body or PlaceholderType.Object or PlaceholderType.Chart or
        PlaceholderType.Table or PlaceholderType.ClipArt or PlaceholderType.Diagram or
        PlaceholderType.Media or PlaceholderType.Picture;

    private static bool HasGeometry(SlideShape shape) =>
        shape.ExtentCxEmu > 0 || shape.ExtentCyEmu > 0 || shape.HasExplicitZeroExtentTransform;

    private static uint NextShapeId(Slide slide)
    {
        var max = slide.Shapes
            .SelectMany(EnumerateShapes)
            .Select(shape => shape.Id)
            .DefaultIfEmpty(0u)
            .Max();
        return max == uint.MaxValue ? 1 : max + 1;
    }

    private static IEnumerable<SlideShape> EnumerateShapes(SlideShape shape)
    {
        yield return shape;
        foreach (var child in shape.Children)
        foreach (var descendant in EnumerateShapes(child))
            yield return descendant;
    }

    private sealed class PlaceholderGeometryState
    {
        private readonly SlideShape _shape;
        private readonly SlideShape _target;
        private readonly long _offsetX;
        private readonly long _offsetY;
        private readonly long _extentCx;
        private readonly long _extentCy;
        private readonly double _rotation;
        private readonly bool _flipH;
        private readonly bool _flipV;
        private readonly bool _explicitZero;

        public PlaceholderGeometryState(SlideShape shape, SlideShape target)
        {
            _shape = shape;
            _target = target;
            _offsetX = shape.OffsetXEmu;
            _offsetY = shape.OffsetYEmu;
            _extentCx = shape.ExtentCxEmu;
            _extentCy = shape.ExtentCyEmu;
            _rotation = shape.RotationDeg;
            _flipH = shape.FlipH;
            _flipV = shape.FlipV;
            _explicitZero = shape.HasExplicitZeroExtentTransform;
        }

        public void ApplyTargetGeometry()
        {
            _shape.OffsetXEmu = _target.OffsetXEmu;
            _shape.OffsetYEmu = _target.OffsetYEmu;
            _shape.ExtentCxEmu = _target.ExtentCxEmu;
            _shape.ExtentCyEmu = _target.ExtentCyEmu;
            _shape.RotationDeg = _target.RotationDeg;
            _shape.FlipH = _target.FlipH;
            _shape.FlipV = _target.FlipV;
            _shape.HasExplicitZeroExtentTransform = _target.HasExplicitZeroExtentTransform;
        }

        public void RestoreOriginalGeometry()
        {
            _shape.OffsetXEmu = _offsetX;
            _shape.OffsetYEmu = _offsetY;
            _shape.ExtentCxEmu = _extentCx;
            _shape.ExtentCyEmu = _extentCy;
            _shape.RotationDeg = _rotation;
            _shape.FlipH = _flipH;
            _shape.FlipV = _flipV;
            _shape.HasExplicitZeroExtentTransform = _explicitZero;
        }
    }
}

internal static class ShapeHelper
{
    internal static SlideShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return Find(p.Slides[slideIndex].Shapes, shapeId);
    }

    internal static SlideShape? Find(Slide slide, uint shapeId) =>
        Find(slide.Shapes, shapeId);
    private static SlideShape? Find(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && Find(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    internal static List<SlideShape>? Shapes(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return p.Slides[slideIndex].Shapes;
    }

    internal static IEnumerable<SlideShape> All(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count)
            yield break;

        foreach (var shape in All(p.Slides[slideIndex].Shapes))
            yield return shape;
    }

    private static IEnumerable<SlideShape> All(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in All(shape.Children))
                yield return child;
        }
    }

    internal static List<SlideShape>? FindContainingList(
        Presentation p,
        int slideIndex,
        uint shapeId)
    {
        var shapes = Shapes(p, slideIndex);
        return shapes is null ? null : FindContainingList(shapes, shapeId);
    }

    private static List<SlideShape>? FindContainingList(
        List<SlideShape> shapes,
        uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
            {
                return shapes;
            }

            if (shape.Children.Count > 0 &&
                FindContainingList(shape.Children, shapeId) is { } childList)
            {
                return childList;
            }
        }

        return null;
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
/// Changes one AutoShape's preset geometry while preserving its authored frame, text, and style.
/// The old preset guides/custom paths are captured so the operation is a single undoable edit.
/// </summary>
public sealed class ChangeAutoShapeKindCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly DrawingShapeKind _newKind;
    private DrawingShapeKind _oldKind;
    private Dictionary<string, double>? _oldAdjustments;
    private List<CustomGeometryPath>? _oldCustomGeometry;

    public ChangeAutoShapeKindCommand(int slideIndex, uint shapeId, DrawingShapeKind newKind)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newKind = newKind;
    }

    public string Label => "Change Shape";

    public bool HasEffect(Presentation presentation) =>
        ShapeHelper.Find(presentation, _slideIndex, _shapeId) is
        { Kind: SlideShapeKind.AutoShape } shape &&
        shape.AutoShapeKind != _newKind;

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is not { Kind: SlideShapeKind.AutoShape })
            return;

        _oldKind = shape.AutoShapeKind;
        _oldAdjustments = new Dictionary<string, double>(shape.PresetGeometryAdjustments,
            StringComparer.OrdinalIgnoreCase);
        _oldCustomGeometry = CloneCustomGeometry(shape.CustomGeometry);
        shape.AutoShapeKind = _newKind;
        shape.PresetGeometryAdjustments.Clear();
        shape.CustomGeometry.Clear();
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is not { Kind: SlideShapeKind.AutoShape })
            return;

        shape.AutoShapeKind = _oldKind;
        shape.PresetGeometryAdjustments.Clear();
        if (_oldAdjustments is not null)
        {
            foreach (var pair in _oldAdjustments)
                shape.PresetGeometryAdjustments[pair.Key] = pair.Value;
        }

        shape.CustomGeometry.Clear();
        if (_oldCustomGeometry is not null)
            shape.CustomGeometry.AddRange(CloneCustomGeometry(_oldCustomGeometry));
    }

    private static List<CustomGeometryPath> CloneCustomGeometry(IEnumerable<CustomGeometryPath> paths) =>
        paths.Select(path =>
        {
            var copy = new CustomGeometryPath
            {
                PathW = path.PathW,
                PathH = path.PathH,
                Fill = path.Fill,
                Stroke = path.Stroke,
            };
            copy.Segments.AddRange(path.Segments);
            return copy;
        }).ToList();
}

/// <summary>
/// Replaces one SmartArt graphic with ordinary slide shapes at the same z-order position.
/// This is the model-side operation behind PowerPoint's Convert to Shapes command.
/// </summary>
public sealed class ConvertSmartArtToShapesCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _smartArtId;
    private readonly SlideShape _original;
    private readonly List<SlideShape> _converted;
    private int _index = -1;

    public ConvertSmartArtToShapesCommand(
        int slideIndex,
        uint smartArtId,
        SlideShape original,
        IEnumerable<SlideShape> converted)
    {
        _slideIndex = slideIndex;
        _smartArtId = smartArtId;
        _original = SlideCloner.CloneShape(original);
        _converted = converted.Select(SlideCloner.CloneShape).ToList();
    }

    public string Label => "Convert SmartArt to Shapes";

    public bool HasEffect(Presentation presentation) =>
        ShapeHelper.Find(presentation, _slideIndex, _smartArtId) is { Kind: SlideShapeKind.SmartArt } &&
        _converted.Count > 0;

    public void Apply(Presentation presentation)
    {
        var shapes = ShapeHelper.Shapes(presentation, _slideIndex);
        if (shapes is null || _converted.Count == 0)
            return;

        _index = shapes.FindIndex(shape => shape.Id == _smartArtId);
        if (_index < 0)
            return;

        shapes.RemoveAt(_index);
        shapes.InsertRange(_index, _converted);
    }

    public void Revert(Presentation presentation)
    {
        var shapes = ShapeHelper.Shapes(presentation, _slideIndex);
        if (shapes is null || _index < 0)
            return;

        var firstConverted = _converted[0].Id;
        var currentIndex = shapes.FindIndex(shape => shape.Id == firstConverted);
        if (currentIndex < 0)
            return;

        var count = Math.Min(_converted.Count, shapes.Count - currentIndex);
        shapes.RemoveRange(currentIndex, count);
        shapes.Insert(Math.Clamp(currentIndex, 0, shapes.Count), _original);
    }
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
        if (!ChartHelper.IsObjectEditable(shapes[_capturedIndex])) return;
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
/// Also re-routes any connectors whose start/end is attached to the moved shape (Wave 23).
/// </summary>
public sealed class MoveShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly long _dx;
    private readonly long _dy;
    private bool _applied;

    // Captured reroute data: (connectorId, oldX, oldY, oldCx, oldCy, oldRoute, newX, newY, newCx, newCy)
    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

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
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        s.OffsetXEmu += _dx;
        s.OffsetYEmu += _dy;
        _applied = true;

        // Reroute attached connectors after the shape has moved.
        _rerouteCapture = ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu -= _dx;
        s.OffsetYEmu -= _dy;

        // Restore connector bounds captured during Apply.
        RevertReroute(p, _slideIndex, _rerouteCapture);
    }

    internal static List<(uint, long, long, long, long, List<(long X, long Y)>?, long, long, long, long)> ApplyReroute(
        Presentation p, int slideIndex, uint movedShapeId)
    {
        var captures = new List<(uint, long, long, long, long, List<(long X, long Y)>?, long, long, long, long)>();
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return captures;

        var slide = p.Slides[slideIndex];
        foreach (var cmd in ConnectorRouter.BuildRerouteCommands(p, slideIndex, movedShapeId))
        {
            // Find the connector and capture old bounds + old route before applying.
            var c = ShapeHelper.Find(p, slideIndex, cmd.ConnectorId);
            if (c is null) continue;
            long ox = c.OffsetXEmu, oy = c.OffsetYEmu, ocx = c.ExtentCxEmu, ocy = c.ExtentCyEmu;
            var oroute = c.ElbowRoute;
            cmd.Apply(p);
            captures.Add((cmd.ConnectorId, ox, oy, ocx, ocy, oroute, cmd.NewX, cmd.NewY, cmd.NewCx, cmd.NewCy));
        }
        return captures;
    }

    internal static void RevertReroute(
        Presentation p, int slideIndex,
        List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>? captures)
    {
        if (captures is null || slideIndex < 0 || slideIndex >= p.Slides.Count) return;
        var slide = p.Slides[slideIndex];
        foreach (var (id, ox, oy, ocx, ocy, oroute, _, _, _, _) in captures)
        {
            var c = ShapeHelper.Find(p, slideIndex, id);
            if (c is null) continue;
            c.OffsetXEmu  = ox;
            c.OffsetYEmu  = oy;
            c.ExtentCxEmu = ocx;
            c.ExtentCyEmu = ocy;
            c.ElbowRoute  = oroute;
        }
    }
}

/// <summary>
/// Sets the absolute position and size of a shape, capturing prior values for undo.
/// Also re-routes any connectors whose start/end is attached to the resized shape (Wave 23).
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
    private bool _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

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
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        _oldOffsetX = s.OffsetXEmu;
        _oldOffsetY = s.OffsetYEmu;
        _oldCx      = s.ExtentCxEmu;
        _oldCy      = s.ExtentCyEmu;
        s.OffsetXEmu  = _newOffsetX;
        s.OffsetYEmu  = _newOffsetY;
        s.ExtentCxEmu = _newCx;
        s.ExtentCyEmu = _newCy;
        _applied = true;

        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.OffsetXEmu  = _oldOffsetX;
        s.OffsetYEmu  = _oldOffsetY;
        s.ExtentCxEmu = _oldCx;
        s.ExtentCyEmu = _oldCy;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
    }
}

/// <summary>Sets the source crop rectangle on a picture without changing its frame geometry.</summary>
public sealed class SetPictureCropCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly double _left;
    private readonly double _top;
    private readonly double _right;
    private readonly double _bottom;
    private bool _captured;
    private bool _hadFormat;
    private double _oldLeft;
    private double _oldTop;
    private double _oldRight;
    private double _oldBottom;

    public SetPictureCropCommand(
        int slideIndex,
        uint shapeId,
        double left,
        double top,
        double right,
        double bottom)
    {
        Validate(left, top, right, bottom);
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _left = left;
        _top = top;
        _right = right;
        _bottom = bottom;
    }

    public string Label => "Crop Picture";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        var format = shape.PictureFormat;
        return (format?.CropLeft ?? 0) != _left ||
               (format?.CropTop ?? 0) != _top ||
               (format?.CropRight ?? 0) != _right ||
               (format?.CropBottom ?? 0) != _bottom;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return;

        if (!_captured)
        {
            _captured = true;
            _hadFormat = shape.PictureFormat is not null;
            _oldLeft = shape.PictureFormat?.CropLeft ?? 0;
            _oldTop = shape.PictureFormat?.CropTop ?? 0;
            _oldRight = shape.PictureFormat?.CropRight ?? 0;
            _oldBottom = shape.PictureFormat?.CropBottom ?? 0;
        }

        if (shape.PictureFormat is null)
        {
            if (_left == 0 && _top == 0 && _right == 0 && _bottom == 0)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.CropLeft = _left;
        shape.PictureFormat.CropTop = _top;
        shape.PictureFormat.CropRight = _right;
        shape.PictureFormat.CropBottom = _bottom;
        if (_left == 0 && _top == 0 && _right == 0 && _bottom == 0 &&
            !shape.PictureFormat.HasColorEffect)
        {
            shape.PictureFormat = null;
        }
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture || !_captured)
            return;

        if (shape.PictureFormat is null)
        {
            if (!_hadFormat && (_oldLeft != 0 || _oldTop != 0 || _oldRight != 0 || _oldBottom != 0))
                shape.PictureFormat = new PictureFormat();
            else
                return;
        }

        shape.PictureFormat.CropLeft = _oldLeft;
        shape.PictureFormat.CropTop = _oldTop;
        shape.PictureFormat.CropRight = _oldRight;
        shape.PictureFormat.CropBottom = _oldBottom;
        if (!_hadFormat && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    private static void Validate(double left, double top, double right, double bottom)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom) ||
            left < 0 || top < 0 || right < 0 || bottom < 0 ||
            left + right >= 1 || top + bottom >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Picture crop fractions must be non-negative and leave a visible source rectangle.");
        }
    }
}

/// <summary>Sets the authored color effects on a picture without changing its crop or frame.</summary>
public sealed class SetPictureColorEffectsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly PictureColorEffectValues _values;
    private bool _captured;
    private bool _hadFormat;
    private PictureColorEffectValues _oldValues;

    public SetPictureColorEffectsCommand(
        int slideIndex,
        uint shapeId,
        PictureColorEffectValues values)
    {
        Validate(values);
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Picture Color Effects";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        return ReadValues(shape.PictureFormat) != _values;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return;

        if (!_captured)
        {
            _captured = true;
            _hadFormat = shape.PictureFormat is not null;
            _oldValues = ReadValues(shape.PictureFormat);
        }

        if (shape.PictureFormat is null)
        {
            if (_values == PictureColorEffectValues.Reset)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.Grayscale = _values.Grayscale;
        shape.PictureFormat.BiLevelThreshold = _values.BiLevelThreshold;
        shape.PictureFormat.Brightness = _values.Brightness;
        shape.PictureFormat.Contrast = _values.Contrast;
        shape.PictureFormat.AlphaModPct = _values.AlphaModPct;

        if (!shape.PictureFormat.HasCrop && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.Kind != SlideShapeKind.Picture || !_captured)
            return;

        if (shape.PictureFormat is null)
        {
            if (!_hadFormat)
                return;
            shape.PictureFormat = new PictureFormat();
        }

        shape.PictureFormat.Grayscale = _oldValues.Grayscale;
        shape.PictureFormat.BiLevelThreshold = _oldValues.BiLevelThreshold;
        shape.PictureFormat.Brightness = _oldValues.Brightness;
        shape.PictureFormat.Contrast = _oldValues.Contrast;
        shape.PictureFormat.AlphaModPct = _oldValues.AlphaModPct;
        if (!shape.PictureFormat.HasCrop && !shape.PictureFormat.HasColorEffect)
            shape.PictureFormat = null;
    }

    private static PictureColorEffectValues ReadValues(PictureFormat? format) => format is null
        ? PictureColorEffectValues.Reset
        : new(
            format.Grayscale,
            format.BiLevelThreshold,
            format.Brightness,
            format.Contrast,
            format.AlphaModPct);

    private static void Validate(PictureColorEffectValues values)
    {
        if (values.BiLevelThreshold is { } threshold &&
            (double.IsNaN(threshold) || threshold < 0 || threshold > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Bi-level threshold must be between 0 and 1.");
        if (values.Brightness is { } brightness &&
            (double.IsNaN(brightness) || brightness < -1 || brightness > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Brightness must be between -1 and 1.");
        if (values.Contrast is { } contrast &&
            (double.IsNaN(contrast) || contrast < -1 || contrast > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Contrast must be between -1 and 1.");
        if (values.AlphaModPct is { } alpha &&
            (double.IsNaN(alpha) || alpha < 0 || alpha > 1))
            throw new ArgumentOutOfRangeException(nameof(values), "Alpha must be between 0 and 1.");
    }
}

/// <summary>
/// Sets one DrawingML preset-geometry adjustment on a shape.
/// A missing value removes the authored adjustment and restores the preset default.
/// </summary>
public sealed class SetShapeGeometryAdjustmentCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly string _name;
    private readonly double? _newValue;
    private bool _hadOldValue;
    private double _oldValue;

    public SetShapeGeometryAdjustmentCommand(int slideIndex, uint shapeId, string name, double? value)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("An adjustment name is required.", nameof(name))
            : name;
        _newValue = value;
    }

    public string Label => "Edit Shape Geometry";

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null)
            return;

        _hadOldValue = shape.PresetGeometryAdjustments.TryGetValue(_name, out _oldValue);
        if (_newValue is { } value)
            shape.PresetGeometryAdjustments[_name] = value;
        else
            shape.PresetGeometryAdjustments.Remove(_name);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null)
            return;

        if (_hadOldValue)
            shape.PresetGeometryAdjustments[_name] = _oldValue;
        else
            shape.PresetGeometryAdjustments.Remove(_name);
    }
}

/// <summary>Moves one vertex or curve control point in an imported custom geometry path.</summary>
public sealed class SetCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _newX;
    private readonly double _newY;
    private readonly CustomGeometryPointSlot _slot;
    private CustomSegment? _oldSegment;

    public SetCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double x,
        double y,
        CustomGeometryPointSlot slot = CustomGeometryPointSlot.Endpoint)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _newX = x;
        _newY = y;
        _slot = slot;
    }

    public string Label => _slot == CustomGeometryPointSlot.Endpoint ? "Edit Shape Vertex" : "Edit Curve Control Point";

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count)
            return;

        var segment = path.Segments[_segmentIndex];
        if (!CanMove(segment.Kind, _slot))
            return;

        _oldSegment = segment;
        path.Segments[_segmentIndex] = ApplyPoint(segment, _slot, _newX, _newY);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _oldSegment is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex >= 0 && _segmentIndex < path.Segments.Count)
            path.Segments[_segmentIndex] = _oldSegment;
    }

    private static bool CanMove(CustomSegmentKind kind, CustomGeometryPointSlot slot) =>
        ((kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo) && slot == CustomGeometryPointSlot.Endpoint) ||
        (kind == CustomSegmentKind.QuadBezTo && (slot is CustomGeometryPointSlot.Control1 or CustomGeometryPointSlot.Endpoint)) ||
        (kind == CustomSegmentKind.CubicBezTo && (slot is CustomGeometryPointSlot.Control1 or CustomGeometryPointSlot.Control2 or CustomGeometryPointSlot.Endpoint));

    private static CustomSegment ApplyPoint(CustomSegment segment, CustomGeometryPointSlot slot, double x, double y) =>
        slot switch
        {
            CustomGeometryPointSlot.Control1 => segment with { X = x, Y = y },
            CustomGeometryPointSlot.Control2 => segment with { X1 = x, Y1 = y },
            _ when segment.Kind is CustomSegmentKind.QuadBezTo => segment with { X1 = x, Y1 = y },
            _ => segment with { X = x, Y = y },
        };
}

/// <summary>Sets one authored ArcTo angle or radius in a custom geometry path.</summary>
public sealed class SetCustomGeometryArcPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _newValue;
    private readonly CustomGeometryArcPointSlot _slot;
    private double _oldValue;

    public SetCustomGeometryArcPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double value,
        CustomGeometryArcPointSlot slot)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _newValue = value;
        _slot = slot;
    }

    public string Label => _slot switch
    {
        CustomGeometryArcPointSlot.RadiusX or CustomGeometryArcPointSlot.RadiusY => "Edit Arc Radius",
        _ => "Edit Arc Angle",
    };

    public bool HasEffect(Presentation presentation)
    {
        var segment = FindSegment(presentation);
        return segment is { Kind: CustomSegmentKind.ArcTo };
    }

    public void Apply(Presentation presentation)
    {
        var segment = FindSegment(presentation);
        if (segment is not { Kind: CustomSegmentKind.ArcTo })
            return;

        _oldValue = ReadValue(segment);
        ReplaceSegment(presentation, WriteValue(segment, _newValue));
    }

    public void Revert(Presentation presentation)
    {
        if (FindSegment(presentation) is { Kind: CustomSegmentKind.ArcTo })
            ReplaceSegment(presentation, WriteValue(FindSegment(presentation)!, _oldValue));
    }

    private CustomSegment? FindSegment(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return null;

        var path = shape.CustomGeometry[_pathIndex];
        return _segmentIndex >= 0 && _segmentIndex < path.Segments.Count
            ? path.Segments[_segmentIndex]
            : null;
    }

    private void ReplaceSegment(Presentation presentation, CustomSegment replacement)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex >= 0 && _segmentIndex < path.Segments.Count)
            path.Segments[_segmentIndex] = replacement;
    }

    private double ReadValue(CustomSegment segment) => _slot switch
    {
        CustomGeometryArcPointSlot.StartAngle => segment.StAng,
        CustomGeometryArcPointSlot.EndAngle => segment.StAng + segment.SwAng,
        CustomGeometryArcPointSlot.RadiusX => segment.WR,
        CustomGeometryArcPointSlot.RadiusY => segment.HR,
        _ => 0,
    };

    private CustomSegment WriteValue(CustomSegment segment, double value) => _slot switch
    {
        CustomGeometryArcPointSlot.StartAngle => segment with { StAng = value },
        CustomGeometryArcPointSlot.EndAngle => segment with { SwAng = value - segment.StAng },
        CustomGeometryArcPointSlot.RadiusX => segment with { WR = Math.Max(1, value) },
        CustomGeometryArcPointSlot.RadiusY => segment with { HR = Math.Max(1, value) },
        _ => segment,
    };
}

/// <summary>Inserts a straight custom-geometry vertex after a selected endpoint.</summary>
public sealed class InsertCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private readonly double _x;
    private readonly double _y;
    private int _insertedSegmentIndex = -1;

    public InsertCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double x,
        double y)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
        _x = x;
        _y = y;
    }

    public string Label => "Add Shape Point";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape is not null &&
            _pathIndex >= 0 && _pathIndex < shape.CustomGeometry.Count &&
            _segmentIndex >= 0 && _segmentIndex < shape.CustomGeometry[_pathIndex].Segments.Count &&
            shape.CustomGeometry[_pathIndex].Segments[_segmentIndex].Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo;
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count ||
            path.Segments[_segmentIndex].Kind is not (CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo))
            return;

        _insertedSegmentIndex = _segmentIndex + 1;
        path.Segments.Insert(_insertedSegmentIndex, new CustomSegment(
            CustomSegmentKind.LineTo, X: _x, Y: _y));
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        if (_insertedSegmentIndex >= 0 && _insertedSegmentIndex < path.Segments.Count &&
            path.Segments[_insertedSegmentIndex].Kind == CustomSegmentKind.LineTo)
            path.Segments.RemoveAt(_insertedSegmentIndex);
    }
}

/// <summary>Deletes a selected straight custom-geometry vertex while preserving path structure.</summary>
public sealed class DeleteCustomGeometryPointCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _pathIndex;
    private readonly int _segmentIndex;
    private CustomSegment? _removedSegment;

    public DeleteCustomGeometryPointCommand(
        int slideIndex,
        uint shapeId,
        int pathIndex,
        int segmentIndex)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _pathIndex = pathIndex;
        _segmentIndex = segmentIndex;
    }

    public string Label => "Delete Shape Point";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape is null || _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return false;

        var path = shape.CustomGeometry[_pathIndex];
        if (_segmentIndex < 0 || _segmentIndex >= path.Segments.Count ||
            path.Segments[_segmentIndex].Kind != CustomSegmentKind.LineTo)
            return false;

        return path.Segments.Count(segment =>
            segment.Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo) > 2;
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (!HasEffect(p) || shape is null)
            return;

        var path = shape.CustomGeometry[_pathIndex];
        _removedSegment = path.Segments[_segmentIndex];
        path.Segments.RemoveAt(_segmentIndex);
    }

    public void Revert(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || _removedSegment is null ||
            _pathIndex < 0 || _pathIndex >= shape.CustomGeometry.Count)
            return;

        shape.CustomGeometry[_pathIndex].Segments.Insert(_segmentIndex, _removedSegment);
    }
}

/// <summary>
/// Sets the rotation of a shape; captures old rotation for undo.
/// Also re-routes any connectors whose start/end is attached to the rotated shape (Wave 23).
/// </summary>
public sealed class RotateShapeCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly double _newRotationDeg;
    private double          _oldRotationDeg;
    private bool             _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

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
        if (s is null || !ChartHelper.IsObjectEditable(s)) return;
        _oldRotationDeg = s.RotationDeg;
        s.RotationDeg   = _newRotationDeg;
        _applied = true;

        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var s = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (s is null) return;
        s.RotationDeg = _oldRotationDeg;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
    }
}

/// <summary>
/// Toggles a shape's horizontal or vertical mirror state and re-routes attached connectors.
/// The flip flags are serialized shape semantics; this command supplies the missing authoring path.
/// </summary>
public sealed class FlipShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly bool _horizontal;
    private bool _oldFlip;
    private bool _applied;

    private List<(uint id, long ox, long oy, long ocx, long ocy, List<(long X, long Y)>? oroute, long nx, long ny, long ncx, long ncy)>?
        _rerouteCapture;

    public FlipShapeCommand(int slideIndex, uint shapeId, bool horizontal)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _horizontal = horizontal;
    }

    public string Label => _horizontal ? "Flip Horizontal" : "Flip Vertical";

    public bool HasEffect(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        return shape is not null && ChartHelper.IsObjectEditable(shape);
    }

    public void Apply(Presentation p)
    {
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null || !ChartHelper.IsObjectEditable(shape)) return;

        _oldFlip = _horizontal ? shape.FlipH : shape.FlipV;
        if (_horizontal)
            shape.FlipH = !_oldFlip;
        else
            shape.FlipV = !_oldFlip;

        _applied = true;
        _rerouteCapture = MoveShapeCommand.ApplyReroute(p, _slideIndex, _shapeId);
    }

    public void Revert(Presentation p)
    {
        if (!_applied) return;
        var shape = ShapeHelper.Find(p, _slideIndex, _shapeId);
        if (shape is null) return;

        if (_horizontal)
            shape.FlipH = _oldFlip;
        else
            shape.FlipV = _oldFlip;

        MoveShapeCommand.RevertReroute(p, _slideIndex, _rerouteCapture);
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
    private int           _oldZIndex = -1;

    public ReorderShapeCommand(int slideIndex, uint shapeId, int newZIndex)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newZIndex  = newZIndex;
    }

    public string Label => "Reorder Shape";

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper.FindContainingList(p, _slideIndex, _shapeId);
        if (shapes is null) return;
        _oldZIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_oldZIndex < 0) return;
        MoveInList(shapes, _oldZIndex, _newZIndex);
    }

    public void Revert(Presentation p)
    {
        if (_oldZIndex < 0) return;
        var shapes = ShapeHelper.FindContainingList(p, _slideIndex, _shapeId);
        if (shapes is null) return;
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
/// Changes the DrawingML text-frame autofit mode of one shape while preserving the authored
/// distinction between no autofit, shrink text on overflow, and grow shape to fit text.
/// </summary>
public sealed class SetShapeTextAutoFitCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextAutoFitKind _newKind;
    private TextAutoFitKind _oldKind;

    public SetShapeTextAutoFitCommand(int slideIndex, uint shapeId, TextAutoFitKind newKind)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newKind = newKind;
    }

    public string Label => "Set Text Autofit";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.AutoFitKind != _newKind;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldKind = body.AutoFitKind;
        body.AutoFitKind = _newKind;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.AutoFitKind = _oldKind;
    }
}

/// <summary>Changes the DrawingML text-frame text direction of one shape.</summary>
public sealed class SetShapeTextVerticalTypeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextVerticalType _newType;
    private TextVerticalType _oldType;

    public SetShapeTextVerticalTypeCommand(int slideIndex, uint shapeId, TextVerticalType newType)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newType = newType;
    }

    public string Label => "Set Text Direction";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.VerticalType != _newType;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldType = body.VerticalType;
        body.VerticalType = _newType;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.VerticalType = _oldType;
    }
}

/// <summary>Changes the DrawingML text-frame column count of one shape.</summary>
public sealed class SetShapeTextColumnCountCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _newCount;
    private int _oldCount;

    public SetShapeTextColumnCountCommand(int slideIndex, uint shapeId, int newCount)
    {
        if (newCount < 1)
            throw new ArgumentOutOfRangeException(nameof(newCount), "Text column count must be positive.");

        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newCount = newCount;
    }

    public string Label => "Set Text Columns";

    public bool HasEffect(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        return shape?.TextBody is { } body && body.ColumnCount != _newCount;
    }

    public void Apply(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        _oldCount = body.ColumnCount;
        body.ColumnCount = _newCount;
    }

    public void Revert(Presentation presentation)
    {
        var shape = ShapeHelper.Find(presentation, _slideIndex, _shapeId);
        if (shape?.TextBody is not { } body)
            return;

        body.ColumnCount = _oldCount;
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
/// <remarks>
/// RR1 fix: Apply snapshots the run's prior (Bold, BoldSet) pair so that Revert can restore
/// the exact prior state — including BoldSet=false (inherited) — rather than blindly
/// re-toggling which would bake the run to explicit non-bold after undo.
/// </remarks>
public sealed class ToggleRunBoldCommand : RunFormatCommandBase
{
    private bool _priorBold;
    private bool _priorBoldSet;

    public ToggleRunBoldCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Bold";

    protected override void ApplyToRun(Run r)
    {
        // Snapshot the prior (Bold, BoldSet) pair before mutating — may be inherited (BoldSet=false).
        _priorBold    = r.Bold;
        _priorBoldSet = r.BoldSet;
        // Forward toggle: invert run.Bold and mark as explicit so the choice round-trips.
        r.Bold    = !r.Bold;
        r.BoldSet = true;
    }

    protected override void RevertFromRun(Run r)
    {
        // Restore the exact prior (Bold, BoldSet) pair — including inherited (BoldSet=false).
        r.Bold    = _priorBold;
        r.BoldSet = _priorBoldSet;
    }
}

/// <summary>Toggles italic on a single run.</summary>
/// <remarks>
/// RR1 fix: mirrors the same prior-(Italic,ItalicSet) snapshot+restore pattern as
/// <see cref="ToggleRunBoldCommand"/>.
/// </remarks>
public sealed class ToggleRunItalicCommand : RunFormatCommandBase
{
    private bool _priorItalic;
    private bool _priorItalicSet;

    public ToggleRunItalicCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Italic";

    protected override void ApplyToRun(Run r)
    {
        _priorItalic    = r.Italic;
        _priorItalicSet = r.ItalicSet;
        r.Italic    = !r.Italic;
        r.ItalicSet = true;
    }

    protected override void RevertFromRun(Run r)
    {
        r.Italic    = _priorItalic;
        r.ItalicSet = _priorItalicSet;
    }
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

/// <summary>Toggles a run's superscript baseline offset.</summary>
public sealed class ToggleRunSuperscriptCommand : RunFormatCommandBase
{
    private int? _priorBaseline;

    public ToggleRunSuperscriptCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Superscript";

    protected override void ApplyToRun(Run r)
    {
        _priorBaseline = r.BaselineOffset;
        r.BaselineOffset = r.BaselineOffset > 0 ? null : 10000;
    }

    protected override void RevertFromRun(Run r) => r.BaselineOffset = _priorBaseline;
}

/// <summary>Toggles a run's subscript baseline offset.</summary>
public sealed class ToggleRunSubscriptCommand : RunFormatCommandBase
{
    private int? _priorBaseline;

    public ToggleRunSubscriptCommand(int slideIndex, uint shapeId, int paragraphIndex, int runIndex)
        : base(slideIndex, shapeId, paragraphIndex, runIndex) { }

    public override string Label => "Subscript";

    protected override void ApplyToRun(Run r)
    {
        _priorBaseline = r.BaselineOffset;
        r.BaselineOffset = r.BaselineOffset < 0 ? null : -10000;
    }

    protected override void RevertFromRun(Run r) => r.BaselineOffset = _priorBaseline;
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

// ════════════════════════════════════════════════════════════════════════════════
// TRANSITION + ANIMATION COMMANDS
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sets or clears the slide transition for the slide at <paramref name="slideIndex"/>.
/// Captures the old transition for undo.
/// </summary>
public sealed class SetSlideTransitionCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly SlideTransition? _newTransition;
    private SlideTransition?          _oldTransition;

    public SetSlideTransitionCommand(int slideIndex, SlideTransition? transition)
    {
        _slideIndex    = slideIndex;
        _newTransition = transition;
    }

    public string Label => "Set Transition";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var slide         = p.Slides[_slideIndex];
        _oldTransition    = slide.Transition;
        slide.Transition  = _newTransition;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Transition = _oldTransition;
    }
}

/// <summary>
/// Replaces the raw PowerPoint paragraph-build list on a slide. The build list is
/// intentionally kept as source XML because PowerPoint stores timing metadata that
/// is broader than the current shared model. The command makes authoring changes
/// undoable without discarding unrelated timing entries.
/// </summary>
public sealed class SetSlideAnimationBuildListCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly string? _newBuildListXml;
    private string? _oldBuildListXml;

    public SetSlideAnimationBuildListCommand(int slideIndex, string? buildListXml)
    {
        _slideIndex = slideIndex;
        _newBuildListXml = buildListXml;
    }

    public string Label => "Set Text Build";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        var slide = p.Slides[_slideIndex];
        _oldBuildListXml = slide.AnimationBuildListXml;
        slide.AnimationBuildListXml = _newBuildListXml;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        p.Slides[_slideIndex].AnimationBuildListXml = _oldBuildListXml;
    }
}

/// <summary>
/// Appends a <see cref="ShapeAnimation"/> to the animation list of the slide at
/// <paramref name="slideIndex"/>.
/// </summary>
public sealed class AddShapeAnimationCommand : IPresentationCommand
{
    private readonly int            _slideIndex;
    private readonly ShapeAnimation _animation;

    public AddShapeAnimationCommand(int slideIndex, ShapeAnimation animation)
    {
        _slideIndex = slideIndex;
        _animation  = animation;
    }

    public string Label => "Add Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Animations.Add(_animation);
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Animations.Remove(_animation);
    }
}

/// <summary>
/// Removes the animation at <paramref name="animationIndex"/> from the slide at <paramref name="slideIndex"/>.
/// Captures the entry and its index for undo.
/// </summary>
public sealed class RemoveShapeAnimationCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly int _animationIndex;
    private ShapeAnimation? _captured;

    public RemoveShapeAnimationCommand(int slideIndex, int animationIndex)
    {
        _slideIndex     = slideIndex;
        _animationIndex = animationIndex;
    }

    public string Label => "Remove Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        _captured = anims[_animationIndex];
        anims.RemoveAt(_animationIndex);
    }

    public void Revert(Presentation p)
    {
        if (_captured is null) return;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        var idx = Math.Clamp(_animationIndex, 0, anims.Count);
        anims.Insert(idx, _captured);
    }
}

/// <summary>
/// Reorders the animation at <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
/// </summary>
public sealed class ReorderShapeAnimationCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly int _from;
    private readonly int _to;

    public ReorderShapeAnimationCommand(int slideIndex, int fromIndex, int toIndex)
    {
        _slideIndex = slideIndex;
        _from       = fromIndex;
        _to         = toIndex;
    }

    public string Label => "Reorder Animation";

    public void Apply(Presentation p)  => MoveInList(p, _from, _to);
    public void Revert(Presentation p) => MoveInList(p, _to, _from);

    private void MoveInList(Presentation p, int from, int to)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (from == to || from < 0 || from >= anims.Count) return;
        var item = anims[from];
        anims.RemoveAt(from);
        var dest = Math.Clamp(to, 0, anims.Count);
        anims.Insert(dest, item);
    }
}

/// <summary>
/// Replaces the animation entry at <paramref name="animationIndex"/> with a new <see cref="ShapeAnimation"/>.
/// Captures old entry for undo.
/// </summary>
public sealed class SetShapeAnimationCommand : IPresentationCommand
{
    private readonly int            _slideIndex;
    private readonly int            _animationIndex;
    private readonly ShapeAnimation _newAnimation;
    private ShapeAnimation?         _oldAnimation;

    public SetShapeAnimationCommand(int slideIndex, int animationIndex, ShapeAnimation newAnimation)
    {
        _slideIndex      = slideIndex;
        _animationIndex  = animationIndex;
        _newAnimation    = newAnimation;
    }

    public string Label => "Edit Animation";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        _oldAnimation         = anims[_animationIndex];
        anims[_animationIndex] = _newAnimation;
    }

    public void Revert(Presentation p)
    {
        if (_oldAnimation is null) return;
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var anims = p.Slides[_slideIndex].Animations;
        if (_animationIndex < 0 || _animationIndex >= anims.Count) return;
        anims[_animationIndex] = _oldAnimation;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// NOTES COMMAND
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the speaker-notes <see cref="TextBody"/> on the slide at <paramref name="slideIndex"/>.
/// Captures the previous value for undo. Pass null to clear notes.
/// </summary>
public sealed class SetSlideNotesCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly TextBody? _newNotes;
    private TextBody?          _oldNotes;

    public SetSlideNotesCommand(int slideIndex, TextBody? newNotes)
    {
        _slideIndex = slideIndex;
        _newNotes   = newNotes;
    }

    public string Label => "Set Notes";

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        var slide  = p.Slides[_slideIndex];
        _oldNotes  = slide.Notes;
        slide.Notes = _newNotes;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return;
        p.Slides[_slideIndex].Notes = _oldNotes;
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
