using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Selects the native control factory for a Zoom Object Properties field.</summary>
public sealed class ZoomObjectPropertiesNativeControlFactory<TControl>
    where TControl : class
{
    private readonly Func<ZoomObjectPropertiesDialogControlPlan, TControl> _createToggle;
    private readonly Func<ZoomObjectPropertiesDialogControlPlan, double, TControl> _createText;
    private readonly Func<ZoomObjectPropertiesDialogControlPlan, double, TControl> _createChoice;

    public ZoomObjectPropertiesNativeControlFactory(
        Func<ZoomObjectPropertiesDialogControlPlan, TControl> createToggle,
        Func<ZoomObjectPropertiesDialogControlPlan, double, TControl> createText,
        Func<ZoomObjectPropertiesDialogControlPlan, double, TControl> createChoice)
    {
        _createToggle = createToggle ?? throw new ArgumentNullException(nameof(createToggle));
        _createText = createText ?? throw new ArgumentNullException(nameof(createText));
        _createChoice = createChoice ?? throw new ArgumentNullException(nameof(createChoice));
    }

    public TControl Create(ZoomObjectPropertiesDialogControlPlan plan, double inputMinWidth) =>
        plan.Kind switch
        {
            ZoomObjectPropertiesDialogControlKind.Toggle => _createToggle(plan),
            ZoomObjectPropertiesDialogControlKind.Text => _createText(plan, inputMinWidth),
            ZoomObjectPropertiesDialogControlKind.Choice => _createChoice(plan, inputMinWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(plan)),
        };
}

/// <summary>Owns the renderer-neutral Zoom Object Properties host composition.</summary>
public sealed class ZoomObjectPropertiesDialogNativeRendererSession<TControl>
    where TControl : class
{
    public ZoomObjectPropertiesDialogNativeRendererSession(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties,
        Action<TControl, ZoomObjectPropertiesDialogFieldState> applyFieldState,
        Action<TControl, bool> focus,
        ZoomObjectPropertiesNativeControlFactory<TControl> controlFactory)
    {
        Session = new(current, summaryTargets, summaryTileProperties);
        Surface = Session.Surface;
        Form = new(Session.Dispatch, applyFieldState, focus);
        ControlFactory = controlFactory ?? throw new ArgumentNullException(nameof(controlFactory));
    }

    public ZoomObjectPropertiesDialogSession Session { get; }

    public ZoomObjectPropertiesDialogSurfacePlan Surface { get; }

    public ZoomObjectPropertiesDialogFormSession<TControl> Form { get; }

    public ZoomObjectPropertiesNativeControlFactory<TControl> ControlFactory { get; }

    public ZoomObjectProperties Properties => Session.CommitPlan.Properties;

    public ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout =>
        Session.CommitPlan.SummaryTileLayout;

    public ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties =>
        Session.CommitPlan.SummaryTileProperties;

    public bool ApplySummaryPropertiesToAllTiles =>
        Session.CommitPlan.ApplySummaryPropertiesToAllTiles;
}
