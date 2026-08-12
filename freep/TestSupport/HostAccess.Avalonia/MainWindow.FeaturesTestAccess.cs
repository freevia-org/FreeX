using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
#if FREEP_WINDOWS_CAPTURE
using Free.Shared.AppServices.Windows;
#endif
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Avalonia.Backstage;
using FreeP.App.Avalonia.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal PresentationCommentPanePlan SetSelectedReviewCommentIndexForTests(int? commentIndex)
        => _reviewWorkflowSession.SetSelectedReviewCommentIndex(commentIndex);

    internal PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForTests(
        string? query = null,
        string? currentAuthor = null,
        string? currentInitials = null)
        => _reviewWorkflowSession.BuildCommentMentionPickerPlan(query, currentAuthor, currentInitials);

    internal PresentationCommentMentionInsertionPlan InsertCommentMentionForTests(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
        => _reviewWorkflowSession.InsertCommentMention(text, caretIndex, candidate);

    internal PresentationCommentMutationPlan InsertMentionInSelectedCommentForTests(
        int caretIndex,
        PresentationCommentMentionCandidate? candidate,
        string? author = null,
        string? initials = null)
        => _reviewWorkflowSession.InsertMentionInSelectedComment(
            caretIndex,
            candidate,
            author,
            initials);

    internal AnimationPanePlaybackSessionPlan ExecuteAnimationPanePlaybackControlForTests(
        AnimationPanePlaybackControlKind controlKind)
    {
        var control = RefreshAnimationPaneTimelinePlan(_animationPaneSession.SelectedAnimationIndex)
            .PlaybackControls
            .First(candidate => candidate.Kind == controlKind);
        return ExecuteAnimationPanePlaybackControl(control, startPreview: false);
    }

    internal AnimationPaneParagraphBuildMutationPlan ToggleParagraphBuildForTests(uint shapeId)
    {
        var plan = _animationPaneSession.ToggleParagraphBuild(shapeId);
        RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
        return plan;
    }

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneTriggerEditForTests(
        int animationIndex,
        int selectedTriggerIndex)
        => ApplyAnimationPaneTriggerEdit(animationIndex, selectedTriggerIndex);

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneDurationEditForTests(
        int animationIndex,
        string text)
        => ApplyAnimationPaneDurationEdit(animationIndex, text);

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneDelayEditForTests(
        int animationIndex,
        string text)
        => ApplyAnimationPaneDelayEdit(animationIndex, text);

    internal AnimationPaneEasingMutationPlan ApplyAnimationPaneEasingEditForTests(
        int animationIndex,
        string accelerationText,
        string decelerationText)
        => ApplyAnimationPaneEasingEdit(animationIndex, accelerationText, decelerationText);

    internal AnimationPaneEffectOptionMutationPlan ApplyAnimationPaneEffectOptionEditForTests(
        int animationIndex,
        string optionId)
        => ApplyAnimationPaneEffectOptionEdit(animationIndex, optionId);

    internal AnimationPaneReorderMutationPlan MoveAnimationPaneItemForTests(int animationIndex, int offset)
        => MoveAnimationPaneItem(animationIndex, offset);

    internal AnimationPaneRemoveMutationPlan RemoveAnimationPaneItemForTests(int animationIndex) =>
        RemoveAnimationPaneItem(animationIndex);

    internal SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistantForTests(string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ToggleSmartArtTextPaneAssistant();
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneEditForTests(
        SmartArtNodeEditKind kind,
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPaneAction(kind);
    }

    internal SmartArtColorApplyResult ApplySmartArtColorPresetForTests(SmartArtColorPreset preset) =>
        ApplySmartArtColorPreset(preset);

    internal SmartArtLayoutApplyResult ApplySmartArtLayoutPresetForTests(SmartArtLayoutPreset preset) =>
        ApplySmartArtLayoutPreset(preset);

    internal SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePresetForTests(SmartArtQuickStylePreset preset) =>
        ApplySmartArtQuickStylePreset(preset);

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneKeyboardRouteForTests(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPaneKeyboardRoute(key, modifiers);
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPanePictureForTests(
        byte[] imageBytes,
        string contentType = "image/png",
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPanePicture(imageBytes, contentType);
    }

}
