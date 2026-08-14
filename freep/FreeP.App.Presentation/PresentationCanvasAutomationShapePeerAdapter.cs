namespace FreeP.App.Compositor;

/// <summary>
/// Projects one portable shape automation descriptor into a native peer's property surface.
/// </summary>
public sealed class PresentationCanvasAutomationShapePeerAdapter<TPeer, TNativeRole, TBounds>
    where TPeer : class
{
    private readonly PresentationCanvasAutomationPeerCoordinator<TPeer> _coordinator;
    private readonly uint _shapeId;
    private readonly Func<PresentationCanvasAutomationRole, TNativeRole> _mapRole;
    private readonly TNativeRole _fallbackRole;
    private readonly Func<uint, TBounds> _getBounds;

    public PresentationCanvasAutomationShapePeerAdapter(
        PresentationCanvasAutomationPeerCoordinator<TPeer> coordinator,
        uint shapeId,
        Func<PresentationCanvasAutomationRole, TNativeRole> mapRole,
        TNativeRole fallbackRole,
        Func<uint, TBounds> getBounds)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(mapRole);
        ArgumentNullException.ThrowIfNull(getBounds);
        _coordinator = coordinator;
        _shapeId = shapeId;
        _mapRole = mapRole;
        _fallbackRole = fallbackRole;
        _getBounds = getBounds;
    }

    public bool IsSelected => _coordinator.IsShapeSelected(_shapeId);

    public bool HasKeyboardFocus => _coordinator.HasShapeKeyboardFocus(_shapeId);

    public string Name => TryGetDescriptor(out var descriptor) ? descriptor.Name : string.Empty;

    public string ClassName => TryGetDescriptor(out var descriptor)
        ? descriptor.ClassName
        : PresentationCanvasAutomationSession.ShapeClassName;

    public string AutomationId => TryGetDescriptor(out var descriptor)
        ? descriptor.AutomationId
        : string.Empty;

    public string HelpText => TryGetDescriptor(out var descriptor)
        ? descriptor.HelpText
        : string.Empty;

    public string LocalizedControlType => TryGetDescriptor(out var descriptor)
        ? descriptor.LocalizedControlType
        : PresentationCanvasAutomationSession.ShapeLocalizedControlType;

    public TNativeRole Role => TryGetDescriptor(out var descriptor)
        ? _mapRole(descriptor.Role)
        : _fallbackRole;

    public TBounds Bounds => _getBounds(_shapeId);

    public void Select() => Request(PresentationCanvasAutomationSelectionMutation.Select);

    public void AddToSelection() => Request(PresentationCanvasAutomationSelectionMutation.Add);

    public void RemoveFromSelection() => Request(PresentationCanvasAutomationSelectionMutation.Remove);

    private bool TryGetDescriptor(out PresentationCanvasAutomationDescriptor descriptor) =>
        _coordinator.TryGetShape(_shapeId, out descriptor);

    private void Request(PresentationCanvasAutomationSelectionMutation mutation) =>
        _coordinator.RequestSelectionMutation(_shapeId, mutation);
}
