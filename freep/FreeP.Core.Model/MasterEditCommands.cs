namespace FreeP.Core.Model;

/// <summary>Identifies the editable root currently shown in Slide Master view.</summary>
public readonly record struct MasterEditTarget(MasterEditTargetKind Kind, string Id)
{
    public static MasterEditTarget Master(string masterId) => new(MasterEditTargetKind.Master, masterId);
    public static MasterEditTarget Layout(string layoutId) => new(MasterEditTargetKind.Layout, layoutId);
}

/// <summary>Whether a master-edit target is a slide master or one of its layouts.</summary>
public enum MasterEditTargetKind
{
    Master,
    Layout,
}

/// <summary>
/// Shared target resolution and shape-tree helpers for commands authored in Slide Master view.
/// These deliberately address <see cref="SlideMaster.Placeholders"/> and
/// <see cref="SlideLayout.Placeholders"/> directly: master editing is not a disguised slide edit.
/// </summary>
public static class MasterEditTargetResolver
{
    public static List<SlideShape>? GetShapes(Presentation presentation, MasterEditTarget target) => target.Kind switch
    {
        MasterEditTargetKind.Master => presentation.Masters.Find(master => master.Id == target.Id)?.Placeholders,
        MasterEditTargetKind.Layout => presentation.Layouts.Find(layout => layout.Id == target.Id)?.Placeholders,
        _ => null,
    };

    public static SlideMaster? GetMaster(Presentation presentation, MasterEditTarget target) => target.Kind switch
    {
        MasterEditTargetKind.Master => presentation.Masters.Find(master => master.Id == target.Id),
        MasterEditTargetKind.Layout => presentation.Masters.Find(master =>
            master.Id == presentation.Layouts.Find(layout => layout.Id == target.Id)?.MasterId),
        _ => null,
    };

    public static List<SlideShape>? FindContainingList(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shapes as List<SlideShape>;

            if (shape.Children.Count > 0 && FindContainingList(shape.Children, shapeId) is { } nested)
                return nested;
        }

        return null;
    }
}

/// <summary>Translates a master or layout shape, including group descendants.</summary>
public sealed class MoveMasterShapeCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly uint _shapeId;
    private readonly long _dx;
    private readonly long _dy;
    private bool _applied;

    public MoveMasterShapeCommand(MasterEditTarget target, uint shapeId, long dxEmu, long dyEmu)
    {
        _target = target;
        _shapeId = shapeId;
        _dx = dxEmu;
        _dy = dyEmu;
    }

    public string Label => "Move Master Shape";

    public bool HasEffect(Presentation presentation) =>
        (_dx != 0 || _dy != 0) && Find(presentation) is not null;

    public void Apply(Presentation presentation)
    {
        if (Find(presentation) is not { } shape)
            return;
        SlideShapeTraversal.TranslateWithDescendants(shape, _dx, _dy);
        _applied = true;
    }

    public void Revert(Presentation presentation)
    {
        if (_applied && Find(presentation) is { } shape)
            SlideShapeTraversal.TranslateWithDescendants(shape, -_dx, -_dy);
    }

    private SlideShape? Find(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes
            ? SlideShapeTraversal.FindById(shapes, _shapeId)
            : null;
}

/// <summary>Sets absolute master/layout shape bounds and restores group descendants on undo.</summary>
public sealed class ResizeMasterShapeCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly uint _shapeId;
    private readonly long _x;
    private readonly long _y;
    private readonly long _cx;
    private readonly long _cy;
    private (long X, long Y, long Cx, long Cy) _before;
    private List<(SlideShape Shape, long X, long Y, long Cx, long Cy)>? _descendants;
    private bool _applied;

    public ResizeMasterShapeCommand(MasterEditTarget target, uint shapeId, long x, long y, long cx, long cy)
    {
        _target = target;
        _shapeId = shapeId;
        _x = x;
        _y = y;
        _cx = cx;
        _cy = cy;
    }

    public string Label => "Resize Master Shape";

    public bool HasEffect(Presentation presentation) => Find(presentation) is { } shape &&
        (shape.OffsetXEmu != _x || shape.OffsetYEmu != _y || shape.ExtentCxEmu != _cx || shape.ExtentCyEmu != _cy);

    public void Apply(Presentation presentation)
    {
        if (Find(presentation) is not { } shape)
            return;

        _before = (shape.OffsetXEmu, shape.OffsetYEmu, shape.ExtentCxEmu, shape.ExtentCyEmu);
        var descendants = SlideShapeTraversal.EnumerateDescendants(shape).ToList();
        _descendants = descendants.Select(child => (child, child.OffsetXEmu, child.OffsetYEmu, child.ExtentCxEmu, child.ExtentCyEmu)).ToList();
        var scaleX = _before.Cx == 0 ? 1d : (double)_cx / _before.Cx;
        var scaleY = _before.Cy == 0 ? 1d : (double)_cy / _before.Cy;
        foreach (var (child, oldX, oldY, oldCx, oldCy) in _descendants)
        {
            child.OffsetXEmu = _x + (long)Math.Round((oldX - _before.X) * scaleX);
            child.OffsetYEmu = _y + (long)Math.Round((oldY - _before.Y) * scaleY);
            child.ExtentCxEmu = (long)Math.Round(oldCx * scaleX);
            child.ExtentCyEmu = (long)Math.Round(oldCy * scaleY);
        }

        shape.OffsetXEmu = _x;
        shape.OffsetYEmu = _y;
        shape.ExtentCxEmu = _cx;
        shape.ExtentCyEmu = _cy;
        _applied = true;
    }

    public void Revert(Presentation presentation)
    {
        if (!_applied || Find(presentation) is not { } shape)
            return;
        shape.OffsetXEmu = _before.X;
        shape.OffsetYEmu = _before.Y;
        shape.ExtentCxEmu = _before.Cx;
        shape.ExtentCyEmu = _before.Cy;
        if (_descendants is not null)
        {
            foreach (var (child, x, y, cx, cy) in _descendants)
            {
                child.OffsetXEmu = x;
                child.OffsetYEmu = y;
                child.ExtentCxEmu = cx;
                child.ExtentCyEmu = cy;
            }
        }
    }

    private SlideShape? Find(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes
            ? SlideShapeTraversal.FindById(shapes, _shapeId)
            : null;
}

/// <summary>Sets a master/layout shape rotation, retaining its former value for undo.</summary>
public sealed class RotateMasterShapeCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly uint _shapeId;
    private readonly double _after;
    private double _before;
    private bool _applied;

    public RotateMasterShapeCommand(MasterEditTarget target, uint shapeId, double rotationDeg)
    {
        _target = target;
        _shapeId = shapeId;
        _after = Normalize(rotationDeg);
    }

    public string Label => "Rotate Master Shape";

    public bool HasEffect(Presentation presentation) => Find(presentation) is { } shape &&
        Math.Abs(shape.RotationDeg - _after) > 0.0001;

    public void Apply(Presentation presentation)
    {
        if (Find(presentation) is not { } shape)
            return;
        _before = shape.RotationDeg;
        shape.RotationDeg = _after;
        _applied = true;
    }

    public void Revert(Presentation presentation)
    {
        if (_applied && Find(presentation) is { } shape)
            shape.RotationDeg = _before;
    }

    private SlideShape? Find(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes
            ? SlideShapeTraversal.FindById(shapes, _shapeId)
            : null;

    private static double Normalize(double rotationDeg)
    {
        if (!double.IsFinite(rotationDeg))
            return 0;
        var normalized = rotationDeg % 360d;
        return normalized < 0 ? normalized + 360d : normalized;
    }
}

/// <summary>Inserts a deep-cloned shape into a master/layout target.</summary>
public sealed class AddMasterShapeCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly SlideShape _source;
    private SlideShape? _inserted;

    public AddMasterShapeCommand(MasterEditTarget target, SlideShape shape)
    {
        _target = target;
        _source = SlideCloner.CloneShape(shape);
    }

    public string Label => "Add Master Shape";
    public int EstimatedBytes => PresentationCommandSizeEstimator.EstimateBytes(_source);

    public bool HasEffect(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes &&
        SlideShapeTraversal.FindById(shapes, _source.Id) is null;

    public void Apply(Presentation presentation)
    {
        if (MasterEditTargetResolver.GetShapes(presentation, _target) is not { } shapes ||
            SlideShapeTraversal.FindById(shapes, _source.Id) is not null)
            return;
        _inserted = SlideCloner.CloneShape(_source);
        shapes.Add(_inserted);
    }

    public void Revert(Presentation presentation)
    {
        if (MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes &&
            MasterEditTargetResolver.FindContainingList(shapes, _source.Id) is { } containing)
        {
            containing.RemoveAll(shape => shape.Id == _source.Id);
        }
    }
}

/// <summary>Removes one master/layout shape while retaining a deep snapshot for undo.</summary>
public sealed class DeleteMasterShapeCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly uint _shapeId;
    private SlideShape? _captured;
    private List<SlideShape>? _container;
    private int _index = -1;

    public DeleteMasterShapeCommand(MasterEditTarget target, uint shapeId)
    {
        _target = target;
        _shapeId = shapeId;
    }

    public string Label => "Delete Master Shape";
    public int EstimatedBytes => PresentationCommandSizeEstimator.EstimateBytes(_captured);

    public bool HasEffect(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes &&
        SlideShapeTraversal.FindById(shapes, _shapeId) is not null;

    public void Apply(Presentation presentation)
    {
        if (MasterEditTargetResolver.GetShapes(presentation, _target) is not { } shapes ||
            MasterEditTargetResolver.FindContainingList(shapes, _shapeId) is not { } containing)
            return;
        _index = containing.FindIndex(shape => shape.Id == _shapeId);
        if (_index < 0)
            return;
        _captured = SlideCloner.CloneShape(containing[_index]);
        _container = containing;
        containing.RemoveAt(_index);
    }

    public void Revert(Presentation presentation)
    {
        if (_captured is null || _index < 0 || _container is null)
            return;
        var insertAt = Math.Min(_index, _container.Count);
        _container.Insert(insertAt, SlideCloner.CloneShape(_captured));
    }
}

/// <summary>Sets a master/layout shape fill, retaining its prior value for undo.</summary>
public sealed class SetMasterShapeFillCommand : IPresentationCommand
{
    private readonly MasterEditTarget _target;
    private readonly uint _shapeId;
    private readonly ShapeFill? _after;
    private ShapeFill? _before;

    public SetMasterShapeFillCommand(MasterEditTarget target, uint shapeId, ShapeFill? fill)
    {
        _target = target;
        _shapeId = shapeId;
        _after = fill;
    }

    public string Label => "Set Master Fill";
    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine([
        PresentationCommandSizeEstimator.EstimateBytes(_before),
        PresentationCommandSizeEstimator.EstimateBytes(_after)]);

    public bool HasEffect(Presentation presentation) => Find(presentation) is { } shape && !Equals(shape.Fill, _after);

    public void Apply(Presentation presentation)
    {
        if (Find(presentation) is not { } shape)
            return;
        _before = shape.Fill;
        shape.Fill = _after;
    }

    public void Revert(Presentation presentation)
    {
        if (Find(presentation) is { } shape)
            shape.Fill = _before;
    }

    private SlideShape? Find(Presentation presentation) =>
        MasterEditTargetResolver.GetShapes(presentation, _target) is { } shapes
            ? SlideShapeTraversal.FindById(shapes, _shapeId)
            : null;
}
