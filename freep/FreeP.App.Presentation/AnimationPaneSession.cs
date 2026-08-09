using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record AnimationPanePlaybackTransition(
    AnimationPaneTimelinePlan Timeline,
    AnimationPanePlaybackSessionPlan Playback,
    AnimationPanePlaybackWorkflowEvidencePlan WorkflowEvidence,
    bool ShouldStartPreview);

/// <summary>
/// Owns renderer-neutral animation-pane selection, projection, playback, and mutation state.
/// Hosts retain native controls, rendering, focus, and slideshow-window ownership.
/// </summary>
public sealed class AnimationPaneSession
{
    private readonly Func<EditingSession> _getEditor;

    public AnimationPaneSession(Func<EditingSession> getEditor)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
    }

    public int SelectedAnimationIndex { get; private set; } = -1;

    public AnimationPaneTimelinePlan? Timeline { get; private set; }

    public AnimationPaneWorkflowEvidencePlan? WorkflowEvidence { get; private set; }

    public AnimationPanePlaybackSessionPlan? Playback { get; private set; }

    public AnimationPanePlaybackWorkflowEvidencePlan? PlaybackWorkflowEvidence { get; private set; }

    public AnimationPaneTimelinePlan Refresh(int? selectedAnimationIndex = null)
    {
        if (selectedAnimationIndex.HasValue)
            SelectedAnimationIndex = selectedAnimationIndex.Value;

        var editor = _getEditor();
        Timeline = AnimationPanePlanner.BuildTimelinePlan(
            editor.CurrentSlide,
            editor.SelectedShapeIds,
            SelectedAnimationIndex,
            isPlaybackRunning: Playback?.IsRunning == true);
        SelectedAnimationIndex = Timeline.SelectedIndex;
        WorkflowEvidence = AnimationPanePlanner.BuildWorkflowEvidencePlan(
            Timeline,
            editor.CurrentSlideIndex);
        return Timeline;
    }

    public void ResetSelection() => SelectedAnimationIndex = -1;

    public void Reset()
    {
        SelectedAnimationIndex = -1;
        Timeline = null;
        WorkflowEvidence = null;
        Playback = null;
        PlaybackWorkflowEvidence = null;
    }

    public AnimationPaneTimelinePlan SelectAnimation(int animationIndex)
    {
        var editor = _getEditor();
        SelectedAnimationIndex = animationIndex;
        if (animationIndex >= 0 && animationIndex < editor.CurrentSlideAnimations.Count)
            editor.Select(editor.CurrentSlideAnimations[animationIndex].ShapeId);

        return Refresh(animationIndex);
    }

    public AnimationPanePlaybackTransition ExecutePlayback(AnimationPanePlaybackControlKind controlKind)
    {
        var editor = _getEditor();
        var timeline = Refresh();
        var control = timeline.PlaybackControls.First(candidate => candidate.Kind == controlKind);
        Playback = AnimationPanePlanner.BuildPlaybackSessionPlan(timeline, controlKind);
        PlaybackWorkflowEvidence = AnimationPanePlanner.BuildPlaybackWorkflowEvidencePlan(
            timeline,
            Playback,
            Array.Empty<SlideShowAnimationStepVisualCheckpointPlan>(),
            editor.CurrentSlideIndex);
        var refreshedTimeline = Refresh();
        return new AnimationPanePlaybackTransition(
            refreshedTimeline,
            Playback,
            PlaybackWorkflowEvidence,
            control.IsEnabled && Playback.IsRunning);
    }

    public AnimationPaneEffectOptionMutationPlan ApplyEffectOption(int animationIndex, string optionId)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            optionId);
        if (AnimationPanePlanner.TryApplyEffectOptionMutation(editor, plan))
            Refresh();
        return plan;
    }

    public AnimationPaneTimingMutationPlan ApplyTrigger(int animationIndex, int selectedTriggerIndex)
    {
        var editor = _getEditor();
        return ApplyTiming(AnimationPanePlanner.BuildTriggerMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            selectedTriggerIndex));
    }

    public AnimationPaneTimingMutationPlan ApplyDuration(int animationIndex, string? text)
    {
        var editor = _getEditor();
        return ApplyTiming(AnimationPanePlanner.BuildDurationMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            text ?? string.Empty));
    }

    public AnimationPaneTimingMutationPlan ApplyDelay(int animationIndex, string? text)
    {
        var editor = _getEditor();
        return ApplyTiming(AnimationPanePlanner.BuildDelayMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            text ?? string.Empty));
    }

    public AnimationPaneEasingMutationPlan ApplyEasing(
        int animationIndex,
        string? accelerationText,
        string? decelerationText)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildEasingMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            accelerationText,
            decelerationText);
        if (AnimationPanePlanner.TryApplyEasingMutation(editor, plan))
            Refresh();
        return plan;
    }

    public AnimationPaneRepeatMutationPlan ApplyRepeat(
        int animationIndex,
        string? repeatText,
        bool autoReverse)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildRepeatMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            repeatText,
            autoReverse);
        if (AnimationPanePlanner.TryApplyRepeatMutation(editor, plan))
            Refresh();
        return plan;
    }

    public AnimationPaneParagraphBuildMutationPlan ToggleParagraphBuild(uint shapeId)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildParagraphBuildMutationPlan(editor.CurrentSlide, shapeId);
        if (AnimationPanePlanner.TryApplyParagraphBuildMutation(editor, plan))
            Refresh();
        return plan;
    }

    public AnimationPaneReorderMutationPlan MoveAnimation(int animationIndex, int offset)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildReorderMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex,
            offset);
        if (AnimationPanePlanner.TryApplyReorderMutation(editor, plan))
        {
            SelectedAnimationIndex = plan.SelectedAnimationIndex;
            Refresh();
        }
        return plan;
    }

    public AnimationPaneRemoveMutationPlan RemoveAnimation(int animationIndex)
    {
        var editor = _getEditor();
        var plan = AnimationPanePlanner.BuildRemoveMutationPlan(
            editor.CurrentSlideAnimations,
            animationIndex);
        if (AnimationPanePlanner.TryApplyRemoveMutation(editor, plan))
        {
            SelectedAnimationIndex = plan.SelectedAnimationIndex;
            Refresh();
        }
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyTiming(AnimationPaneTimingMutationPlan plan)
    {
        if (AnimationPanePlanner.TryApplyTimingMutation(_getEditor(), plan))
            Refresh();
        return plan;
    }
}
