using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// The host delegates both FreeP shells supply to build their renderer-neutral workflow sessions.
/// Everything else the sessions need is the shared editor accessor.
/// </summary>
public sealed record PresentationHostSessionPorts(
    Func<EditingSession> GetEditor,
    Action MarkDirty,
    Action RefreshCanvas,
    Action UpdateHost,
    Func<Presentation, int, int, int, byte[]> RenderSlidePreview,
    PresentationDomainContextMenuSessionCallbacks DomainContextMenu,
    Action<PresentationSmartArtTextPanePlan> RenderSmartArtTextPane);

/// <summary>The sessions every FreeP shell owns for the lifetime of a main window.</summary>
public sealed record PresentationHostSessions(
    PresentationSmartArtTextPaneSession SmartArtTextPane,
    PresentationZoomAuthoringSession ZoomAuthoring,
    PresentationDomainContextMenuSession DomainContextMenu,
    PresentationNotesPaneSession NotesPane,
    PresentationHyperlinkWorkflowSession HyperlinkWorkflow,
    SlideShowCustomShowSession CustomShow,
    AnimationPaneSession AnimationPane);

/// <summary>
/// Builds the session set that the WPF and Avalonia main-window constructors previously assembled
/// by hand with identical shapes. Hosts keep their review callbacks (pane coordinators are native)
/// and pass only the delegates in <see cref="PresentationHostSessionPorts"/>.
/// </summary>
public static class PresentationHostSessionFactory
{
    public static PresentationHostSessions Create(PresentationHostSessionPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var getEditor = ports.GetEditor ?? throw new ArgumentNullException(nameof(ports));
        return new PresentationHostSessions(
            new PresentationSmartArtTextPaneSession(
                getEditor,
                new PresentationSmartArtTextPaneSessionCallbacks(
                    MarkDirty: ports.MarkDirty,
                    RefreshCanvas: ports.RefreshCanvas,
                    UpdateHost: ports.UpdateHost,
                    RenderPane: ports.RenderSmartArtTextPane)),
            new PresentationZoomAuthoringSession(
                getEditor,
                new PresentationZoomAuthoringSessionCallbacks(
                    ports.MarkDirty,
                    ports.RefreshCanvas,
                    ports.UpdateHost,
                    ports.RenderSlidePreview)),
            new PresentationDomainContextMenuSession(getEditor, ports.DomainContextMenu),
            new PresentationNotesPaneSession(getEditor),
            new PresentationHyperlinkWorkflowSession(getEditor),
            new SlideShowCustomShowSession(getEditor),
            new AnimationPaneSession(getEditor));
    }
}
