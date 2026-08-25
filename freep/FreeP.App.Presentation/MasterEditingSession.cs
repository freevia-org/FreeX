using Free.Shared.Drawing;
using Free.Shared.Commands;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free editing session for PowerPoint Slide Master view. It intentionally owns a
/// master/layout target rather than making <see cref="EditingSession"/> pretend that master
/// placeholders are slide shapes. Both native hosts share this session and its undo bus.
/// </summary>
public sealed class MasterEditingSession
{
    private readonly List<uint> _selectedShapeIds = new();
    private uint? _shapeIdWatermark;
    private MasterEditTarget? _target;

    public MasterEditingSession(Presentation presentation, PresentationCommandBus bus)
    {
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        var targets = BuildTargets();
        _target = targets.Count > 0 ? targets[0] : null;
    }

    public Presentation Presentation { get; }
    public PresentationCommandBus Bus { get; }
    public MasterEditTarget? Target => _target;
    public IReadOnlyList<uint> SelectedShapeIds => _selectedShapeIds;
    public IReadOnlyList<MasterEditTarget> Targets => BuildTargets();
    public SlideMaster? CurrentMaster => _target is { } target ? MasterEditTargetResolver.GetMaster(Presentation, target) : null;
    public SlideLayout? CurrentLayout => _target is { Kind: MasterEditTargetKind.Layout, Id: var id }
        ? Presentation.Layouts.Find(layout => layout.Id == id)
        : null;
    public IReadOnlyList<SlideShape> CurrentShapes => _target is { } target
        ? MasterEditTargetResolver.GetShapes(Presentation, target) ?? []
        : [];

    public event EventHandler? TargetChanged;
    public event EventHandler? SelectionChanged;
    public event Action? Changed
    {
        add => Bus.Changed += value;
        remove => Bus.Changed -= value;
    }

    public bool CanUndo => Bus.CanUndo;
    public bool CanRedo => Bus.CanRedo;

    public bool SelectTarget(MasterEditTarget target)
    {
        if (MasterEditTargetResolver.GetShapes(Presentation, target) is null)
            return false;
        if (_target == target)
            return true;
        _target = target;
        _selectedShapeIds.Clear();
        TargetChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Select(uint shapeId, bool addToSelection = false)
    {
        if (_target is null || SlideShapeTraversal.FindById(CurrentShapes, shapeId) is null)
            return;
        if (!addToSelection)
            _selectedShapeIds.Clear();
        if (!_selectedShapeIds.Contains(shapeId))
            _selectedShapeIds.Add(shapeId);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selectedShapeIds.Count == 0)
            return;
        _selectedShapeIds.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Move(uint shapeId, long dxEmu, long dyEmu) => Execute(new MoveMasterShapeCommand(RequireTarget(), shapeId, dxEmu, dyEmu));

    public void MoveSelected(long dxEmu, long dyEmu)
    {
        var target = RequireTarget();
        ExecuteBatch("Move Master Shapes", _selectedShapeIds.Select(id =>
            (IPresentationCommand)new MoveMasterShapeCommand(target, id, dxEmu, dyEmu)));
    }

    public void Resize(uint shapeId, long x, long y, long cx, long cy) =>
        Execute(new ResizeMasterShapeCommand(RequireTarget(), shapeId, x, y, Math.Max(0, cx), Math.Max(0, cy)));

    public bool ApplySelectedTransforms(IEnumerable<CanvasShapeTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        var target = RequireTarget();
        var selected = _selectedShapeIds.ToHashSet();
        var commands = new List<IPresentationCommand>();
        foreach (var transform in transforms)
        {
            if (!selected.Contains(transform.ShapeId) ||
                SlideShapeTraversal.FindById(CurrentShapes, transform.ShapeId) is not { } shape)
                continue;
            if (shape.OffsetXEmu != transform.XEmu || shape.OffsetYEmu != transform.YEmu ||
                shape.ExtentCxEmu != transform.CxEmu || shape.ExtentCyEmu != transform.CyEmu)
            {
                commands.Add(new ResizeMasterShapeCommand(target, transform.ShapeId,
                    transform.XEmu, transform.YEmu, Math.Max(0, transform.CxEmu), Math.Max(0, transform.CyEmu)));
            }
            if (Math.Abs(shape.RotationDeg - transform.RotationDeg) > 0.0001)
                commands.Add(new RotateMasterShapeCommand(target, transform.ShapeId, transform.RotationDeg));
        }
        return ExecuteBatch("Transform Master Shapes", commands);
    }

    public void Rotate(uint shapeId, double rotationDeg) => Execute(new RotateMasterShapeCommand(RequireTarget(), shapeId, rotationDeg));

    public void Delete(uint shapeId)
    {
        Execute(new DeleteMasterShapeCommand(RequireTarget(), shapeId));
        PruneSelection();
    }

    public void DeleteSelected()
    {
        var target = RequireTarget();
        var selected = _selectedShapeIds.ToArray();
        ClearSelection();
        ExecuteBatch("Delete Master Shapes", selected.Select(id =>
            (IPresentationCommand)new DeleteMasterShapeCommand(target, id)));
    }

    public void SetFill(uint shapeId, ShapeFill? fill) => Execute(new SetMasterShapeFillCommand(RequireTarget(), shapeId, fill));

    public void AddShape(SlideShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        shape.Id = AllocateShapeId();
        Execute(new AddMasterShapeCommand(RequireTarget(), shape));
    }

    /// <summary>Adds a text placeholder at the center of the master/layout editing surface.</summary>
    public SlideShape AddTextPlaceholder(PlaceholderType type = PlaceholderType.Body)
    {
        var shape = new SlideShape
        {
            Name = $"{type} Placeholder",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = Presentation.SlideSizeCxEmu / 4,
            OffsetYEmu = Presentation.SlideSizeCyEmu / 3,
            ExtentCxEmu = Presentation.SlideSizeCxEmu / 2,
            ExtentCyEmu = Presentation.SlideSizeCyEmu / 5,
            Fill = ShapeFill.None.Instance,
            Placeholder = new Placeholder { Type = type },
            TextBody = new TextBody { Wrap = true },
        };
        shape.TextBody.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = string.Empty } } });
        AddShape(shape);
        return shape;
    }

    public void Undo()
    {
        Bus.Undo();
        PruneSelection();
    }

    public void Redo()
    {
        Bus.Redo();
        PruneSelection();
    }

    private void Execute(IPresentationCommand command)
    {
        Bus.Execute(command);
        PruneSelection();
    }

    private bool ExecuteBatch(string label, IEnumerable<IPresentationCommand> commands)
    {
        var batch = commands.ToArray();
        if (batch.Length == 0)
            return false;
        Execute(batch.Length == 1 ? batch[0] : new BatchCommand(label, batch));
        return true;
    }

    private MasterEditTarget RequireTarget() => _target ?? throw new InvalidOperationException("Select a slide master or layout before editing.");

    private IReadOnlyList<MasterEditTarget> BuildTargets()
    {
        var targets = new List<MasterEditTarget>();
        foreach (var master in Presentation.Masters)
        {
            targets.Add(MasterEditTarget.Master(master.Id));
            targets.AddRange(Presentation.Layouts
                .Where(layout => layout.MasterId == master.Id)
                .Select(layout => MasterEditTarget.Layout(layout.Id)));
        }
        return targets;
    }

    private uint AllocateShapeId()
    {
        _shapeIdWatermark ??= Presentation.Slides.SelectMany(SlideShapeTraversal.EnumerateDepthFirst)
            .Concat(Presentation.Masters.SelectMany(master => SlideShapeTraversal.EnumerateDepthFirst(master.Placeholders)))
            .Concat(Presentation.Layouts.SelectMany(layout => SlideShapeTraversal.EnumerateDepthFirst(layout.Placeholders)))
            .Select(shape => shape.Id)
            .DefaultIfEmpty()
            .Max();
        _shapeIdWatermark = _shapeIdWatermark.Value + 1u;
        return _shapeIdWatermark.Value;
    }

    private void PruneSelection()
    {
        var live = SlideShapeTraversal.EnumerateDepthFirst(CurrentShapes).Select(shape => shape.Id).ToHashSet();
        if (_selectedShapeIds.RemoveAll(id => !live.Contains(id)) > 0)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
