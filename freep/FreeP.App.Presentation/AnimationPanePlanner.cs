using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record AnimationPaneDurationEditPlan(
    bool ShouldUpdate,
    int DurationMs,
    string DisplayText);

public enum AnimationPaneTimingEditKind
{
    Trigger,
    Duration,
    Delay,
}

public sealed record AnimationPaneEffectOptionDescriptor(
    string Id,
    string DisplayText,
    AnimationDirection? Direction,
    bool IsSelected,
    int? WheelSpokeCount = null,
    string? EffectSubtype = null,
    AnimationScaleBehavior? ScaleBehavior = null,
    string? PreservedNumericBehaviorXml = null,
    string? PreservedColorBehaviorXml = null,
    string? NativeColorToken = null,
    string? PreservedFontStyleBehaviorXml = null,
    string? NativeFontStyleProperty = null,
    bool? NativeFontStyleValue = null)
{
    public bool ReversesMotionPath { get; init; }
}

public sealed record AnimationPaneEffectOptionsPlan(
    bool CanApply,
    int AnimationIndex,
    string EffectText,
    string SelectedOptionText,
    IReadOnlyList<AnimationPaneEffectOptionDescriptor> Options,
    string? DisabledReason)
{
    public IReadOnlyList<AnimationPaneEffectOptionDescriptor> WheelSpokeOptions { get; init; } =
        Array.Empty<AnimationPaneEffectOptionDescriptor>();
}

public sealed record AnimationPaneEffectOptionMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    AnimationDirection? Direction,
    string DisplayText,
    string? DisabledReason,
    int? WheelSpokeCount = null,
    string? EffectSubtype = null,
    AnimationScaleBehavior? ScaleBehavior = null,
    string? PreservedNumericBehaviorXml = null,
    string? PreservedColorBehaviorXml = null,
    string? NativeColorToken = null,
    string? PreservedFontStyleBehaviorXml = null,
    string? NativeFontStyleProperty = null,
    bool? NativeFontStyleValue = null)
{
    public bool ReversesMotionPath { get; init; }
}

public sealed record AnimationPaneRepeatMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    int? RepeatCount,
    bool RepeatIndefinitely,
    bool AutoReverse,
    string DisplayText,
    string? DisabledReason);

public sealed record AnimationPaneTimingMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    AnimationPaneTimingEditKind Kind,
    AnimationTrigger Trigger,
    int DurationMs,
    int DelayMs,
    string DisplayText,
    string? DisabledReason);

public sealed record AnimationPaneEasingMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    int? Acceleration,
    int? Deceleration,
    string AccelerationText,
    string DecelerationText,
    string? DisabledReason);

public sealed record AnimationPaneTimelinePlan(
    IReadOnlyList<AnimationPaneTimelineItemPlan> Items,
    int SelectedIndex,
    AnimationPanePlaybackIntent PreviewIntent,
    IReadOnlyList<AnimationPanePlaybackControlDescriptor> PlaybackControls)
{
    public bool HasAnimations => Items.Count > 0;
    public AnimationPaneTimelineItemPlan? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}

public sealed record AnimationPaneParagraphBuildMutationPlan(
    bool ShouldApply,
    uint ShapeId,
    bool EnableParagraphBuild,
    string? UpdatedBuildListXml,
    string DisplayText,
    string? DisabledReason);

public sealed record AnimationPaneTimelineItemPlan(
    int Index,
    string OrderText,
    uint ShapeId,
    string ShapeName,
    string EffectText,
    AnimationKind Kind,
    AnimationPreset Preset,
    AnimationTrigger Trigger,
    int TriggerIndex,
    string TriggerText,
    int DelayMs,
    string DelayText,
    int DurationMs,
    string DurationText,
    int StartMs,
    string StartText,
    int EndMs,
    bool CanMoveEarlier,
    bool CanMoveLater,
    bool IsSelected,
    AnimationPaneEffectOptionsPlan EffectOptions)
{
    public int? RepeatCount { get; init; }
    public bool RepeatIndefinitely { get; init; }
    public bool AutoReverse { get; init; }
    public int? Acceleration { get; init; }
    public int? Deceleration { get; init; }
    public uint? TriggerShapeId { get; init; }
}

public sealed record AnimationPaneChoiceControlPlan(
    AnimationPaneControlDescriptor Descriptor,
    IReadOnlyList<AnimationPaneControlOptionPlan> Options,
    int SelectedIndex,
    bool IsVisible,
    bool IsEnabled,
    string ToolTip)
{
    public string? ResolveOptionId(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Options.Count
            ? Options[selectedIndex].Id
            : null;
}

public sealed record AnimationPaneTextControlPlan(
    AnimationPaneControlDescriptor Descriptor,
    string Text);

public sealed record AnimationPaneToggleControlPlan(
    AnimationPaneControlDescriptor Descriptor,
    bool IsChecked);

public sealed record AnimationPaneActionControlPlan(
    AnimationPaneControlDescriptor Descriptor,
    bool IsVisible,
    bool IsEnabled,
    string ToolTip);

public sealed record AnimationPaneItemControlPlan(
    AnimationPaneChoiceControlPlan EffectOptions,
    AnimationPaneChoiceControlPlan WheelSpokes,
    AnimationPaneChoiceControlPlan Trigger,
    AnimationPaneTextControlPlan Duration,
    AnimationPaneTextControlPlan Delay,
    AnimationPaneChoiceControlPlan Repeat,
    AnimationPaneToggleControlPlan AutoReverse,
    AnimationPaneTextControlPlan SmoothStart,
    AnimationPaneTextControlPlan SmoothEnd,
    AnimationPaneActionControlPlan MoveEarlier,
    AnimationPaneActionControlPlan MoveLater,
    AnimationPaneActionControlPlan Remove,
    AnimationPaneParagraphBuildMutationPlan ParagraphBuildMutation,
    AnimationPaneActionControlPlan ParagraphBuild,
    AnimationPaneActionControlPlan EditMotionPath);

public sealed record AnimationPaneWorkflowViewPlan(
    string Heading,
    string Message,
    string EmptyMessage,
    IReadOnlyList<string> RowSummaries,
    IReadOnlyList<string> PlaybackControlSummaries);

public sealed record AnimationPaneWorkflowEvidencePlan(
    AnimationPaneWorkflowViewPlan View,
    int RowCount,
    int EditableTimingRowCount,
    int EffectOptionRowCount,
    int ReorderableRowCount,
    bool HasSelectedRow,
    bool CanPreview,
    bool CanPlayFromSelected,
    IReadOnlyList<string> EvidenceLines);

public enum AnimationPanePlaybackIntentKind
{
    None,
    PreviewCurrentSlide,
}

public enum AnimationPanePlaybackControlKind
{
    PreviewCurrentSlide,
    PlayFromSelected,
    PlayCurrentSlide,
    Stop,
}

public enum AnimationPanePlaybackSessionState
{
    Idle,
    Running,
    Stopped,
}

public sealed record AnimationPanePlaybackIntent(
    AnimationPanePlaybackIntentKind Kind,
    bool CanExecute,
    int? SelectedAnimationIndex,
    int TotalDurationMs,
    string Description);

public sealed record AnimationPanePlaybackControlDescriptor(
    string CommandId,
    AnimationPanePlaybackControlKind Kind,
    string Label,
    bool IsEnabled,
    int? StartAnimationIndex,
    int TotalDurationMs,
    string ToolTip,
    string? DisabledReason);

public sealed record AnimationPanePlaybackSegmentPlan(
    int AnimationIndex,
    uint ShapeId,
    string ShapeName,
    string EffectText,
    AnimationTrigger Trigger,
    int AbsoluteStartMs,
    int RelativeStartMs,
    int DurationMs,
    int AbsoluteEndMs,
    int RelativeEndMs);

public sealed record AnimationPanePlaybackSessionPlan(
    AnimationPanePlaybackSessionState State,
    AnimationPanePlaybackControlKind CommandKind,
    int? StartAnimationIndex,
    int ElapsedMs,
    int TotalDurationMs,
    int RemainingDurationMs,
    IReadOnlyList<AnimationPanePlaybackSegmentPlan> Segments,
    IReadOnlyList<AnimationPanePlaybackControlDescriptor> PlaybackControls,
    string StatusText)
{
    public bool IsRunning => State == AnimationPanePlaybackSessionState.Running;
}

public enum AnimationPanePlaybackWorkflowHost
{
    Wpf,
    Avalonia
}

public sealed record AnimationPanePlaybackWorkflowHostEvidenceRow(
    AnimationPanePlaybackWorkflowHost Host,
    string EvidenceId,
    int SlideIndex,
    AnimationPanePlaybackControlKind CommandKind,
    AnimationPanePlaybackSessionState SessionState,
    int SegmentCount,
    int PlaybackCheckpointCount,
    bool RequiresPowerPointCom,
    string EvidenceSummary);

public sealed record AnimationPanePlaybackWorkflowEvidencePlan(
    string ScenarioId,
    int SlideIndex,
    AnimationPanePlaybackControlKind CommandKind,
    AnimationPanePlaybackSessionState SessionState,
    int? StartAnimationIndex,
    int SegmentCount,
    int PlaybackCheckpointCount,
    IReadOnlyList<SlideShowAnimationVisualTrackKind> TrackKinds,
    IReadOnlyList<SlideShowAnimationClipKind> ClipKinds,
    IReadOnlyList<AnimationPanePlaybackWorkflowHostEvidenceRow> HostRows,
    IReadOnlyList<string> EvidenceLines)
{
    public bool HasSharedNoComHostEvidence =>
        HostRows.Any(row => row.Host == AnimationPanePlaybackWorkflowHost.Wpf) &&
        HostRows.Any(row => row.Host == AnimationPanePlaybackWorkflowHost.Avalonia) &&
        HostRows.All(row => !row.RequiresPowerPointCom);
}

public sealed record AnimationPaneReorderIntent(
    bool CanMove,
    int FromIndex,
    int ToIndex);

public sealed record AnimationPaneReorderMutationPlan(
    bool ShouldApply,
    int FromIndex,
    int ToIndex,
    int SelectedAnimationIndex,
    string DisplayText,
    string? DisabledReason);

public sealed record AnimationPaneRemoveMutationPlan(
    bool ShouldApply,
    int AnimationIndex,
    int SelectedAnimationIndex,
    string DisplayText,
    string? DisabledReason);

public static class AnimationPanePlanner
{
    public static string MissingAnimationMessage => PresentationPaneTextResources.AnimationMissing;
    public static string InvalidTriggerMessage => PresentationPaneTextResources.AnimationInvalidTrigger;
    public static string InvalidDurationMessage => PresentationPaneTextResources.AnimationInvalidDuration;
    public static string InvalidDelayMessage => PresentationPaneTextResources.AnimationInvalidDelay;
    public static string InvalidRepeatMessage => PresentationPaneTextResources.AnimationInvalidRepeat;
    public static string InvalidEasingMessage => PresentationPaneTextResources.AnimationInvalidEasing;
    public static string MissingEffectOptionMessage => PresentationPaneTextResources.AnimationMissingEffectOption;
    public static string UnsupportedEffectOptionMessage => PresentationPaneTextResources.AnimationUnsupportedEffectOption;
    public static string InvalidEffectOptionMessage => PresentationPaneTextResources.AnimationInvalidEffectOption;
    public static string ParagraphBuildLabel => PresentationPaneTextResources.AnimationParagraphBuild;
    public static string ParagraphBuildDisabledMessage => PresentationPaneTextResources.AnimationParagraphBuildDisabled;
    public static string ParagraphBuildInvalidXmlMessage => PresentationPaneTextResources.AnimationParagraphBuildInvalidXml;
    public static string InvalidReorderMessage => PresentationPaneTextResources.AnimationInvalidReorder;
    public static string InvalidRemoveMessage => PresentationPaneTextResources.AnimationInvalidRemove;

    public static IReadOnlyList<string> TriggerLabels =>
        PresentationPaneTextResources.AnimationTriggerOptions
            .Select(option => option.Label)
            .ToArray();

    public static AnimationPaneControlSchemaPlan BuildControlSchema() =>
        PresentationPaneTextResources.BuildAnimationPaneControlSchema();

    public static AnimationPaneItemControlPlan BuildItemControlPlan(
        AnimationPaneTimelineItemPlan item,
        Slide? slide,
        bool canEditMotionPath,
        AnimationPaneControlSchemaPlan? schema = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        schema ??= BuildControlSchema();
        var effectOptions = schema.GetRequired(AnimationPaneControlKind.EffectOptions);
        var wheelSpokes = schema.GetRequired(AnimationPaneControlKind.WheelSpokes);
        var trigger = schema.GetRequired(AnimationPaneControlKind.Trigger);
        var duration = schema.GetRequired(AnimationPaneControlKind.Duration);
        var delay = schema.GetRequired(AnimationPaneControlKind.Delay);
        var repeat = schema.GetRequired(AnimationPaneControlKind.Repeat);
        var autoReverse = schema.GetRequired(AnimationPaneControlKind.AutoReverse);
        var smoothStart = schema.GetRequired(AnimationPaneControlKind.SmoothStart);
        var smoothEnd = schema.GetRequired(AnimationPaneControlKind.SmoothEnd);
        var moveEarlier = schema.GetRequired(AnimationPaneControlKind.MoveEarlier);
        var moveLater = schema.GetRequired(AnimationPaneControlKind.MoveLater);
        var remove = schema.GetRequired(AnimationPaneControlKind.RemoveAnimation);
        var paragraphBuild = schema.GetRequired(AnimationPaneControlKind.ParagraphBuild);
        var editMotionPath = schema.GetRequired(AnimationPaneControlKind.EditMotionPath);

        var effectOptionPlans = item.EffectOptions.Options
            .Select(option => new AnimationPaneControlOptionPlan(option.Id, option.DisplayText))
            .ToArray();
        var wheelSpokePlans = item.EffectOptions.WheelSpokeOptions
            .Select(option => new AnimationPaneControlOptionPlan(option.Id, option.DisplayText))
            .ToArray();
        var paragraphBuildMutation = BuildParagraphBuildMutationPlan(slide, item.ShapeId);
        var repeatText = FormatRepeat(item.RepeatCount, item.RepeatIndefinitely);
        var showMotionPathEditor = item.Kind == AnimationKind.Motion && canEditMotionPath;

        return new AnimationPaneItemControlPlan(
            BuildChoiceControlPlan(
                effectOptions,
                effectOptionPlans,
                FindSelectedOptionIndex(item.EffectOptions.Options),
                effectOptionPlans.Length > 0,
                item.EffectOptions.CanApply,
                item.EffectOptions.CanApply
                    ? effectOptions.ToolTip
                    : item.EffectOptions.DisabledReason ?? effectOptions.ToolTip),
            BuildChoiceControlPlan(
                wheelSpokes,
                wheelSpokePlans,
                FindSelectedOptionIndex(item.EffectOptions.WheelSpokeOptions),
                wheelSpokePlans.Length > 0,
                item.EffectOptions.CanApply,
                wheelSpokes.ToolTip),
            BuildChoiceControlPlan(
                trigger,
                trigger.Options,
                item.TriggerIndex,
                isVisible: true,
                isEnabled: true,
                toolTip: trigger.ToolTip),
            new AnimationPaneTextControlPlan(duration, item.DurationText),
            new AnimationPaneTextControlPlan(delay, item.DelayText),
            BuildChoiceControlPlan(
                repeat,
                repeat.Options,
                FindOptionIndex(repeat.Options, repeatText),
                isVisible: true,
                isEnabled: true,
                toolTip: repeat.ToolTip),
            new AnimationPaneToggleControlPlan(autoReverse, item.AutoReverse),
            new AnimationPaneTextControlPlan(smoothStart, FormatEasing(item.Acceleration)),
            new AnimationPaneTextControlPlan(smoothEnd, FormatEasing(item.Deceleration)),
            new AnimationPaneActionControlPlan(
                moveEarlier,
                IsVisible: true,
                IsEnabled: item.CanMoveEarlier,
                ToolTip: moveEarlier.ToolTip),
            new AnimationPaneActionControlPlan(
                moveLater,
                IsVisible: true,
                IsEnabled: item.CanMoveLater,
                ToolTip: moveLater.ToolTip),
            new AnimationPaneActionControlPlan(
                remove,
                IsVisible: true,
                IsEnabled: true,
                ToolTip: remove.ToolTip),
            paragraphBuildMutation,
            new AnimationPaneActionControlPlan(
                paragraphBuild,
                IsVisible: true,
                IsEnabled: paragraphBuildMutation.ShouldApply,
                ToolTip: paragraphBuildMutation.DisabledReason ?? paragraphBuildMutation.DisplayText),
            new AnimationPaneActionControlPlan(
                editMotionPath,
                IsVisible: showMotionPathEditor,
                IsEnabled: showMotionPathEditor,
                ToolTip: editMotionPath.ToolTip));
    }

    public static AnimationPaneParagraphBuildMutationPlan BuildParagraphBuildMutationPlan(
        Slide? slide,
        uint shapeId)
    {
        if (slide is null || shapeId == 0)
        {
            return new(
                false,
                shapeId,
                false,
                slide?.AnimationBuildListXml,
                ParagraphBuildLabel,
                ParagraphBuildDisabledMessage);
        }

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape?.TextBody is null || shape.TextBody.Paragraphs.Count == 0)
        {
            return new(
                false,
                shapeId,
                false,
                slide.AnimationBuildListXml,
                ParagraphBuildLabel,
                ParagraphBuildDisabledMessage);
        }

        var enable = !SlideShowAnimationBuildPlanner.IsParagraphBuild(slide, shapeId);
        if (!SlideShowAnimationBuildPlanner.TrySetParagraphBuild(
                slide,
                shapeId,
                enable,
                out var updatedXml))
        {
            return new(
                false,
                shapeId,
                enable,
                slide.AnimationBuildListXml,
                ParagraphBuildLabel,
                ParagraphBuildInvalidXmlMessage);
        }

        return new(
            true,
            shapeId,
            enable,
            updatedXml,
            enable ? PresentationPaneTextResources.AnimationParagraphBuildAllAtOnce : ParagraphBuildLabel,
            null);
    }

    public static bool TryApplyParagraphBuildMutation(
        EditingSession editor,
        AnimationPaneParagraphBuildMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!plan.ShouldApply)
            return false;

        editor.SetCurrentSlideAnimationBuildList(plan.UpdatedBuildListXml);
        return true;
    }

    public static AnimationPaneTimelinePlan BuildTimelinePlan(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds = null,
        int selectedAnimationIndex = -1,
        CultureInfo? displayCulture = null,
        bool isPlaybackRunning = false)
    {
        displayCulture ??= CultureInfo.CurrentCulture;
        var animations = slide?.Animations;
        if (animations is null || animations.Count == 0)
        {
            var controls = BuildPlaybackControls(-1, 0, 0, isPlaybackRunning);
            return new AnimationPaneTimelinePlan(
                Array.Empty<AnimationPaneTimelineItemPlan>(),
                -1,
                new AnimationPanePlaybackIntent(
                    AnimationPanePlaybackIntentKind.None,
                    false,
                    null,
                    0,
                    PresentationPaneTextResources.AnimationNoAnimationsToPreview),
                controls);
        }

        var selectedIndex = NormalizeSelectedIndex(animations, selectedShapeIds, selectedAnimationIndex);
        var items = new List<AnimationPaneTimelineItemPlan>(animations.Count);
        var sequenceAnchorMs = 0;
        var previousEndMs = 0;
        var totalDurationMs = 0;

        for (int i = 0; i < animations.Count; i++)
        {
            var animation = animations[i];
            if (animation.Trigger == AnimationTrigger.OnClick)
            {
                sequenceAnchorMs = previousEndMs;
            }

            var startMs = animation.Trigger switch
            {
                AnimationTrigger.AfterPrevious => previousEndMs + Math.Max(0, animation.DelayMs),
                _ => sequenceAnchorMs + Math.Max(0, animation.DelayMs),
            };
            var durationMs = Math.Max(0, animation.DurationMs);
            var endMs = startMs + durationMs;
            previousEndMs = endMs;
            totalDurationMs = Math.Max(totalDurationMs, endMs);

            var triggerIndex = ToTriggerIndex(animation.Trigger);
            var effectOptions = BuildEffectOptionsPlan(animations, i);
            items.Add(new AnimationPaneTimelineItemPlan(
                i,
                (i + 1).ToString(CultureInfo.InvariantCulture),
                animation.ShapeId,
                ResolveShapeName(slide!, animation.ShapeId),
                FormatEffect(animation),
                animation.Kind,
                animation.Preset,
                animation.Trigger,
                triggerIndex,
                TriggerLabels[triggerIndex],
                animation.DelayMs,
                FormatDuration(Math.Max(0, animation.DelayMs), displayCulture),
                animation.DurationMs,
                FormatDuration(Math.Max(0, animation.DurationMs), displayCulture),
                startMs,
                FormatDuration(startMs, displayCulture),
                endMs,
                i > 0,
                i < animations.Count - 1,
                i == selectedIndex,
                effectOptions)
            {
                RepeatCount = animation.RepeatCount,
                RepeatIndefinitely = animation.RepeatIndefinitely,
                AutoReverse = animation.AutoReverse,
                Acceleration = animation.Acceleration,
                Deceleration = animation.Deceleration,
                TriggerShapeId = animation.TriggerShapeId,
            });
        }

        var playbackControls = BuildPlaybackControls(
            selectedIndex,
            animations.Count,
            totalDurationMs,
            isPlaybackRunning);
        var previewControl = playbackControls.First(control =>
            control.Kind == AnimationPanePlaybackControlKind.PreviewCurrentSlide);
        return new AnimationPaneTimelinePlan(
            items,
            selectedIndex,
            new AnimationPanePlaybackIntent(
                AnimationPanePlaybackIntentKind.PreviewCurrentSlide,
                previewControl.IsEnabled,
                selectedIndex >= 0 ? selectedIndex : null,
                totalDurationMs,
                previewControl.ToolTip),
            playbackControls);
    }

    public static IReadOnlyList<AnimationPanePlaybackControlDescriptor> BuildPlaybackControls(
        int selectedAnimationIndex,
        int animationCount,
        int totalDurationMs,
        bool isPlaybackRunning = false)
    {
        var hasAnimations = animationCount > 0;
        var hasSelectedAnimation = selectedAnimationIndex >= 0 && selectedAnimationIndex < animationCount;
        var safeDurationMs = Math.Max(0, totalDurationMs);
        var canStartPlayback = hasAnimations && !isPlaybackRunning;
        var runningDisabledReason = PresentationPaneTextResources.AnimationPreviewAlreadyRunning;
        var noAnimationsToPreview = PresentationPaneTextResources.AnimationNoAnimationsToPreview;
        var noAnimationsToPlay = PresentationPaneTextResources.AnimationNoAnimationsToPlay;
        var selectRowToPlay = PresentationPaneTextResources.AnimationSelectRowToPlay;

        return
        [
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.preview",
                AnimationPanePlaybackControlKind.PreviewCurrentSlide,
                PresentationPaneTextResources.AnimationPreview,
                canStartPlayback,
                null,
                safeDurationMs,
                canStartPlayback
                    ? PresentationPaneTextResources.AnimationPreviewCurrentSlideToolTip
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : noAnimationsToPreview,
                canStartPlayback
                    ? null
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : noAnimationsToPreview),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.play-selected",
                AnimationPanePlaybackControlKind.PlayFromSelected,
                PresentationPaneTextResources.AnimationPlayFromSelected,
                hasSelectedAnimation && !isPlaybackRunning,
                hasSelectedAnimation ? selectedAnimationIndex : null,
                safeDurationMs,
                hasSelectedAnimation && !isPlaybackRunning
                    ? PresentationPaneTextResources.AnimationPlayFromSelectedToolTip
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : selectRowToPlay,
                hasSelectedAnimation && !isPlaybackRunning
                    ? null
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : selectRowToPlay),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.play-slide",
                AnimationPanePlaybackControlKind.PlayCurrentSlide,
                PresentationPaneTextResources.AnimationPlayAll,
                canStartPlayback,
                null,
                safeDurationMs,
                canStartPlayback
                    ? PresentationPaneTextResources.AnimationPlayAllToolTip
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : noAnimationsToPlay,
                canStartPlayback
                    ? null
                    : isPlaybackRunning
                        ? runningDisabledReason
                        : noAnimationsToPlay),
            new AnimationPanePlaybackControlDescriptor(
                "freep.anim.pane.stop",
                AnimationPanePlaybackControlKind.Stop,
                PresentationPaneTextResources.AnimationStop,
                isPlaybackRunning,
                null,
                safeDurationMs,
                isPlaybackRunning
                    ? PresentationPaneTextResources.AnimationStopToolTip
                    : PresentationPaneTextResources.AnimationNoPreviewRunning,
                isPlaybackRunning ? null : PresentationPaneTextResources.AnimationNoPreviewRunning),
        ];
    }

    public static AnimationPanePlaybackSessionPlan BuildPlaybackSessionPlan(
        AnimationPaneTimelinePlan timelinePlan,
        AnimationPanePlaybackControlKind commandKind,
        int elapsedMs = 0)
    {
        ArgumentNullException.ThrowIfNull(timelinePlan);

        if (commandKind == AnimationPanePlaybackControlKind.Stop)
        {
            return BuildStoppedPlaybackSessionPlan(timelinePlan);
        }

        if (!timelinePlan.HasAnimations)
        {
            return BuildIdlePlaybackSessionPlan(
                timelinePlan,
                commandKind,
                PresentationPaneTextResources.AnimationNoAnimationsToPreview);
        }

        var startIndex = commandKind switch
        {
            AnimationPanePlaybackControlKind.PreviewCurrentSlide => 0,
            AnimationPanePlaybackControlKind.PlayCurrentSlide => 0,
            AnimationPanePlaybackControlKind.PlayFromSelected => timelinePlan.SelectedIndex,
            _ => -1
        };

        if (startIndex < 0 || startIndex >= timelinePlan.Items.Count)
        {
            return BuildIdlePlaybackSessionPlan(
                timelinePlan,
                commandKind,
                PresentationPaneTextResources.AnimationSelectRowToPlay);
        }

        var startItem = timelinePlan.Items[startIndex];
        var isTriggerPlayback = commandKind == AnimationPanePlaybackControlKind.PlayFromSelected
            && startItem.TriggerShapeId is not null;
        var playbackItems = isTriggerPlayback
            ? timelinePlan.Items
                .Where(item => item.TriggerShapeId == startItem.TriggerShapeId && item.Index >= startIndex)
                .ToArray()
            : timelinePlan.Items.Skip(startIndex).ToArray();
        var anchorStartMs = startItem.StartMs;
        var segments = isTriggerPlayback
            ? BuildTriggerPlaybackSegments(playbackItems, anchorStartMs)
            : playbackItems
                .Select(item =>
                {
                    var relativeStartMs = Math.Max(0, item.StartMs - anchorStartMs);
                    var relativeEndMs = Math.Max(relativeStartMs, item.EndMs - anchorStartMs);
                    return new AnimationPanePlaybackSegmentPlan(
                        item.Index,
                        item.ShapeId,
                        item.ShapeName,
                        item.EffectText,
                        item.Trigger,
                        item.StartMs,
                        relativeStartMs,
                        Math.Max(0, item.DurationMs),
                        item.EndMs,
                        relativeEndMs);
                })
                .ToArray();
        var totalDurationMs = segments.Length == 0 ? 0 : segments.Max(segment => segment.RelativeEndMs);
        var safeElapsedMs = Math.Clamp(elapsedMs, 0, totalDurationMs);
        var sourceTotalDurationMs = timelinePlan.Items.Count == 0 ? 0 : timelinePlan.Items.Max(item => item.EndMs);
        var runningControls = BuildPlaybackControls(
            timelinePlan.SelectedIndex,
            timelinePlan.Items.Count,
            sourceTotalDurationMs,
            isPlaybackRunning: true);

        return new AnimationPanePlaybackSessionPlan(
            AnimationPanePlaybackSessionState.Running,
            commandKind,
            startIndex,
            safeElapsedMs,
            totalDurationMs,
            Math.Max(0, totalDurationMs - safeElapsedMs),
            segments,
            runningControls,
            commandKind == AnimationPanePlaybackControlKind.PlayFromSelected
                ? PresentationPaneTextResources.BuildAnimationPlayingFromMessage(startIndex + 1)
                : PresentationPaneTextResources.AnimationPlayingAll);
    }

    private static AnimationPanePlaybackSegmentPlan[] BuildTriggerPlaybackSegments(
        IReadOnlyList<AnimationPaneTimelineItemPlan> items,
        int anchorStartMs)
    {
        var segments = new AnimationPanePlaybackSegmentPlan[items.Count];
        var sequenceAnchorMs = 0;
        var previousEndMs = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Trigger == AnimationTrigger.OnClick)
                sequenceAnchorMs = previousEndMs;

            var relativeStartMs = item.Trigger switch
            {
                AnimationTrigger.AfterPrevious => previousEndMs + Math.Max(0, item.DelayMs),
                _ => sequenceAnchorMs + Math.Max(0, item.DelayMs),
            };
            var durationMs = Math.Max(0, item.DurationMs);
            var relativeEndMs = Math.Max(relativeStartMs, relativeStartMs + durationMs);
            previousEndMs = relativeEndMs;

            segments[index] = new AnimationPanePlaybackSegmentPlan(
                item.Index,
                item.ShapeId,
                item.ShapeName,
                item.EffectText,
                item.Trigger,
                anchorStartMs + relativeStartMs,
                relativeStartMs,
                durationMs,
                anchorStartMs + relativeEndMs,
                relativeEndMs);
        }

        return segments;
    }

    private static AnimationPanePlaybackSessionPlan BuildIdlePlaybackSessionPlan(
        AnimationPaneTimelinePlan timelinePlan,
        AnimationPanePlaybackControlKind commandKind,
        string statusText)
    {
        var sourceTotalDurationMs = timelinePlan.Items.Count == 0 ? 0 : timelinePlan.Items.Max(item => item.EndMs);
        return new AnimationPanePlaybackSessionPlan(
            AnimationPanePlaybackSessionState.Idle,
            commandKind,
            null,
            0,
            0,
            0,
            Array.Empty<AnimationPanePlaybackSegmentPlan>(),
            BuildPlaybackControls(timelinePlan.SelectedIndex, timelinePlan.Items.Count, sourceTotalDurationMs),
            statusText);
    }

    private static AnimationPanePlaybackSessionPlan BuildStoppedPlaybackSessionPlan(
        AnimationPaneTimelinePlan timelinePlan)
    {
        var sourceTotalDurationMs = timelinePlan.Items.Count == 0 ? 0 : timelinePlan.Items.Max(item => item.EndMs);
        return new AnimationPanePlaybackSessionPlan(
            AnimationPanePlaybackSessionState.Stopped,
            AnimationPanePlaybackControlKind.Stop,
            null,
            0,
            0,
            0,
            Array.Empty<AnimationPanePlaybackSegmentPlan>(),
            BuildPlaybackControls(timelinePlan.SelectedIndex, timelinePlan.Items.Count, sourceTotalDurationMs),
            PresentationPaneTextResources.AnimationPreviewStopped);
    }

    public static AnimationPaneReorderIntent BuildReorderIntent(
        int animationIndex,
        int animationCount,
        int offset)
    {
        var toIndex = animationIndex + offset;
        var canMove = animationIndex >= 0
            && animationIndex < animationCount
            && toIndex >= 0
            && toIndex < animationCount
            && offset != 0;
        return new AnimationPaneReorderIntent(canMove, animationIndex, toIndex);
    }

    public static AnimationPaneReorderMutationPlan BuildReorderMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        int offset)
    {
        var intent = BuildReorderIntent(animationIndex, animations.Count, offset);
        if (!intent.CanMove)
        {
            return new AnimationPaneReorderMutationPlan(
                false,
                intent.FromIndex,
                intent.ToIndex,
                NormalizeReorderSelection(animationIndex, animations.Count),
                "Cannot move animation",
                InvalidReorderMessage);
        }

        return new AnimationPaneReorderMutationPlan(
            true,
            intent.FromIndex,
            intent.ToIndex,
            intent.ToIndex,
            intent.ToIndex < intent.FromIndex
                ? $"Move animation {intent.FromIndex + 1} earlier"
                : $"Move animation {intent.FromIndex + 1} later",
            null);
    }

    public static string FormatEffect(ShapeAnimation animation)
    {
        var kindPrefix = animation.Kind switch
        {
            AnimationKind.Entrance => "In",
            AnimationKind.Exit => "Out",
            AnimationKind.Emphasis => "Em",
            AnimationKind.Motion => "Mv",
            _ => "?"
        };

        return animation.Kind == AnimationKind.Motion
            ? "Mv: Motion"
            : $"{kindPrefix}: {animation.Preset}";
    }

    public static int ToTriggerIndex(AnimationTrigger trigger)
    {
        return trigger switch
        {
            AnimationTrigger.OnClick => 0,
            AnimationTrigger.WithPrevious => 1,
            AnimationTrigger.AfterPrevious => 2,
            _ => 0
        };
    }

    public static bool TryGetTrigger(int selectedIndex, out AnimationTrigger trigger)
    {
        switch (selectedIndex)
        {
            case 0:
                trigger = AnimationTrigger.OnClick;
                return true;
            case 1:
                trigger = AnimationTrigger.WithPrevious;
                return true;
            case 2:
                trigger = AnimationTrigger.AfterPrevious;
                return true;
            default:
                trigger = AnimationTrigger.OnClick;
                return false;
        }
    }

    public static string FormatDuration(int ms, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double seconds = ms / 1000.0;
        return seconds.ToString("0.##", culture);
    }

    public static bool TryParseDuration(string text, out int ms)
        => TryParseTimingSeconds(text, allowZero: false, out ms);

    public static bool TryParseDelay(string text, out int ms)
        => TryParseTimingSeconds(text, allowZero: true, out ms);

    private static bool TryParseTimingSeconds(string text, bool allowZero, out int ms)
    {
        text = text.Trim();
        if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1].Trim();
        }

        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds)
            && (allowZero ? seconds >= 0 : seconds > 0))
        {
            ms = (int)(seconds * 1000.0);
            return true;
        }

        ms = 0;
        return false;
    }

    public static AnimationPaneDurationEditPlan BuildDurationEditPlan(
        string text,
        int currentDurationMs,
        CultureInfo? displayCulture = null)
    {
        if (TryParseDuration(text, out int parsedDurationMs)
            && parsedDurationMs != currentDurationMs)
        {
            return new(true, parsedDurationMs, FormatDuration(parsedDurationMs, displayCulture));
        }

        return new(false, currentDurationMs, FormatDuration(currentDurationMs, displayCulture));
    }

    public static AnimationPaneDurationEditPlan BuildDelayEditPlan(
        string text,
        int currentDelayMs,
        CultureInfo? displayCulture = null)
    {
        if (TryParseDelay(text, out int parsedDelayMs)
            && parsedDelayMs != currentDelayMs)
        {
            return new(true, parsedDelayMs, FormatDuration(parsedDelayMs, displayCulture));
        }

        return new(false, currentDelayMs, FormatDuration(currentDelayMs, displayCulture));
    }

    public static AnimationPaneTimingMutationPlan BuildTriggerMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        int selectedTriggerIndex)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Trigger,
                MissingAnimationMessage);
        }

        if (!TryGetTrigger(selectedTriggerIndex, out var trigger))
        {
            return new AnimationPaneTimingMutationPlan(
                false,
                animationIndex,
                AnimationPaneTimingEditKind.Trigger,
                animation.Trigger,
                animation.DurationMs,
                animation.DelayMs,
                TriggerLabels[ToTriggerIndex(animation.Trigger)],
                InvalidTriggerMessage);
        }

        return new AnimationPaneTimingMutationPlan(
            animation.Trigger != trigger,
            animationIndex,
            AnimationPaneTimingEditKind.Trigger,
            trigger,
            animation.DurationMs,
            animation.DelayMs,
            TriggerLabels[ToTriggerIndex(trigger)],
            null);
    }

    public static AnimationPaneTimingMutationPlan BuildDurationMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string text,
        CultureInfo? displayCulture = null)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Duration,
                MissingAnimationMessage,
                displayCulture);
        }

        var editPlan = BuildDurationEditPlan(text, animation.DurationMs, displayCulture);
        return new AnimationPaneTimingMutationPlan(
            editPlan.ShouldUpdate,
            animationIndex,
            AnimationPaneTimingEditKind.Duration,
            animation.Trigger,
            editPlan.DurationMs,
            animation.DelayMs,
            editPlan.DisplayText,
            TryParseDuration(text, out _) ? null : InvalidDurationMessage);
    }

    public static AnimationPaneTimingMutationPlan BuildDelayMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string text,
        CultureInfo? displayCulture = null)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledMutationPlan(
                animationIndex,
                AnimationPaneTimingEditKind.Delay,
                MissingAnimationMessage,
                displayCulture);
        }

        var editPlan = BuildDelayEditPlan(text, animation.DelayMs, displayCulture);
        return new AnimationPaneTimingMutationPlan(
            editPlan.ShouldUpdate,
            animationIndex,
            AnimationPaneTimingEditKind.Delay,
            animation.Trigger,
            animation.DurationMs,
            editPlan.DurationMs,
            editPlan.DisplayText,
            TryParseDelay(text, out _) ? null : InvalidDelayMessage);
    }

    public static AnimationPaneEffectOptionsPlan BuildEffectOptionsPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return DisabledEffectOptionsPlan(
                animationIndex,
                string.Empty,
                MissingEffectOptionMessage);
        }

        var descriptors = BuildSupportedEffectOptions(animation).ToArray();
        var nativeFontSize = SlideShowPlaybackPlanner.ResolveFontSizeBehavior(animation);
        var wheelSpokeOptions = animation.Preset == AnimationPreset.Wheel
            ? BuildWheelSpokeOptions(animation.WheelSpokeCount).ToArray()
            : Array.Empty<AnimationPaneEffectOptionDescriptor>();
        if (descriptors.Length == 0 && wheelSpokeOptions.Length == 0)
        {
            return DisabledEffectOptionsPlan(
                animationIndex,
                FormatEffect(animation),
                UnsupportedEffectOptionMessage);
        }

        if (descriptors.Length == 0)
        {
            var selectedWheelOption = wheelSpokeOptions.First(option => option.IsSelected);
            return new AnimationPaneEffectOptionsPlan(
                true,
                animationIndex,
                FormatEffect(animation),
                selectedWheelOption.DisplayText,
                Array.Empty<AnimationPaneEffectOptionDescriptor>(),
                null)
            {
                WheelSpokeOptions = wheelSpokeOptions
            };
        }

        var isAmountEffect = AnimationAmountSemantics.IsGrowShrink(animation.Preset);
        var isNativeColorEffect = descriptors.Any(option => option.NativeColorToken is not null);
        var isNativeFontStyleEffect = descriptors.Any(option => option.NativeFontStyleProperty is not null);
        var selectedDirection = animation.Preset == AnimationPreset.Split
            ? AnimationDirectionSemantics.ResolveSplitDirection(animation)
            : animation.Direction;
        var selected = (isNativeColorEffect || isNativeFontStyleEffect
                ? descriptors.FirstOrDefault(option => option.IsSelected)
                : isAmountEffect
                ? descriptors.FirstOrDefault(option => option.IsSelected)
                : descriptors.FirstOrDefault(option =>
                option.Direction == selectedDirection
                && option.EffectSubtype == animation.EffectSubtype)
                ?? descriptors.FirstOrDefault(option => option.EffectSubtype == animation.EffectSubtype))
            ?? descriptors[0];
        var normalized = descriptors
            .Select(option => option with
            {
                IsSelected = isNativeFontStyleEffect
                    ? option.IsSelected
                    : isNativeColorEffect
                    ? string.Equals(option.NativeColorToken, selected.NativeColorToken, StringComparison.OrdinalIgnoreCase)
                    : isAmountEffect
                    ? ScaleBehaviorEquals(option.ScaleBehavior, selected.ScaleBehavior)
                    : option.Direction == selected.Direction
                        && option.EffectSubtype == selected.EffectSubtype
            })
            .ToArray();

        return new AnimationPaneEffectOptionsPlan(
            true,
            animationIndex,
            FormatEffect(animation),
            isNativeFontStyleEffect || nativeFontSize is not null
                ? selected.DisplayText
                : isAmountEffect
                ? AnimationAmountSemantics.Describe(animation.Preset, animation.ScaleBehavior)
                : selected.DisplayText,
            normalized,
            null)
        {
            WheelSpokeOptions = wheelSpokeOptions
        };
    }

    public static AnimationPaneEffectOptionMutationPlan BuildEffectOptionMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string optionId)
    {
        var optionsPlan = BuildEffectOptionsPlan(animations, animationIndex);
        if (!optionsPlan.CanApply)
        {
            return new AnimationPaneEffectOptionMutationPlan(
                false,
                animationIndex,
                null,
                string.Empty,
                optionsPlan.DisabledReason,
                null);
        }

        var option = optionsPlan.Options
            .Concat(optionsPlan.WheelSpokeOptions)
            .FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, optionId));
        if (option is null)
        {
            var selected = optionsPlan.Options.FirstOrDefault(candidate => candidate.IsSelected)
                ?? optionsPlan.WheelSpokeOptions.First(candidate => candidate.IsSelected);
            return new AnimationPaneEffectOptionMutationPlan(
                false,
                animationIndex,
                selected.Direction,
                optionsPlan.SelectedOptionText,
                InvalidEffectOptionMessage,
                null);
        }

        var animation = animations[animationIndex];
        if (option.ReversesMotionPath)
        {
            return new AnimationPaneEffectOptionMutationPlan(
                animation.Motion is { Segments.Count: > 1 },
                animationIndex,
                null,
                option.DisplayText,
                animation.Motion is { Segments.Count: > 1 } ? null : MissingEffectOptionMessage,
                null)
            {
                ReversesMotionPath = true,
            };
        }

        var currentWheelSpokeCount = ResolveWheelSpokeCount(animation);
        var direction = option.Direction ?? animation.Direction;
        var effectSubtype = option.EffectSubtype ?? animation.EffectSubtype;
        var scaleBehavior = option.ScaleBehavior ?? animation.ScaleBehavior;
        var preservedNumericBehaviorXml = option.PreservedNumericBehaviorXml
            ?? animation.PreservedNumericBehaviorXml;
        var preservedColorBehaviorXml = option.PreservedColorBehaviorXml
            ?? ResolveNativeColorBehaviorXml(animation);
        var preservedFontStyleBehaviorXml = option.PreservedFontStyleBehaviorXml
            ?? animation.PreservedFontStyleBehaviorXml;
        if (option.EffectSubtype is not null)
            direction = null;
        var isAmountEffect = AnimationAmountSemantics.IsGrowShrink(animation.Preset);
        var nativeFontSize = SlideShowPlaybackPlanner.ResolveFontSizeBehavior(animation);
        var nextScale = isAmountEffect
            ? AnimationAmountSemantics.ResolveScale(AnimationPreset.Grow, scaleBehavior)
            : 1d;
        var amountChanged = nativeFontSize is not null
            ? Math.Abs(nativeFontSize.Multiplier - nextScale) >= 0.000001
            : !ScaleBehaviorEquals(animation.ScaleBehavior, scaleBehavior);
        var nativeColorChanged = option.NativeColorToken is not null
            && !string.Equals(
                option.NativeColorToken,
                SlideShowPlaybackPlanner.ResolveNativeColorToken(
                    ResolveNativeColorBehaviorXml(animation)),
                StringComparison.OrdinalIgnoreCase);
        var nativeFontStyleChanged = false;
        if (option.NativeFontStyleProperty is not null
            && option.NativeFontStyleValue is { } expectedFontStyleValue)
        {
            nativeFontStyleChanged = SlideShowPlaybackPlanner.ResolveFontStyleProperty(
                animation,
                option.NativeFontStyleProperty) is not { } currentFontStyleValue
                || currentFontStyleValue != expectedFontStyleValue;
        }
        return new AnimationPaneEffectOptionMutationPlan(
            (nativeColorChanged
                || nativeFontStyleChanged
                || (isAmountEffect
                ? amountChanged
                : animation.Direction != direction)
                || (animation.Preset == AnimationPreset.Wheel
                    && option.WheelSpokeCount is not null
                    && currentWheelSpokeCount != option.WheelSpokeCount)
                || (!isAmountEffect && animation.EffectSubtype != effectSubtype)),
            animationIndex,
            direction,
            option.DisplayText,
            null,
            option.WheelSpokeCount ?? animation.WheelSpokeCount,
            isAmountEffect ? null : effectSubtype,
            scaleBehavior,
            preservedNumericBehaviorXml,
            preservedColorBehaviorXml,
            option.NativeColorToken,
            preservedFontStyleBehaviorXml,
            option.NativeFontStyleProperty,
            option.NativeFontStyleValue);
    }

    public static bool TryApplyEffectOptionMutation(
        EditingSession editor,
        AnimationPaneEffectOptionMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        if (plan.ReversesMotionPath)
        {
            if (current.Kind != AnimationKind.Motion || current.Motion is not { Segments.Count: > 1 })
                return false;

            updated.Motion = MotionPath.ReversedClone(current.Motion);
            editor.SetAnimation(plan.AnimationIndex, updated);
            return true;
        }

        updated.Direction = plan.Direction;
        if (AnimationAmountSemantics.IsGrowShrink(current.Preset))
        {
            updated.ScaleBehavior = plan.ScaleBehavior?.Clone();
            if (SlideShowPlaybackPlanner.ResolveFontSizeBehavior(current) is not null)
                updated.PreservedNumericBehaviorXml = plan.PreservedNumericBehaviorXml;
        }
        else
            updated.EffectSubtype = plan.EffectSubtype;
        if (plan.NativeColorToken is not null)
        {
            var rewritten = SlideShowPlaybackPlanner.RewriteNativeColorBehavior(
                ResolveNativeColorBehaviorXml(current),
                plan.NativeColorToken);
            if (rewritten is null)
                return false;

            SetNativeColorBehaviorXml(updated, rewritten);
        }
        if (plan.NativeFontStyleProperty is not null
            && plan.NativeFontStyleValue is { } nativeFontStyleValue)
        {
            var rewritten = SlideShowPlaybackPlanner.RewriteFontStyleBehavior(
                current.PreservedFontStyleBehaviorXml,
                plan.NativeFontStyleProperty,
                nativeFontStyleValue);
            if (rewritten is null)
                return false;

            updated.PreservedFontStyleBehaviorXml = rewritten;
        }
        if (current.Preset == AnimationPreset.Wheel)
            updated.WheelSpokeCount = plan.WheelSpokeCount;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
    }

    public static AnimationPaneRepeatMutationPlan BuildRepeatMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string? repeatText,
        bool autoReverse)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return new AnimationPaneRepeatMutationPlan(
                false, animationIndex, null, false, autoReverse, string.Empty, MissingAnimationMessage);
        }

        if (!TryParseRepeat(repeatText, out var repeatCount, out var indefinite))
        {
            return new AnimationPaneRepeatMutationPlan(
                false,
                animationIndex,
                animation.RepeatCount,
                animation.RepeatIndefinitely,
                animation.AutoReverse,
                FormatRepeat(animation.RepeatCount, animation.RepeatIndefinitely),
                InvalidRepeatMessage);
        }

        var changed = repeatCount != animation.RepeatCount
            || indefinite != animation.RepeatIndefinitely
            || autoReverse != animation.AutoReverse;
        return new AnimationPaneRepeatMutationPlan(
            changed,
            animationIndex,
            repeatCount,
            indefinite,
            autoReverse,
            FormatRepeat(repeatCount, indefinite),
            null);
    }

    public static bool TryApplyRepeatMutation(
        EditingSession editor,
        AnimationPaneRepeatMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.RepeatCount = plan.RepeatCount;
        updated.RepeatIndefinitely = plan.RepeatIndefinitely;
        updated.AutoReverse = plan.AutoReverse;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
    }

    public static bool TryParseRepeat(
        string? text,
        out int? repeatCount,
        out bool indefinite)
    {
        var normalized = text?.Trim();
        indefinite = string.Equals(
                normalized,
                PresentationPaneTextResources.AnimationRepeatIndefinitely,
                StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(normalized, "indefinitely", StringComparison.OrdinalIgnoreCase);
        if (indefinite)
        {
            repeatCount = null;
            return true;
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count >= 1)
        {
            repeatCount = count == 1 ? null : count;
            return true;
        }

        repeatCount = null;
        return false;
    }

    public static string FormatRepeat(int? repeatCount, bool indefinite)
        => indefinite
            ? PresentationPaneTextResources.AnimationRepeatIndefinitely
            : (repeatCount ?? 1).ToString(CultureInfo.InvariantCulture);

    public static bool TryApplyReorderMutation(
        EditingSession editor,
        AnimationPaneReorderMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply)
        {
            return false;
        }

        var animations = editor.CurrentSlideAnimations;
        if (plan.FromIndex < 0
            || plan.FromIndex >= animations.Count
            || plan.ToIndex < 0
            || plan.ToIndex >= animations.Count
            || plan.FromIndex == plan.ToIndex)
        {
            return false;
        }

        editor.MoveAnimation(plan.FromIndex, plan.ToIndex);
        return true;
    }

    public static AnimationPaneRemoveMutationPlan BuildRemoveMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex)
    {
        bool canRemove = animationIndex >= 0 && animationIndex < animations.Count;
        if (!canRemove)
        {
            return new AnimationPaneRemoveMutationPlan(
                false,
                animationIndex,
                NormalizeReorderSelection(animationIndex, animations.Count),
                "Remove animation",
                InvalidRemoveMessage);
        }

        return new AnimationPaneRemoveMutationPlan(
            true,
            animationIndex,
            Math.Min(animationIndex, animations.Count - 2),
            $"Remove animation {animationIndex + 1}",
            null);
    }

    public static bool TryApplyRemoveMutation(
        EditingSession editor,
        AnimationPaneRemoveMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || plan.AnimationIndex < 0
            || plan.AnimationIndex >= editor.CurrentSlideAnimations.Count)
        {
            return false;
        }

        editor.RemoveAnimation(plan.AnimationIndex);
        return true;
    }

    public static bool TryApplyTimingMutation(
        EditingSession editor,
        AnimationPaneTimingMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.Trigger = plan.Trigger;
        updated.DurationMs = plan.DurationMs;
        updated.DelayMs = plan.DelayMs;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
    }

    public static AnimationPaneEasingMutationPlan BuildEasingMutationPlan(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        string? accelerationText,
        string? decelerationText)
    {
        if (!TryGetAnimation(animations, animationIndex, out var animation))
        {
            return new AnimationPaneEasingMutationPlan(
                false,
                animationIndex,
                null,
                null,
                FormatEasing(null),
                FormatEasing(null),
                MissingAnimationMessage);
        }

        if (!TryParseEasing(accelerationText, out var acceleration)
            || !TryParseEasing(decelerationText, out var deceleration))
        {
            return new AnimationPaneEasingMutationPlan(
                false,
                animationIndex,
                animation.Acceleration,
                animation.Deceleration,
                FormatEasing(animation.Acceleration),
                FormatEasing(animation.Deceleration),
                InvalidEasingMessage);
        }

        return new AnimationPaneEasingMutationPlan(
            acceleration != animation.Acceleration || deceleration != animation.Deceleration,
            animationIndex,
            acceleration,
            deceleration,
            FormatEasing(acceleration),
            FormatEasing(deceleration),
            null);
    }

    public static bool TryApplyEasingMutation(
        EditingSession editor,
        AnimationPaneEasingMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.ShouldApply
            || !TryGetAnimation(editor.CurrentSlideAnimations, plan.AnimationIndex, out var current))
        {
            return false;
        }

        var updated = PresentationAnimationCommandPlanner.CloneAnimation(current);
        updated.Acceleration = plan.Acceleration;
        updated.Deceleration = plan.Deceleration;
        editor.SetAnimation(plan.AnimationIndex, updated);
        return true;
    }

    public static string FormatEasing(int? value)
        => $"{Math.Clamp(value ?? 0, 0, 100000) / 1000d:0.###}%";

    public static bool TryParseEasing(string? text, out int? value)
    {
        var normalized = text?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            value = null;
            return true;
        }

        normalized = normalized.TrimEnd('%').Trim();
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            || percent is < 0 or > 100)
        {
            value = null;
            return false;
        }

        value = (int)Math.Round(percent * 1000, MidpointRounding.AwayFromZero);
        return true;
    }

    public static AnimationPaneWorkflowViewPlan BuildWorkflowViewPlan(
        AnimationPaneTimelinePlan timelinePlan,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(timelinePlan);

        var emptyMessage = PresentationPaneTextResources.AnimationPaneEmptyMessage;
        var safeSlideNumber = Math.Max(0, slideIndex) + 1;
        var heading = PresentationPaneTextResources.BuildAnimationPaneHeading(
            safeSlideNumber,
            timelinePlan.Items.Count);
        var message = timelinePlan.SelectedItem is { } selected
            ? PresentationPaneTextResources.BuildAnimationPaneSelectedMessage(
                selected.ShapeName,
                selected.EffectText)
            : timelinePlan.HasAnimations
                ? PresentationPaneTextResources.AnimationPaneSelectRowMessage
                : emptyMessage;
        var rowSummaries = timelinePlan.Items
            .Select(BuildWorkflowRowSummary)
            .ToArray();
        var controlSummaries = timelinePlan.PlaybackControls
            .Select(BuildPlaybackControlSummary)
            .ToArray();

        return new AnimationPaneWorkflowViewPlan(
            heading,
            message,
            emptyMessage,
            rowSummaries,
            controlSummaries);
    }

    public static AnimationPaneWorkflowEvidencePlan BuildWorkflowEvidencePlan(
        AnimationPaneTimelinePlan timelinePlan,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(timelinePlan);

        var viewPlan = BuildWorkflowViewPlan(timelinePlan, slideIndex);
        var editableTimingRowCount = timelinePlan.Items.Count;
        var effectOptionRowCount = timelinePlan.Items.Count(item => item.EffectOptions.Options.Count > 0);
        var reorderableRowCount = timelinePlan.Items.Count(item => item.CanMoveEarlier || item.CanMoveLater);
        var canPreview = timelinePlan.PlaybackControls.Any(control =>
            control.Kind == AnimationPanePlaybackControlKind.PreviewCurrentSlide && control.IsEnabled);
        var canPlayFromSelected = timelinePlan.PlaybackControls.Any(control =>
            control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected && control.IsEnabled);
        var evidenceLines = new List<string>
        {
            $"Rows: {timelinePlan.Items.Count}; selected: {FormatSelectedEvidence(timelinePlan.SelectedIndex)}; "
                + $"timing editors: {editableTimingRowCount}; effect-option rows: {effectOptionRowCount}; "
                + $"reorderable rows: {reorderableRowCount}",
            "Playback controls: " + string.Join("; ", viewPlan.PlaybackControlSummaries),
        };

        if (timelinePlan.SelectedItem is { } selected)
        {
            evidenceLines.Add(
                $"Selected row: {selected.ShapeName} - {selected.EffectText}; trigger {selected.TriggerText}; "
                    + $"duration {selected.DurationText}s; delay {selected.DelayText}s");
        }
        else
        {
            evidenceLines.Add(viewPlan.Message);
        }

        return new AnimationPaneWorkflowEvidencePlan(
            viewPlan,
            timelinePlan.Items.Count,
            editableTimingRowCount,
            effectOptionRowCount,
            reorderableRowCount,
            timelinePlan.SelectedIndex >= 0,
            canPreview,
            canPlayFromSelected,
            evidenceLines);
    }

    public static AnimationPanePlaybackWorkflowEvidencePlan BuildPlaybackWorkflowEvidencePlan(
        AnimationPaneTimelinePlan timelinePlan,
        AnimationPanePlaybackSessionPlan sessionPlan,
        IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> playbackCheckpoints,
        int slideIndex,
        string scenarioId = "animation-pane-playback")
    {
        ArgumentNullException.ThrowIfNull(timelinePlan);
        ArgumentNullException.ThrowIfNull(sessionPlan);
        ArgumentNullException.ThrowIfNull(playbackCheckpoints);

        var safeScenarioId = NormalizeScenarioId(scenarioId);
        var safeSlideIndex = Math.Max(0, slideIndex);
        var frames = playbackCheckpoints
            .SelectMany(checkpoint => checkpoint.Frames)
            .ToArray();
        var trackKinds = frames
            .Select(frame => frame.TrackKind)
            .Distinct()
            .OrderBy(kind => kind.ToString(), StringComparer.Ordinal)
            .ToArray();
        var clipKinds = frames
            .Select(frame => frame.ClipKind)
            .Where(kind => kind != SlideShowAnimationClipKind.None)
            .Distinct()
            .OrderBy(kind => kind.ToString(), StringComparer.Ordinal)
            .ToArray();
        var summary =
            $"slide {safeSlideIndex + 1}; command {sessionPlan.CommandKind}; state {sessionPlan.State}; "
            + $"segments {sessionPlan.Segments.Count}; checkpoints {playbackCheckpoints.Count}; "
            + $"tracks {FormatEnumList(trackKinds)}; clips {FormatEnumList(clipKinds)}";
        var evidenceIdBase =
            $"{safeScenarioId}-slide-{safeSlideIndex + 1}-{NormalizeScenarioId(sessionPlan.CommandKind.ToString())}";
        var hostRows = new[]
        {
            BuildPlaybackWorkflowHostRow(AnimationPanePlaybackWorkflowHost.Wpf, evidenceIdBase, safeSlideIndex, sessionPlan, playbackCheckpoints.Count, summary),
            BuildPlaybackWorkflowHostRow(AnimationPanePlaybackWorkflowHost.Avalonia, evidenceIdBase, safeSlideIndex, sessionPlan, playbackCheckpoints.Count, summary)
        };
        var evidenceLines = new[]
        {
            $"Scenario {safeScenarioId}: slide {safeSlideIndex + 1}; command {sessionPlan.CommandKind}; state {sessionPlan.State}; segments {sessionPlan.Segments.Count}; checkpoints {playbackCheckpoints.Count}",
            $"Pane playback tracks: {FormatEnumList(trackKinds)}; clips: {FormatEnumList(clipKinds)}; selected start: {FormatSelectedEvidence(sessionPlan.StartAnimationIndex ?? -1)}",
            "Shared host rows: WPF/Avalonia; PowerPoint COM required: false"
        };

        return new AnimationPanePlaybackWorkflowEvidencePlan(
            safeScenarioId,
            safeSlideIndex,
            sessionPlan.CommandKind,
            sessionPlan.State,
            sessionPlan.StartAnimationIndex,
            sessionPlan.Segments.Count,
            playbackCheckpoints.Count,
            trackKinds,
            clipKinds,
            hostRows,
            evidenceLines);
    }

    public static string BuildWorkflowRowSummary(AnimationPaneTimelineItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return $"{item.OrderText}. {item.ShapeName} - {item.EffectText}{FormatEffectOptions(item.EffectOptions)} - {item.TriggerText}; "
            + $"duration {item.DurationText}s; delay {item.DelayText}s; starts {item.StartText}s; "
            + $"move earlier {FormatAvailability(item.CanMoveEarlier)}; move later {FormatAvailability(item.CanMoveLater)}";
    }

    public static string BuildPlaybackControlSummary(AnimationPanePlaybackControlDescriptor control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return $"{control.Label}: {FormatAvailability(control.IsEnabled)}";
    }

    private static AnimationPaneChoiceControlPlan BuildChoiceControlPlan(
        AnimationPaneControlDescriptor descriptor,
        IReadOnlyList<AnimationPaneControlOptionPlan> options,
        int selectedIndex,
        bool isVisible,
        bool isEnabled,
        string toolTip) =>
        new(descriptor, options, selectedIndex, isVisible, isEnabled, toolTip);

    private static int FindSelectedOptionIndex(
        IReadOnlyList<AnimationPaneEffectOptionDescriptor> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].IsSelected)
                return index;
        }

        return -1;
    }

    private static int FindOptionIndex(
        IReadOnlyList<AnimationPaneControlOptionPlan> options,
        string label)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].Label, label, StringComparison.CurrentCulture))
                return index;
        }

        return -1;
    }

    private static int NormalizeSelectedIndex(
        IReadOnlyList<ShapeAnimation> animations,
        IReadOnlyList<uint>? selectedShapeIds,
        int selectedAnimationIndex)
    {
        if (selectedAnimationIndex >= 0 && selectedAnimationIndex < animations.Count)
        {
            return selectedAnimationIndex;
        }

        if (selectedShapeIds is null || selectedShapeIds.Count == 0)
        {
            return -1;
        }

        for (int i = animations.Count - 1; i >= 0; i--)
        {
            if (selectedShapeIds.Contains(animations[i].ShapeId))
            {
                return i;
            }
        }

        return -1;
    }

    private static int NormalizeReorderSelection(int animationIndex, int animationCount)
    {
        if (animationCount <= 0)
        {
            return -1;
        }

        return Math.Clamp(animationIndex, 0, animationCount - 1);
    }

    private static string ResolveShapeName(Slide slide, uint shapeId)
    {
        var shape = ShapeHitTester.FindShape(slide, shapeId);
        return string.IsNullOrWhiteSpace(shape?.Name)
            ? $"Shape {shapeId.ToString(CultureInfo.InvariantCulture)}"
            : shape!.Name;
    }

    private static bool TryGetAnimation(
        IReadOnlyList<ShapeAnimation> animations,
        int animationIndex,
        out ShapeAnimation animation)
    {
        if (animationIndex >= 0 && animationIndex < animations.Count)
        {
            animation = animations[animationIndex];
            return true;
        }

        animation = default!;
        return false;
    }

    private static AnimationPaneTimingMutationPlan DisabledMutationPlan(
        int animationIndex,
        AnimationPaneTimingEditKind kind,
        string disabledReason,
        CultureInfo? displayCulture = null)
        => new(
            false,
            animationIndex,
            kind,
            AnimationTrigger.OnClick,
            0,
            0,
            FormatDuration(0, displayCulture),
            disabledReason);

    private static AnimationPaneEffectOptionsPlan DisabledEffectOptionsPlan(
        int animationIndex,
        string effectText,
        string disabledReason)
        => new(
            false,
            animationIndex,
            effectText,
            string.Empty,
            Array.Empty<AnimationPaneEffectOptionDescriptor>(),
            disabledReason);

    private static string FormatEffectOptions(AnimationPaneEffectOptionsPlan plan)
        => plan.CanApply
            ? $" ({plan.SelectedOptionText})"
            : string.Empty;

    private static string FormatAvailability(bool isAvailable)
        => isAvailable ? "available" : "unavailable";

    private static string FormatSelectedEvidence(int selectedIndex)
        => selectedIndex >= 0
            ? (selectedIndex + 1).ToString(CultureInfo.InvariantCulture)
            : "none";

    private static string NormalizeScenarioId(string value)
    {
        var source = string.IsNullOrWhiteSpace(value)
            ? "animation-pane"
            : value.Trim().ToLowerInvariant();
        var normalized = new string(source
            .Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'
                ? character
                : '-')
            .ToArray())
            .Trim('-');

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? "animation-pane" : normalized;
    }

    private static AnimationPanePlaybackWorkflowHostEvidenceRow BuildPlaybackWorkflowHostRow(
        AnimationPanePlaybackWorkflowHost host,
        string evidenceIdBase,
        int slideIndex,
        AnimationPanePlaybackSessionPlan sessionPlan,
        int playbackCheckpointCount,
        string evidenceSummary)
    {
        var hostToken = host.ToString().ToLowerInvariant();
        return new AnimationPanePlaybackWorkflowHostEvidenceRow(
            host,
            $"{evidenceIdBase}-{hostToken}",
            slideIndex,
            sessionPlan.CommandKind,
            sessionPlan.State,
            sessionPlan.Segments.Count,
            playbackCheckpointCount,
            RequiresPowerPointCom: false,
            evidenceSummary);
    }

    private static string FormatEnumList<T>(IReadOnlyList<T> values)
        => values.Count == 0 ? "none" : string.Join(", ", values);

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> BuildSupportedEffectOptions(
        ShapeAnimation animation)
    {
        if (animation.Kind == AnimationKind.Motion)
        {
            if (animation.Motion is { Segments.Count: > 1 })
            {
                yield return new AnimationPaneEffectOptionDescriptor(
                    "reverse-path",
                    "Reverse Path",
                    null,
                    true)
                {
                    ReversesMotionPath = true,
                };
            }

            yield break;
        }

        if (SlideShowPlaybackPlanner.ResolveFontSizeBehavior(animation) is not null)
        {
            foreach (var option in FontSizeAmountOptions(animation))
                yield return option;
            yield break;
        }

        var nativeColorOptions = NativeColorOptions(animation).ToArray();
        if (nativeColorOptions.Length > 0)
        {
            foreach (var option in nativeColorOptions)
                yield return option;
            yield break;
        }

        var nativeFontStyleOptions = NativeFontStyleOptions(animation).ToArray();
        if (nativeFontStyleOptions.Length > 0)
        {
            foreach (var option in nativeFontStyleOptions)
                yield return option;
            yield break;
        }

        switch (animation.Preset)
        {
            case AnimationPreset.FlyIn:
                foreach (var option in FlyInDirectionOptions())
                    yield return option;
                break;

            case AnimationPreset.Wipe:
                foreach (var option in FromCardinalOptions())
                    yield return option;
                break;

            case AnimationPreset.Zoom:
                foreach (var option in InOutOptions())
                    yield return option;
                break;

            case AnimationPreset.Split:
                foreach (var option in SplitDirectionOptions())
                    yield return option;
                break;

            case AnimationPreset.RandomBars:
            case AnimationPreset.Blinds:
            case AnimationPreset.Checkerboard:
            case AnimationPreset.Wave:
                foreach (var option in HorizontalVerticalOptions())
                    yield return option;
                break;

            case AnimationPreset.Box:
            case AnimationPreset.Circle:
            case AnimationPreset.Diamond:
            case AnimationPreset.Plus:
            case AnimationPreset.Wedge:
            case AnimationPreset.Wheel:
                foreach (var option in InOutOptions())
                    yield return option;
                break;

            case AnimationPreset.Spin:
                foreach (var option in SpinAmountOptions())
                    yield return option;
                break;

            case AnimationPreset.Grow:
            case AnimationPreset.Shrink:
            case AnimationPreset.Pulse:
            case AnimationPreset.GrowWithColor:
                foreach (var option in GrowShrinkAmountOptions(animation))
                    yield return option;
                break;

            case AnimationPreset.Spiral:
            case AnimationPreset.Swivel:
                foreach (var option in InOutOptions())
                    yield return option;
                break;

            case AnimationPreset.Peek:
            case AnimationPreset.Crawl:
            case AnimationPreset.Bounce:
            case AnimationPreset.Float:
            case AnimationPreset.Swoop:
            case AnimationPreset.Boomerang:
                foreach (var option in FromCardinalOptions())
                    yield return option;
                break;

            case AnimationPreset.Strips:
                yield return EffectOption("left-up", "Left Up", AnimationDirection.LeftUp);
                yield return EffectOption("left-down", "Left Down", AnimationDirection.LeftDown);
                yield return EffectOption("right-up", "Right Up", AnimationDirection.RightUp);
                yield return EffectOption("right-down", "Right Down", AnimationDirection.RightDown);
                break;
        }
    }

    // PowerPoint's Fly effect supports all four slide edges and all four corners.
    private static IEnumerable<AnimationPaneEffectOptionDescriptor> FlyInDirectionOptions()
    {
        foreach (var option in FromCardinalOptions())
            yield return option;

        yield return EffectOption("from-top-left", "From Top Left", AnimationDirection.FromTopLeft);
        yield return EffectOption("from-top-right", "From Top Right", AnimationDirection.FromTopRight);
        yield return EffectOption("from-bottom-left", "From Bottom Left", AnimationDirection.FromBottomLeft);
        yield return EffectOption("from-bottom-right", "From Bottom Right", AnimationDirection.FromBottomRight);
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> FromCardinalOptions()
    {
        yield return EffectOption("from-bottom", "From Bottom", AnimationDirection.FromBottom);
        yield return EffectOption("from-left", "From Left", AnimationDirection.FromLeft);
        yield return EffectOption("from-right", "From Right", AnimationDirection.FromRight);
        yield return EffectOption("from-top", "From Top", AnimationDirection.FromTop);
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> HorizontalVerticalOptions()
    {
        yield return EffectOption("horizontal", "Horizontal", AnimationDirection.Horizontal);
        yield return EffectOption("vertical", "Vertical", AnimationDirection.Vertical);
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> SplitDirectionOptions()
    {
        yield return EffectOption("horizontal-in", "Horizontal In", AnimationDirection.HorizontalIn);
        yield return EffectOption("horizontal-out", "Horizontal Out", AnimationDirection.HorizontalOut);
        yield return EffectOption("vertical-in", "Vertical In", AnimationDirection.VerticalIn);
        yield return EffectOption("vertical-out", "Vertical Out", AnimationDirection.VerticalOut);
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> InOutOptions()
    {
        yield return EffectOption("in", "In", AnimationDirection.In);
        yield return EffectOption("out", "Out", AnimationDirection.Out);
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> SpinAmountOptions()
    {
        yield return EffectSubtypeOption("quarter-spin", "Quarter Spin", "quarterSpin");
        yield return EffectSubtypeOption("half-spin", "Half Spin", "halfSpin");
        yield return EffectSubtypeOption("full-spin", "Full Spin", "fullSpin");
        yield return EffectSubtypeOption("two-spins", "Two Spins", "twoSpins");
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> GrowShrinkAmountOptions(
        ShapeAnimation animation)
    {
        foreach (var choice in AnimationAmountSemantics.SupportedChoices)
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                $"amount-{choice.Token}",
                choice.DisplayText,
                null,
                AnimationAmountSemantics.IsSupportedScale(animation.ScaleBehavior, choice.Scale),
                ScaleBehavior: AnimationAmountSemantics.CreateChoiceBehavior(animation.Preset, choice.Scale));
        }

        // Keep a missing/default or nonstandard imported token visible instead of
        // silently selecting the first named amount in the pane.
        if (animation.ScaleBehavior is null)
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                "amount-default",
                AnimationAmountSemantics.Describe(animation.Preset, animation.ScaleBehavior),
                null,
                true,
                ScaleBehavior: AnimationScaleBehavior.FromTo(
                    animation.Preset == AnimationPreset.Shrink ? 0.8 : 1.2));
        }
        else if (!AnimationAmountSemantics.SupportedChoices.Any(choice =>
                     AnimationAmountSemantics.IsSupportedScale(animation.ScaleBehavior, choice.Scale)))
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                "amount-custom",
                AnimationAmountSemantics.Describe(animation.Preset, animation.ScaleBehavior),
                null,
                true,
                ScaleBehavior: animation.ScaleBehavior.Clone());
        }
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> FontSizeAmountOptions(
        ShapeAnimation animation)
    {
        var current = SlideShowPlaybackPlanner.ResolveFontSizeBehavior(animation);
        foreach (var choice in AnimationAmountSemantics.SupportedChoices)
        {
            var numericBehaviorXml = SlideShowPlaybackPlanner.RewriteFontSizeBehavior(
                animation,
                choice.Scale);
            if (numericBehaviorXml is null)
                continue;

            yield return new AnimationPaneEffectOptionDescriptor(
                $"amount-{choice.Token}",
                choice.DisplayText,
                null,
                current is not null && Math.Abs(current.Multiplier - choice.Scale) < 0.000001,
                ScaleBehavior: AnimationAmountSemantics.CreateChoiceBehavior(AnimationPreset.Grow, choice.Scale),
                PreservedNumericBehaviorXml: numericBehaviorXml);
        }

        if (current is null
            || AnimationAmountSemantics.SupportedChoices.Any(choice =>
                Math.Abs(choice.Scale - current.Multiplier) < 0.000001))
        {
            yield break;
        }

        var customBehaviorXml = SlideShowPlaybackPlanner.RewriteFontSizeBehavior(
            animation,
            current.Multiplier);
        if (customBehaviorXml is not null)
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                "amount-custom",
                $"Custom ({current.Multiplier * 100:0.##}%)",
                null,
                true,
                ScaleBehavior: AnimationScaleBehavior.FromTo(current.Multiplier),
                PreservedNumericBehaviorXml: customBehaviorXml);
        }
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> NativeColorOptions(
        ShapeAnimation animation)
    {
        var behaviorXml = ResolveNativeColorBehaviorXml(animation);
        var currentToken = SlideShowPlaybackPlanner.ResolveNativeColorToken(behaviorXml);
        if (currentToken is null)
            yield break;

        foreach (var token in new[] { "accent1", "accent2", "accent3", "accent4", "accent5", "accent6" })
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                $"color-{token}",
                token.Replace("accent", "Accent ", StringComparison.Ordinal),
                null,
                string.Equals(currentToken, token, StringComparison.OrdinalIgnoreCase),
                PreservedColorBehaviorXml: behaviorXml,
                NativeColorToken: token);
        }
    }

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> NativeFontStyleOptions(
        ShapeAnimation animation)
    {
        if (string.IsNullOrWhiteSpace(animation.PreservedFontStyleBehaviorXml))
            yield break;

        foreach (var property in new[]
        {
            (Name: "style.fontStyle", Label: "Italic"),
            (Name: "style.fontWeight", Label: "Bold"),
            (Name: "style.textDecorationUnderline", Label: "Underline"),
        })
        {
            var current = SlideShowPlaybackPlanner.ResolveFontStyleProperty(animation, property.Name);
            if (current is null)
                continue;

            foreach (var value in new[] { false, true })
            {
                yield return new AnimationPaneEffectOptionDescriptor(
                    $"font-style-{property.Label.ToLowerInvariant()}-{(value ? "on" : "off")}",
                    $"{property.Label}: {(value ? "On" : "Off")}",
                    null,
                    current == value,
                    PreservedFontStyleBehaviorXml: animation.PreservedFontStyleBehaviorXml,
                    NativeFontStyleProperty: property.Name,
                    NativeFontStyleValue: value);
            }
        }
    }

    private static string? ResolveNativeColorBehaviorXml(ShapeAnimation animation) =>
        animation.Preset switch
        {
            AnimationPreset.ChangeFillColor => animation.PreservedFillBehaviorXml,
            AnimationPreset.ChangeLineColor => animation.PreservedLineBehaviorXml,
            AnimationPreset.ChangeColor
                or AnimationPreset.ColorPulse
                or AnimationPreset.ColorWave => animation.PreservedColorBehaviorXml,
            _ => null,
        };

    private static void SetNativeColorBehaviorXml(ShapeAnimation animation, string value)
    {
        switch (animation.Preset)
        {
            case AnimationPreset.ChangeFillColor:
                animation.PreservedFillBehaviorXml = value;
                break;
            case AnimationPreset.ChangeLineColor:
                animation.PreservedLineBehaviorXml = value;
                break;
            case AnimationPreset.ChangeColor:
            case AnimationPreset.ColorPulse:
            case AnimationPreset.ColorWave:
                animation.PreservedColorBehaviorXml = value;
                break;
        }
    }

    private static bool ScaleBehaviorEquals(AnimationScaleBehavior? left, AnimationScaleBehavior? right) =>
        left?.FromX == right?.FromX
        && left?.FromY == right?.FromY
        && left?.ToX == right?.ToX
        && left?.ToY == right?.ToY
        && left?.ByX == right?.ByX
        && left?.ByY == right?.ByY
        && left?.ZoomContents == right?.ZoomContents;

    private static AnimationPaneEffectOptionDescriptor EffectOption(
        string id,
        string displayText,
        AnimationDirection direction)
        => new(id, displayText, direction, false);

    private static AnimationPaneEffectOptionDescriptor EffectSubtypeOption(
        string id,
        string displayText,
        string effectSubtype)
        => new(id, displayText, null, false, EffectSubtype: effectSubtype);

    private static IEnumerable<AnimationPaneEffectOptionDescriptor> BuildWheelSpokeOptions(int? authoredCount)
    {
        var counts = new SortedSet<int> { 1, 2, 3, 4, 8 };
        if (authoredCount is > 0)
            counts.Add(authoredCount.Value);
        var selected = authoredCount is > 0 ? authoredCount.Value : 4;

        foreach (var count in counts)
        {
            yield return new AnimationPaneEffectOptionDescriptor(
                $"spokes-{count}",
                count == 1 ? "1 spoke" : $"{count} spokes",
                null,
                count == selected,
                count);
        }
    }

    private static int? ResolveWheelSpokeCount(ShapeAnimation animation) =>
        animation.Preset == AnimationPreset.Wheel
            ? animation.WheelSpokeCount is > 0 ? animation.WheelSpokeCount.Value : 4
            : null;
}
