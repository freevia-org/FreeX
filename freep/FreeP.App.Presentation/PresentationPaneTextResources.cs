using FreeP.App.Localization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationMediaPlaybackStartOptionPlan(
    MediaPlaybackStartMode Mode,
    string Label);

public sealed record PresentationSmartArtTextPaneActionText(
    SmartArtNodeEditKind Kind,
    string Label,
    string ToolTip);

public sealed record PresentationSmartArtTextPaneChromeText(
    string Heading,
    string ToggleAssistant,
    string ReplacePicture,
    string RemovePicture,
    string Apply,
    string Close,
    IReadOnlyList<PresentationSmartArtTextPaneActionText> OutlineActions);

public sealed record SlideSectionNamePromptText(
    string Title,
    string Label,
    string AcceptText,
    string CancelText);

public enum AnimationPaneControlKind
{
    EffectOptions,
    WheelSpokes,
    Trigger,
    Duration,
    Delay,
    Repeat,
    AutoReverse,
    SmoothStart,
    SmoothEnd,
    MoveEarlier,
    MoveLater,
    RemoveAnimation,
    ParagraphBuild,
    EditMotionPath,
}

public sealed record AnimationPaneControlOptionPlan(
    string Id,
    string Label);

public sealed record AnimationPaneControlDescriptor(
    string Id,
    AnimationPaneControlKind Kind,
    string Label,
    string ToolTip,
    IReadOnlyList<AnimationPaneControlOptionPlan> Options,
    string? ValidationMessage = null)
{
    public IReadOnlyList<string> OptionLabels => Options.Select(option => option.Label).ToArray();
}

public sealed record AnimationPaneControlSchemaPlan(
    string Heading,
    IReadOnlyList<AnimationPaneControlDescriptor> Controls)
{
    public AnimationPaneControlDescriptor GetRequired(AnimationPaneControlKind kind) =>
        Controls.Single(control => control.Kind == kind);
}

/// <summary>
/// Localized renderer-neutral text for FreeP workarea panes.
/// </summary>
public static class PresentationPaneTextResources
{
    public static string AccessibilityHeading => Loc.Get("Pane_Accessibility_Heading");
    public static string AnimationPaneHeading => Loc.Get("Pane_Animation_Heading");
    public static string AnimationPaneEmptyMessage => Loc.Get("Pane_Animation_EmptyMessage");
    public static string AnimationPaneSelectRowMessage => Loc.Get("Pane_Animation_SelectRowMessage");
    public static string AnimationEffectOptions => Loc.Get("Pane_Animation_EffectOptions");
    public static string AnimationWheelSpokes => Loc.Get("Pane_Animation_WheelSpokes");
    public static string AnimationTrigger => Loc.Get("Pane_Animation_Trigger");
    public static string AnimationDuration => Loc.Get("Pane_Animation_Duration");
    public static string AnimationDurationSeconds => Loc.Get("Pane_Animation_DurationSeconds");
    public static string AnimationDelay => Loc.Get("Pane_Animation_Delay");
    public static string AnimationDelaySeconds => Loc.Get("Pane_Animation_DelaySeconds");
    public static string AnimationRepeat => Loc.Get("Pane_Animation_Repeat");
    public static string AnimationRepeatCount => Loc.Get("Pane_Animation_RepeatCount");
    public static string AnimationAutoReverse => Loc.Get("Pane_Animation_AutoReverse");
    public static string AnimationAutoReverseToolTip => Loc.Get("Pane_Animation_AutoReverseToolTip");
    public static string AnimationSmoothStart => Loc.Get("Pane_Animation_SmoothStart");
    public static string AnimationSmoothEnd => Loc.Get("Pane_Animation_SmoothEnd");
    public static string AnimationMoveEarlier => Loc.Get("Pane_Animation_MoveEarlier");
    public static string AnimationMoveLater => Loc.Get("Pane_Animation_MoveLater");
    public static string AnimationRemove => Loc.Get("Pane_Animation_Remove");
    public static string AnimationParagraphBuild => Loc.Get("Pane_Animation_ParagraphBuild");
    public static string AnimationParagraphBuildAllAtOnce => Loc.Get("Pane_Animation_ParagraphBuildAllAtOnce");
    public static string AnimationEditMotionPath => Loc.Get("Pane_Animation_EditMotionPath");
    public static string AnimationEditMotionPathToolTip => Loc.Get("Pane_Animation_EditMotionPathToolTip");
    public static string AnimationRepeatIndefinitely => Loc.Get("Pane_Animation_RepeatIndefinitely");
    public static string AnimationMissing => Loc.Get("Pane_Animation_Validation_MissingAnimation");
    public static string AnimationInvalidTrigger => Loc.Get("Pane_Animation_Validation_InvalidTrigger");
    public static string AnimationInvalidDuration => Loc.Get("Pane_Animation_Validation_InvalidDuration");
    public static string AnimationInvalidDelay => Loc.Get("Pane_Animation_Validation_InvalidDelay");
    public static string AnimationInvalidRepeat => Loc.Get("Pane_Animation_Validation_InvalidRepeat");
    public static string AnimationInvalidEasing => Loc.Get("Pane_Animation_Validation_InvalidEasing");
    public static string AnimationMissingEffectOption => Loc.Get("Pane_Animation_Validation_MissingEffectOption");
    public static string AnimationUnsupportedEffectOption => Loc.Get("Pane_Animation_Validation_UnsupportedEffectOption");
    public static string AnimationInvalidEffectOption => Loc.Get("Pane_Animation_Validation_InvalidEffectOption");
    public static string AnimationParagraphBuildDisabled => Loc.Get("Pane_Animation_Validation_ParagraphBuildDisabled");
    public static string AnimationParagraphBuildInvalidXml => Loc.Get("Pane_Animation_Validation_ParagraphBuildInvalidXml");
    public static string AnimationInvalidReorder => Loc.Get("Pane_Animation_Validation_InvalidReorder");
    public static string AnimationInvalidRemove => Loc.Get("Pane_Animation_Validation_InvalidRemove");
    public static string AnimationPreview => Loc.Get("Pane_Animation_Playback_Preview");
    public static string AnimationPlayFromSelected => Loc.Get("Pane_Animation_Playback_PlayFromSelected");
    public static string AnimationPlayAll => Loc.Get("Pane_Animation_Playback_PlayAll");
    public static string AnimationStop => Loc.Get("Pane_Animation_Playback_Stop");
    public static string AnimationPreviewCurrentSlideToolTip => Loc.Get("Pane_Animation_Playback_PreviewCurrentSlideToolTip");
    public static string AnimationPlayFromSelectedToolTip => Loc.Get("Pane_Animation_Playback_PlayFromSelectedToolTip");
    public static string AnimationPlayAllToolTip => Loc.Get("Pane_Animation_Playback_PlayAllToolTip");
    public static string AnimationStopToolTip => Loc.Get("Pane_Animation_Playback_StopToolTip");
    public static string AnimationNoAnimationsToPreview => Loc.Get("Pane_Animation_Playback_NoAnimationsToPreview");
    public static string AnimationNoAnimationsToPlay => Loc.Get("Pane_Animation_Playback_NoAnimationsToPlay");
    public static string AnimationSelectRowToPlay => Loc.Get("Pane_Animation_Playback_SelectRowToPlay");
    public static string AnimationPreviewAlreadyRunning => Loc.Get("Pane_Animation_Playback_AlreadyRunning");
    public static string AnimationNoPreviewRunning => Loc.Get("Pane_Animation_Playback_NoPreviewRunning");
    public static string AnimationPreviewStopped => Loc.Get("Pane_Animation_Playback_PreviewStopped");
    public static string AnimationPlayingAll => Loc.Get("Pane_Animation_Playback_PlayingAll");

    public static string MediaCaptionsHeading => Loc.Get("Pane_MediaCaptions_Heading");
    public static string PlaybackVolume => Loc.Get("Pane_Media_PlaybackVolume");
    public static string ApplyVolume => Loc.Get("Pane_Media_ApplyVolume");
    public static string PlaybackStart => Loc.Get("Pane_Media_PlaybackStart");
    public static string LoopUntilStopped => Loc.Get("Pane_Media_LoopUntilStopped");
    public static string ShowWhenStopped => Loc.Get("Pane_Media_ShowWhenStopped");
    public static string RewindAfterPlaying => Loc.Get("Pane_Media_RewindAfterPlaying");
    public static string PlayFullScreen => Loc.Get("Pane_Media_PlayFullScreen");
    public static string StopAfterSlides => Loc.Get("Pane_Media_StopAfterSlides");
    public static string ApplyPlayback => Loc.Get("Pane_Media_ApplyPlayback");
    public static string TrimStartMilliseconds => Loc.Get("Pane_Media_TrimStartMilliseconds");
    public static string TrimEndMilliseconds => Loc.Get("Pane_Media_TrimEndMilliseconds");
    public static string FadeInMilliseconds => Loc.Get("Pane_Media_FadeInMilliseconds");
    public static string FadeOutMilliseconds => Loc.Get("Pane_Media_FadeOutMilliseconds");
    public static string ApplyTiming => Loc.Get("Pane_Media_ApplyTiming");
    public static string MediaBookmarks => Loc.Get("Pane_Media_Bookmarks");
    public static string BookmarkName => Loc.Get("Pane_Media_BookmarkName");
    public static string BookmarkTimeMilliseconds => Loc.Get("Pane_Media_BookmarkTimeMilliseconds");
    public static string AddBookmark => Loc.Get("Pane_Media_AddBookmark");
    public static string ReplaceBookmark => Loc.Get("Pane_Media_ReplaceBookmark");
    public static string DeleteBookmark => Loc.Get("Pane_Media_DeleteBookmark");
    public static string AltTextHeading => Loc.Get("Pane_AltText_Heading");
    public static string ReadingOrderHeading => Loc.Get("Pane_ReadingOrder_Heading");
    public static string ReadingOrderSelectedItem => Loc.Get("Pane_ReadingOrder_SelectedItem");
    public static string ProofingHeading => Loc.Get("Pane_Proofing_Heading");
    public static string ProofingSelectedIssue => Loc.Get("Pane_Proofing_SelectedIssue");
    public static string NewCommentDefault => Loc.Get("Pane_Comments_NewCommentDefault");
    public static string NewReplyDefault => Loc.Get("Pane_Comments_NewReplyDefault");
    public static string NewCommentCommand => Loc.Get("Pane_Comments_NewCommentCommand");
    public static string ReplyCommand => Loc.Get("Pane_Comments_ReplyCommand");
    public static string CommentsEmptyMessage => Loc.Get("Pane_Comments_EmptyMessage");

    public static PresentationSmartArtTextPaneChromeText BuildSmartArtTextPaneChrome() =>
        new(
            Loc.Get("Pane_SmartArt_Heading"),
            Loc.Get("Pane_SmartArt_ToggleAssistant"),
            Loc.Get("Pane_SmartArt_ReplacePicture"),
            Loc.Get("Pane_SmartArt_RemovePicture"),
            Loc.Get("Pane_SmartArt_Apply"),
            Loc.Get("Pane_SmartArt_Close"),
            [
                SmartArtAction(SmartArtNodeEditKind.AddSiblingAfter, "AddSibling"),
                SmartArtAction(SmartArtNodeEditKind.AddChild, "AddChild"),
                SmartArtAction(SmartArtNodeEditKind.Remove, "Remove"),
                SmartArtAction(SmartArtNodeEditKind.MoveUp, "MoveUp"),
                SmartArtAction(SmartArtNodeEditKind.MoveDown, "MoveDown"),
                SmartArtAction(SmartArtNodeEditKind.Promote, "Promote"),
                SmartArtAction(SmartArtNodeEditKind.Demote, "Demote"),
                SmartArtAction(SmartArtNodeEditKind.AddAssistant, "AddAssistant"),
            ]);

    public static IReadOnlyList<AnimationPaneControlOptionPlan> AnimationTriggerOptions =>
    [
        new("on-click", Loc.Get("Ribbon_Option_AnimationTriggerOnClick_Label")),
        new("with-previous", Loc.Get("Ribbon_Option_AnimationTriggerWithPrevious_Label")),
        new("after-previous", Loc.Get("Ribbon_Option_AnimationTriggerAfterPrevious_Label")),
    ];

    public static IReadOnlyList<AnimationPaneControlOptionPlan> AnimationRepeatOptions =>
    [
        new("1", "1"),
        new("2", "2"),
        new("3", "3"),
        new("4", "4"),
        new("indefinitely", AnimationRepeatIndefinitely),
    ];

    public static AnimationPaneControlSchemaPlan BuildAnimationPaneControlSchema() =>
        new(AnimationPaneHeading,
        [
            Control("effect-options", AnimationPaneControlKind.EffectOptions,
                AnimationEffectOptions, AnimationEffectOptions),
            Control("wheel-spokes", AnimationPaneControlKind.WheelSpokes,
                AnimationWheelSpokes, AnimationWheelSpokes),
            Control("trigger", AnimationPaneControlKind.Trigger,
                AnimationTrigger, AnimationTrigger, AnimationTriggerOptions, AnimationInvalidTrigger),
            Control("duration", AnimationPaneControlKind.Duration,
                AnimationDuration, AnimationDurationSeconds, validationMessage: AnimationInvalidDuration),
            Control("delay", AnimationPaneControlKind.Delay,
                AnimationDelay, AnimationDelaySeconds, validationMessage: AnimationInvalidDelay),
            Control("repeat", AnimationPaneControlKind.Repeat,
                AnimationRepeat, AnimationRepeatCount, AnimationRepeatOptions, AnimationInvalidRepeat),
            Control("auto-reverse", AnimationPaneControlKind.AutoReverse,
                AnimationAutoReverse, AnimationAutoReverseToolTip),
            Control("smooth-start", AnimationPaneControlKind.SmoothStart,
                AnimationSmoothStart, AnimationSmoothStart, validationMessage: AnimationInvalidEasing),
            Control("smooth-end", AnimationPaneControlKind.SmoothEnd,
                AnimationSmoothEnd, AnimationSmoothEnd, validationMessage: AnimationInvalidEasing),
            Control("move-earlier", AnimationPaneControlKind.MoveEarlier,
                AnimationMoveEarlier, AnimationMoveEarlier),
            Control("move-later", AnimationPaneControlKind.MoveLater,
                AnimationMoveLater, AnimationMoveLater),
            Control("remove", AnimationPaneControlKind.RemoveAnimation,
                AnimationRemove, AnimationRemove),
            Control("paragraph-build", AnimationPaneControlKind.ParagraphBuild,
                AnimationParagraphBuild, AnimationParagraphBuild),
            Control("edit-motion-path", AnimationPaneControlKind.EditMotionPath,
                AnimationEditMotionPath, AnimationEditMotionPathToolTip),
        ]);

    public static IReadOnlyList<PresentationMediaPlaybackStartOptionPlan> MediaPlaybackStartOptions =>
    [
        new(MediaPlaybackStartMode.InClickSequence, Loc.Get("Pane_Media_StartOnClick")),
        new(MediaPlaybackStartMode.Automatically, Loc.Get("Pane_Media_StartAutomatically")),
    ];

    public static string BuildMediaCaptionsHeading(string? shapeName) =>
        string.IsNullOrWhiteSpace(shapeName)
            ? MediaCaptionsHeading
            : Loc.Format("Pane_MediaCaptions_HeadingFormat", shapeName);

    public static string BuildAltTextHeading(string? shapeName) =>
        string.IsNullOrWhiteSpace(shapeName)
            ? AltTextHeading
            : Loc.Format("Pane_AltText_HeadingFormat", shapeName);

    public static string BuildReadingOrderHeading(int slideIndex, int itemCount) =>
        Loc.Format("Pane_ReadingOrder_HeadingFormat", slideIndex + 1, itemCount);

    public static string BuildReadingOrderSelectedMessage(string shapeName) =>
        Loc.Format("Pane_ReadingOrder_SelectedFormat", shapeName);

    public static string BuildReadingOrderItemTitle(int readingOrderIndex, string shapeName) =>
        Loc.Format("Pane_ReadingOrder_ItemTitleFormat", readingOrderIndex + 1, shapeName);

    public static string BuildReadingOrderItemMetadata(string shapeTypeLabel, int nestingDepth) =>
        Loc.Format("Pane_ReadingOrder_ItemMetadataFormat", shapeTypeLabel, nestingDepth);

    public static string BuildReadingOrderSelectToolTip(string shapeName) =>
        Loc.Format("Pane_ReadingOrder_SelectToolTipFormat", shapeName);

    public static string BuildProofingHeading(int issueCount) =>
        Loc.Format("Pane_Proofing_HeadingFormat", issueCount);

    public static string BuildProofingSelectedMessage(
        string slideDisplay,
        string text,
        string suggestedReplacement) =>
        Loc.Format("Pane_Proofing_SelectedFormat", slideDisplay, text, suggestedReplacement);

    public static string BuildSmartArtRowRole(bool isAssistant, int level) =>
        isAssistant
            ? Loc.Get("Pane_SmartArt_AssistantRow")
            : level == 0
                ? Loc.Get("Pane_SmartArt_RootRow")
                : Loc.Format("Pane_SmartArt_LevelRowFormat", level + 1);

    public static SlideSectionNamePromptText BuildSlideSectionNamePrompt(
        SlideSectionActionKind kind) =>
        new(
            kind == SlideSectionActionKind.RenameSection
                ? Loc.Get("Pane_SlideSection_RenamePromptTitle")
                : Loc.Get("Pane_SlideSection_AddPromptTitle"),
            Loc.Get("Pane_SlideSection_NamePromptLabel"),
            Loc.Get("Pane_SlideSection_NamePromptAccept"),
            Loc.Get("Pane_SlideSection_NamePromptCancel"));

    public static string BuildAnimationPaneHeading(int slideNumber, int animationCount) =>
        Loc.Format("Pane_Animation_HeadingFormat", slideNumber, animationCount);

    public static string BuildAnimationPaneSelectedMessage(string shapeName, string effectText) =>
        Loc.Format("Pane_Animation_SelectedMessageFormat", shapeName, effectText);

    public static string BuildAnimationPlayingFromMessage(int animationNumber) =>
        Loc.Format("Pane_Animation_Playback_PlayingFromFormat", animationNumber);

    private static AnimationPaneControlDescriptor Control(
        string id,
        AnimationPaneControlKind kind,
        string label,
        string toolTip,
        IReadOnlyList<AnimationPaneControlOptionPlan>? options = null,
        string? validationMessage = null) =>
        new(id, kind, label, toolTip, options ?? Array.Empty<AnimationPaneControlOptionPlan>(), validationMessage);

    private static PresentationSmartArtTextPaneActionText SmartArtAction(
        SmartArtNodeEditKind kind,
        string resourceSuffix) =>
        new(
            kind,
            Loc.Get($"Pane_SmartArt_{resourceSuffix}"),
            Loc.Get($"Pane_SmartArt_{resourceSuffix}_ToolTip"));
}
