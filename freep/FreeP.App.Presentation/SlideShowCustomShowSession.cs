using FreeP.Core.Model;

namespace FreeP.App.Compositor;

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

    public bool TryBuildNamedRoute(
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route) =>
        SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            _getEditor().Presentation,
            customShowName,
            startIndex,
            out route);

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
}
