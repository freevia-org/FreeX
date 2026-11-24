namespace FreeP.App.Compositor;

/// <summary>How a body-grid column is sized in a view mode.</summary>
public enum PresentationViewModeColumnSize
{
    /// <summary>Sized to its content (the normal slide pane column).</summary>
    Auto,

    /// <summary>Takes the remaining space (one star).</summary>
    Fill,

    /// <summary>Collapsed to zero width.</summary>
    Hidden,
}

/// <summary>
/// The structural layout both FreeP shells apply for a view mode: which body columns are shown,
/// whether the slide pane fills its column, and the notes-pane / canvas-host visibility and styling.
/// </summary>
public readonly record struct PresentationViewModeLayoutPlan(
    bool IsOutline,
    bool IsSlideSorter,
    bool IsNotesPage,
    bool IsSlideMaster,
    PresentationViewModeColumnSize PaneColumn,
    PresentationViewModeColumnSize CanvasColumn,
    bool SlidePaneFillsWidth,
    bool CanvasHostVisible,
    bool NotesPaneVisible,
    bool NotesPageSurface);

public static class PresentationViewModeLayoutPlanner
{
    public static PresentationViewModeLayoutPlan Plan(PresentationViewModeState state, bool showNotesPane)
    {
        var isSlideSorter = state.Mode == PresentationViewMode.SlideSorter;
        var isNotesPage = state.Mode == PresentationViewMode.NotesPage;
        return new PresentationViewModeLayoutPlan(
            IsOutline: state.Mode == PresentationViewMode.Outline,
            IsSlideSorter: isSlideSorter,
            IsNotesPage: isNotesPage,
            IsSlideMaster: state.Mode == PresentationViewMode.SlideMaster,
            PaneColumn: isSlideSorter
                ? PresentationViewModeColumnSize.Fill
                : isNotesPage ? PresentationViewModeColumnSize.Hidden : PresentationViewModeColumnSize.Auto,
            CanvasColumn: isSlideSorter
                ? PresentationViewModeColumnSize.Hidden
                : PresentationViewModeColumnSize.Fill,
            SlidePaneFillsWidth: isSlideSorter || isNotesPage,
            CanvasHostVisible: !isSlideSorter,
            NotesPaneVisible: IsNotesPaneVisible(state, showNotesPane),
            NotesPageSurface: isNotesPage);
    }

    /// <summary>The notes pane shows in notes-page view, or in the other editable views when enabled.</summary>
    public static bool IsNotesPaneVisible(PresentationViewModeState state, bool showNotesPane) =>
        state.Mode is not PresentationViewMode.SlideSorter and not PresentationViewMode.SlideMaster &&
        (state.Mode == PresentationViewMode.NotesPage || showNotesPane);
}
