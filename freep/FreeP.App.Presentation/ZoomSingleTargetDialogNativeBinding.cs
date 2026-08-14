namespace FreeP.App.Compositor;

/// <summary>Owns single-target selection capture and acceptance for native Zoom dialogs.</summary>
public sealed class ZoomSingleTargetDialogNativeBinding<TControl>
    where TControl : class
{
    private readonly Func<TControl, int> _getSelectedIndex;
    private readonly Action _accepted;

    public ZoomSingleTargetDialogNativeBinding(
        ZoomTargetDialogKind kind,
        IReadOnlyList<(string Id, string DisplayName)> options,
        Func<ZoomSingleTargetDialogSession, TControl> createControl,
        Func<TControl, int> getSelectedIndex,
        Action accepted,
        string? selectedTargetId = null,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createControl);
        Session = new(kind, options, selectedTargetId, title);
        Control = createControl(Session) ?? throw new InvalidOperationException("The native Zoom control factory returned null.");
        _getSelectedIndex = getSelectedIndex ?? throw new ArgumentNullException(nameof(getSelectedIndex));
        _accepted = accepted ?? throw new ArgumentNullException(nameof(accepted));
    }

    public ZoomSingleTargetDialogSession Session { get; }

    public TControl Control { get; }

    public PresentationDialogSurfacePlan<ZoomTargetDialogField, ZoomTargetDialogAction> Surface =>
        Session.Surface;

    public string? SelectedTargetId => Session.SelectedTargetId;

    public bool TryAccept()
    {
        if (!Session.TryAccept(_getSelectedIndex(Control)))
            return false;

        _accepted();
        return true;
    }
}
