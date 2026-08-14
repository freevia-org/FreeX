using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public static class PresentationCanvasAutomationRoleMapper
{
    public static TNative Map<TNative>(
        PresentationCanvasAutomationRole role,
        TNative canvas,
        TNative image,
        TNative dataGrid,
        TNative custom) =>
        role switch
        {
            PresentationCanvasAutomationRole.Canvas => canvas,
            PresentationCanvasAutomationRole.Image => image,
            PresentationCanvasAutomationRole.DataGrid => dataGrid,
            _ => custom,
        };
}

public readonly record struct PresentationCanvasAutomationPeerSelectionChange<TPeer>(
    TPeer Peer,
    bool WasSelected,
    bool IsSelected)
    where TPeer : class;

public readonly record struct PresentationCanvasAutomationPeerFocusChange<TPeer>(
    TPeer? PreviousPeer,
    TPeer? CurrentPeer)
    where TPeer : class;

/// <summary>
/// Owns the framework-neutral orchestration around a renderer's virtual canvas peers.
/// Native renderers only translate the resulting peers, descriptors, bounds, and events.
/// </summary>
public sealed class PresentationCanvasAutomationPeerCoordinator<TPeer>
    where TPeer : class
{
    private readonly PresentationCanvasAutomationSession _automation;
    private readonly Func<Presentation?> _getPresentation;
    private readonly Func<Slide?> _getSlide;
    private readonly Func<IReadOnlyList<uint>?> _getSelectedShapeIds;
    private readonly Func<uint, TPeer> _createPeer;
    private readonly Dictionary<uint, TPeer> _peers = [];

    public PresentationCanvasAutomationPeerCoordinator(
        PresentationCanvasAutomationSession automation,
        Func<Presentation?> getPresentation,
        Func<Slide?> getSlide,
        Func<IReadOnlyList<uint>?> getSelectedShapeIds,
        Func<uint, TPeer> createPeer)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(getSlide);
        ArgumentNullException.ThrowIfNull(getSelectedShapeIds);
        ArgumentNullException.ThrowIfNull(createPeer);

        _automation = automation;
        _getPresentation = getPresentation;
        _getSlide = getSlide;
        _getSelectedShapeIds = getSelectedShapeIds;
        _createPeer = createPeer;
    }

    public bool CanSelectMultiple => _automation.CanSelectMultiple;

    public bool IsSelectionRequired => _automation.IsSelectionRequired;

    public PresentationCanvasAutomationDescriptor CanvasDescriptor =>
        _automation.ProjectCanvas(_getPresentation(), _getSlide());

    public IReadOnlyList<TPeer> GetSelection() =>
        _automation.ProjectSelection(_getSlide(), _getSelectedShapeIds())
            .Select(descriptor => GetOrCreatePeer(descriptor.ShapeId!.Value))
            .ToArray();

    public IReadOnlyList<TPeer> SynchronizeChildren() =>
        PresentationAutomationPeerCache.Synchronize(
            _automation.ProjectShapes(_getSlide(), _getSelectedShapeIds()),
            _peers,
            _createPeer);

    public TPeer GetOrCreatePeer(uint shapeId) =>
        PresentationAutomationPeerCache.GetOrCreate(_peers, shapeId, _createPeer);

    public bool TryGetShape(
        uint shapeId,
        out PresentationCanvasAutomationDescriptor descriptor) =>
        _automation.TryProjectShape(
            _getSlide(),
            shapeId,
            _getSelectedShapeIds(),
            out descriptor);

    public bool IsShapeSelected(uint shapeId) =>
        TryGetShape(shapeId, out var descriptor) && descriptor.IsSelected;

    public bool HasShapeKeyboardFocus(uint shapeId) =>
        TryGetShape(shapeId, out var descriptor) && descriptor.HasKeyboardFocus;

    public void RequestSelectionMutation(
        uint shapeId,
        PresentationCanvasAutomationSelectionMutation mutation) =>
        _automation.RequestSelectionMutation(shapeId, mutation);

    public bool TryProjectLocalBounds(
        uint shapeId,
        SlideTransformCore transform,
        out SlideScreenRect localBounds)
    {
        if (TryGetShape(shapeId, out var descriptor))
            return _automation.TryProjectLocalBounds(descriptor, transform, out localBounds);

        localBounds = default;
        return false;
    }

    public IReadOnlyList<PresentationCanvasAutomationPeerSelectionChange<TPeer>>
        GetSelectionChanges(PresentationCanvasAutomationSelectionDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        var changes = new List<PresentationCanvasAutomationPeerSelectionChange<TPeer>>(
            delta.RemovedShapeIds.Length + delta.AddedShapeIds.Length);
        foreach (var shapeId in delta.RemovedShapeIds)
        {
            if (_peers.TryGetValue(shapeId, out var peer))
                changes.Add(new(peer, WasSelected: true, IsSelected: false));
        }

        foreach (var shapeId in delta.AddedShapeIds)
            changes.Add(new(GetOrCreatePeer(shapeId), WasSelected: false, IsSelected: true));

        return changes;
    }

    public PresentationCanvasAutomationPeerFocusChange<TPeer> GetFocusChange(
        PresentationCanvasAutomationSelectionDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        TPeer? previousPeer = null;
        if (delta.Previous.FocusedShapeId is { } previousId)
            _peers.TryGetValue(previousId, out previousPeer);

        var currentPeer = delta.Current.FocusedShapeId is { } currentId
            ? GetOrCreatePeer(currentId)
            : null;
        return new(previousPeer, currentPeer);
    }
}
