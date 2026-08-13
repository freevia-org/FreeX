using System.Collections.Immutable;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral roles exposed by the slide canvas automation tree.
/// </summary>
public enum PresentationCanvasAutomationRole
{
    Canvas,
    Shape,
    Image,
    DataGrid,
}

/// <summary>
/// Describes how a renderer should update automation focus after a selection change.
/// </summary>
public enum PresentationCanvasAutomationFocusIntent
{
    None,
    MoveToShape,
    ClearShapeFocus,
}

/// <summary>
/// Selection-provider operations requested by an automation client.
/// </summary>
public enum PresentationCanvasAutomationSelectionMutation
{
    Select,
    Add,
    Remove,
}

/// <summary>
/// Shape geometry retained in presentation coordinates for renderer-specific projection.
/// </summary>
public readonly record struct PresentationCanvasAutomationBounds(
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu);

/// <summary>
/// Framework-free UI Automation projection for the canvas or one of its virtual shapes.
/// </summary>
public sealed record PresentationCanvasAutomationDescriptor(
    uint? ShapeId,
    string AutomationId,
    string ClassName,
    string Name,
    string HelpText,
    string LocalizedControlType,
    PresentationCanvasAutomationRole Role,
    PresentationCanvasAutomationBounds? Bounds,
    bool IsSelected,
    bool HasKeyboardFocus);

/// <summary>
/// Detached, immutable selection state used as the basis for automation notifications.
/// </summary>
public sealed record PresentationCanvasAutomationSelectionSnapshot(
    ImmutableArray<uint> ShapeIds,
    uint? FocusedShapeId)
{
    public static PresentationCanvasAutomationSelectionSnapshot Empty { get; } =
        new(ImmutableArray<uint>.Empty, null);
}

/// <summary>
/// Ordered selection changes and the resulting renderer focus action.
/// </summary>
public sealed record PresentationCanvasAutomationSelectionDelta(
    PresentationCanvasAutomationSelectionSnapshot Previous,
    PresentationCanvasAutomationSelectionSnapshot Current,
    ImmutableArray<uint> AddedShapeIds,
    ImmutableArray<uint> RemovedShapeIds,
    PresentationCanvasAutomationFocusIntent FocusIntent)
{
    public bool HasChanges =>
        !Previous.ShapeIds.AsSpan().SequenceEqual(Current.ShapeIds.AsSpan());
}

/// <summary>
/// Owns the shared automation tree, immutable selection history, notification deltas, and
/// focus policy for WPF and Avalonia slide canvases.
/// </summary>
public sealed class PresentationCanvasAutomationSession
{
    public const string CanvasClassName = "SlideCanvas";
    public const string ShapeClassName = "SlideShape";
    public const string ShapeLocalizedControlType = "shape";
    public const string SelectionMutationNotSupportedMessage =
        "Shape selection is owned by the slide canvas's editing session.";

    private PresentationCanvasAutomationSelectionSnapshot _selection =
        PresentationCanvasAutomationSelectionSnapshot.Empty;

    public PresentationCanvasAutomationSelectionSnapshot Selection => _selection;

    public bool CanSelectMultiple => true;

    public bool IsSelectionRequired => false;

    public PresentationCanvasAutomationDescriptor ProjectCanvas(
        Presentation? presentation,
        Slide? slide)
    {
        var name = "Slide canvas";
        if (presentation is not null && slide is not null)
        {
            var slideIndex = presentation.Slides.IndexOf(slide);
            if (slideIndex >= 0)
                name = $"Slide {slideIndex + 1} canvas";
        }

        return new PresentationCanvasAutomationDescriptor(
            ShapeId: null,
            AutomationId: string.Empty,
            ClassName: CanvasClassName,
            Name: name,
            HelpText: string.Empty,
            LocalizedControlType: string.Empty,
            Role: PresentationCanvasAutomationRole.Canvas,
            Bounds: null,
            IsSelected: false,
            HasKeyboardFocus: false);
    }

    public ImmutableArray<PresentationCanvasAutomationDescriptor> ProjectShapes(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        if (slide is null || slide.Shapes.Count == 0)
            return ImmutableArray<PresentationCanvasAutomationDescriptor>.Empty;

        var selection = BuildSelectionSnapshot(slide, selectedShapeIds);
        var selectedIds = selection.ShapeIds.ToHashSet();
        var descriptors = ImmutableArray.CreateBuilder<PresentationCanvasAutomationDescriptor>();
        var projectedIds = new HashSet<uint>();

        foreach (var shape in slide.Shapes)
        {
            if (shape.IsHidden || !projectedIds.Add(shape.Id))
                continue;

            descriptors.Add(ProjectShape(shape, selectedIds, selection.FocusedShapeId));
        }

        return descriptors.ToImmutable();
    }

    public ImmutableArray<PresentationCanvasAutomationDescriptor> ProjectSelection(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        var selection = BuildSelectionSnapshot(slide, selectedShapeIds);
        if (slide is null || selection.ShapeIds.IsEmpty)
            return ImmutableArray<PresentationCanvasAutomationDescriptor>.Empty;

        var visibleShapes = slide.Shapes
            .Where(shape => !shape.IsHidden)
            .GroupBy(shape => shape.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var selectedIds = selection.ShapeIds.ToHashSet();
        var descriptors = ImmutableArray.CreateBuilder<PresentationCanvasAutomationDescriptor>(
            selection.ShapeIds.Length);

        foreach (var shapeId in selection.ShapeIds)
        {
            if (visibleShapes.TryGetValue(shapeId, out var shape))
                descriptors.Add(ProjectShape(shape, selectedIds, selection.FocusedShapeId));
        }

        return descriptors.ToImmutable();
    }

    public bool TryProjectShape(
        Slide? slide,
        uint shapeId,
        IReadOnlyList<uint>? selectedShapeIds,
        out PresentationCanvasAutomationDescriptor descriptor)
    {
        if (slide is not null)
        {
            var shape = slide.Shapes.FirstOrDefault(candidate =>
                candidate.Id == shapeId && !candidate.IsHidden);
            if (shape is not null)
            {
                var selection = BuildSelectionSnapshot(slide, selectedShapeIds);
                descriptor = ProjectShape(
                    shape,
                    selection.ShapeIds.ToHashSet(),
                    selection.FocusedShapeId);
                return true;
            }
        }

        descriptor = null!;
        return false;
    }

    public bool TryProjectLocalBounds(
        PresentationCanvasAutomationDescriptor descriptor,
        SlideTransformCore transform,
        out SlideScreenRect localBounds)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(transform);

        if (descriptor.Bounds is not { } bounds)
        {
            localBounds = default;
            return false;
        }

        var topLeft = transform.SlideToScreen(
            SlideTransformCore.EmuToDip(bounds.OffsetXEmu),
            SlideTransformCore.EmuToDip(bounds.OffsetYEmu));
        var bottomRight = transform.SlideToScreen(
            SlideTransformCore.EmuToDip(bounds.OffsetXEmu + bounds.ExtentCxEmu),
            SlideTransformCore.EmuToDip(bounds.OffsetYEmu + bounds.ExtentCyEmu));
        localBounds = new SlideScreenRect(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        return true;
    }

    /// <summary>
    /// Seeds notification state from a new editing session without raising a synthetic delta.
    /// The selection is copied so later in-place mutations cannot alter this baseline.
    /// </summary>
    public void ResetSelection(Slide? slide, IReadOnlyList<uint>? selectedShapeIds) =>
        _selection = BuildSelectionSnapshot(slide, selectedShapeIds);

    /// <summary>
    /// Captures the current selection and returns the notification work required by a renderer.
    /// </summary>
    public PresentationCanvasAutomationSelectionDelta CaptureSelectionDelta(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        var previous = _selection;
        var current = BuildSelectionSnapshot(slide, selectedShapeIds);
        _selection = current;

        var previousIds = previous.ShapeIds.ToHashSet();
        var currentIds = current.ShapeIds.ToHashSet();
        var added = current.ShapeIds.Where(shapeId => !previousIds.Contains(shapeId)).ToImmutableArray();
        var removed = previous.ShapeIds.Where(shapeId => !currentIds.Contains(shapeId)).ToImmutableArray();
        var focusIntent = previous.FocusedShapeId == current.FocusedShapeId
            ? PresentationCanvasAutomationFocusIntent.None
            : current.FocusedShapeId is null
                ? PresentationCanvasAutomationFocusIntent.ClearShapeFocus
                : PresentationCanvasAutomationFocusIntent.MoveToShape;

        return new PresentationCanvasAutomationSelectionDelta(
            previous,
            current,
            added,
            removed,
            focusIntent);
    }

    /// <summary>
    /// Keeps automation providers read-only because all selection changes must pass through
    /// EditingSession, which owns command state and SelectionChanged notification ordering.
    /// </summary>
    public void RequestSelectionMutation(
        uint shapeId,
        PresentationCanvasAutomationSelectionMutation mutation) =>
        throw new InvalidOperationException(SelectionMutationNotSupportedMessage);

    private static PresentationCanvasAutomationSelectionSnapshot BuildSelectionSnapshot(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        if (slide is null || selectedShapeIds is null || selectedShapeIds.Count == 0)
            return PresentationCanvasAutomationSelectionSnapshot.Empty;

        var visibleShapeIds = slide.Shapes
            .Where(shape => !shape.IsHidden)
            .Select(shape => shape.Id)
            .ToHashSet();
        var seen = new HashSet<uint>();
        var selected = ImmutableArray.CreateBuilder<uint>(selectedShapeIds.Count);

        foreach (var shapeId in selectedShapeIds)
        {
            if (visibleShapeIds.Contains(shapeId) && seen.Add(shapeId))
                selected.Add(shapeId);
        }

        if (selected.Count == 0)
            return PresentationCanvasAutomationSelectionSnapshot.Empty;

        var shapeIds = selected.ToImmutable();
        return new PresentationCanvasAutomationSelectionSnapshot(shapeIds, shapeIds[^1]);
    }

    private static PresentationCanvasAutomationDescriptor ProjectShape(
        SlideShape shape,
        IReadOnlySet<uint> selectedShapeIds,
        uint? focusedShapeId)
    {
        var name = !string.IsNullOrWhiteSpace(shape.Name)
            ? shape.Name
            : !string.IsNullOrWhiteSpace(shape.AlternativeTextTitle)
                ? shape.AlternativeTextTitle
                : !string.IsNullOrWhiteSpace(shape.AlternativeText)
                    ? shape.AlternativeText
                    : $"Shape {shape.Id}";

        return new PresentationCanvasAutomationDescriptor(
            ShapeId: shape.Id,
            AutomationId: $"Shape_{shape.Id}",
            ClassName: ShapeClassName,
            Name: name,
            HelpText: shape.AlternativeText,
            LocalizedControlType: ShapeLocalizedControlType,
            Role: shape.Kind switch
            {
                SlideShapeKind.Picture => PresentationCanvasAutomationRole.Image,
                SlideShapeKind.Table => PresentationCanvasAutomationRole.DataGrid,
                _ => PresentationCanvasAutomationRole.Shape,
            },
            Bounds: new PresentationCanvasAutomationBounds(
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu),
            IsSelected: selectedShapeIds.Contains(shape.Id),
            HasKeyboardFocus: focusedShapeId == shape.Id);
    }
}
