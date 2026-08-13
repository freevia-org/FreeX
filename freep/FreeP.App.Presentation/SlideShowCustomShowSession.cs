using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCaptionPlaybackSelection(
    int SlideIndex,
    uint ShapeId,
    int TrackIndex);

public sealed record SlideShowPlaybackLaunchPlan(
    SlideShowPlaybackRoute Route,
    SlideShowCaptionPlaybackSelection? CaptionSelection);

/// <summary>
/// Owns renderer-neutral custom-show routing, dialog projection, and undoable authoring.
/// Hosts retain native dialogs, slideshow windows, ownership, and focus behavior.
/// </summary>
public sealed class SlideShowCustomShowSession
{
    private readonly Func<EditingSession> _getEditor;

    public SlideShowCustomShowSession(Func<EditingSession> getEditor)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
    }

    public SlideShowLaunchPlan BuildLaunchPlan()
    {
        var editor = _getEditor();
        return SlideShowCustomShowPlanner.BuildLaunchPlan(
            editor.Presentation,
            editor.CurrentSlideIndex);
    }

    public SlideShowCustomShowAuthoringPlan BuildAuthoringPlan() =>
        SlideShowCustomShowPlanner.BuildAuthoringPlan(_getEditor().Presentation);

    public SlideShowCustomShowSessionPlan BuildDialogPlan(SlideShowCustomShowSessionState state) =>
        SlideShowCustomShowSessionPlanner.BuildPlan(BuildAuthoringPlan(), state);

    public SlideShowCustomShowDialogSession CreateDialogSession(
        Func<string?, bool> tryStartShow,
        SlideShowCustomShowSessionState? initialState = null)
    {
        ArgumentNullException.ThrowIfNull(tryStartShow);
        return new SlideShowCustomShowDialogSession(
            new SlideShowCustomShowDialogSessionCallbacks(
                BuildDialogPlan,
                ApplyMutation,
                tryStartShow),
            initialState);
    }

    public bool TryBuildLaunchRoute(
        bool fromStart,
        int? animationStartIndex,
        out SlideShowPlaybackRoute route)
    {
        var editor = _getEditor();
        var choiceId = fromStart
            ? SlideShowCustomShowPlanner.FullPresentationChoiceId
            : SlideShowCustomShowPlanner.FromCurrentSlideChoiceId;
        if (!SlideShowCustomShowPlanner.TryBuildRouteForLaunchChoice(
                editor.Presentation,
                choiceId,
                editor.CurrentSlideIndex,
                out route))
        {
            return false;
        }

        if (animationStartIndex is int selectedAnimationIndex)
            route = route.WithAnimationStartIndex(selectedAnimationIndex);
        return route.SlideCount > 0;
    }

    public bool TryBuildPlaybackLaunch(
        bool fromStart,
        int? animationStartIndex,
        int? selectedCaptionTrackIndex,
        out SlideShowPlaybackLaunchPlan launchPlan)
    {
        if (!TryBuildLaunchRoute(fromStart, animationStartIndex, out var route))
        {
            launchPlan = null!;
            return false;
        }

        launchPlan = BuildPlaybackLaunchPlan(route, selectedCaptionTrackIndex);
        return true;
    }

    public bool TryBuildNamedRoute(
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route) =>
        SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            _getEditor().Presentation,
            customShowName,
            startIndex,
            out route);

    public bool TryBuildNamedPlaybackLaunch(
        string? customShowName,
        int startIndex,
        int? selectedCaptionTrackIndex,
        out SlideShowPlaybackLaunchPlan launchPlan)
    {
        if (!TryBuildNamedRoute(customShowName, startIndex, out var route) || route.SlideCount == 0)
        {
            launchPlan = null!;
            return false;
        }

        launchPlan = BuildPlaybackLaunchPlan(route, selectedCaptionTrackIndex);
        return true;
    }

    public SlideShowCustomShowMutationResult ApplyMutation(
        SlideShowCustomShowDialogMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _getEditor().ApplyCustomShowMutation(request.Apply);
    }

    public SlideShowCustomShowMutationResult Create(
        string? name,
        IEnumerable<string?> slideIds) =>
        ApplyMutation(SlideShowCustomShowDialogMutationRequest.Create(name, slideIds));

    public SlideShowCustomShowMutationResult Rename(int customShowIndex, string? name) =>
        ApplyMutation(SlideShowCustomShowDialogMutationRequest.Rename(customShowIndex, name));

    public SlideShowCustomShowMutationResult Delete(int customShowIndex) =>
        ApplyMutation(SlideShowCustomShowDialogMutationRequest.Delete(customShowIndex));

    public SlideShowCustomShowMutationResult UpdateSlides(
        int customShowIndex,
        IEnumerable<string?> slideIds) =>
        ApplyMutation(SlideShowCustomShowDialogMutationRequest.UpdateSlides(customShowIndex, slideIds));

    public SlideShowCustomShowMutationResult MoveSlide(
        int customShowIndex,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetSlideIndex) =>
        ApplyMutation(SlideShowCustomShowDialogMutationRequest.MoveSlide(
            customShowIndex,
            sourceSlideIndex,
            sourceSlideId,
            targetSlideIndex));

    private SlideShowPlaybackLaunchPlan BuildPlaybackLaunchPlan(
        SlideShowPlaybackRoute route,
        int? selectedCaptionTrackIndex)
    {
        var editor = _getEditor();
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            editor.CurrentSlide,
            editor.SelectedShapeIds);
        var captionSelection = mediaShape is not null && selectedCaptionTrackIndex is int trackIndex
            ? new SlideShowCaptionPlaybackSelection(editor.CurrentSlideIndex, mediaShape.Id, trackIndex)
            : null;
        return new SlideShowPlaybackLaunchPlan(route, captionSelection);
    }
}
