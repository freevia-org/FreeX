using Free.Shared.Drawing;
using Free.Shared.Commands;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-free editing session for PowerPoint Slide Master view. It intentionally owns a
/// master/layout target rather than making <see cref="EditingSession"/> pretend that master
/// placeholders are slide shapes. Both native hosts share this session and its undo bus.
/// </summary>
public sealed class MasterEditingSession : ICanvasGestureEditingSession
{
    private readonly List<uint> _selectedShapeIds = new();
    private readonly Slide _previewSlide = new();
    private uint? _shapeIdWatermark;
    private MasterEditTarget? _target;

    public MasterEditingSession(Presentation presentation, PresentationCommandBus bus)
    {
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Bus = bus ?? throw new ArgumentNullException(nameof(bus));
        var targets = BuildTargets();
        _target = targets.Count > 0 ? targets[0] : null;
        RefreshPreviewSlide();
        Bus.Changed += OnBusChanged;
    }

    public Presentation Presentation { get; }
    public PresentationCommandBus Bus { get; }
    public MasterEditTarget? Target => _target;
    public IReadOnlyList<uint> SelectedShapeIds => _selectedShapeIds;
    /// <summary>
    /// A shallow stage projection whose shapes reference the active master/layout target. It is
    /// only for shared hit-testing and gesture geometry; mutations still use master commands.
    /// </summary>
    public Slide? CurrentSlide => _target is null ? null : _previewSlide;
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
    public event EventHandler? CurrentSlideChanged;
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
        RefreshPreviewSlide();
        TargetChanged?.Invoke(this, EventArgs.Empty);
        CurrentSlideChanged?.Invoke(this, EventArgs.Empty);
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

    public void ResizeShape(uint shapeId, long x, long y, long cx, long cy) => Resize(shapeId, x, y, cx, cy);

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

    public void RotateShape(uint shapeId, double rotationDeg) => Rotate(shapeId, rotationDeg);

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

    // Master placeholders intentionally do not opt into the slide-only format painter, picture
    // crop, or geometry-point tools. Returning no-op/false keeps the shared gesture router honest
    // while core selection and transform gestures remain available in both native hosts.
    public bool IsFormatPainterActive => false;
    public bool BeginFormatPainter() => false;
    public void CancelFormatPainter() { }
    public bool TryApplyFormatPainterToShape(uint targetShapeId) => false;
    public void SelectSlide(int index) { }
    public bool SetPictureCrop(uint shapeId, PictureCropValues values) => false;
    public void SetShapeGeometryAdjustment(uint shapeId, string name, double? value) { }
    public void SetCustomGeometryPoint(uint shapeId, int pathIndex, int segmentIndex, double x, double y,
        CustomGeometryPointSlot slot = CustomGeometryPointSlot.Endpoint) { }
    public void SetCustomGeometryArcPoint(uint shapeId, int pathIndex, int segmentIndex, double value,
        CustomGeometryArcPointSlot slot) { }
    public bool TryInsertCustomGeometryPoint(uint shapeId, string handleName) => false;
    public bool TryDeleteCustomGeometryPoint(uint shapeId, string handleName) => false;

    private void Execute(IPresentationCommand command)
    {
        Bus.Execute(command);
        PruneSelection();
    }

    private void OnBusChanged()
    {
        RefreshPreviewSlide();
        PruneSelection();
    }

    private void RefreshPreviewSlide()
    {
        _previewSlide.LayoutId = _target is { Kind: MasterEditTargetKind.Layout, Id: var layoutId }
            ? layoutId
            : _target is { Kind: MasterEditTargetKind.Master, Id: var masterId }
                ? Presentation.Layouts.FirstOrDefault(layout => layout.MasterId == masterId)?.Id
                : null;
        _previewSlide.Shapes.Clear();
        _previewSlide.Shapes.AddRange(CurrentShapes);
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
