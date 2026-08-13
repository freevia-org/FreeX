using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Drawing;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host.Backstage;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelHyperlink = FreeP.Core.Model.Hyperlink;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal bool InvokeReviewCommentPaneMentionActionForTests(string tag, string? candidateLabel = null)
    {
        var button = EnumerateCommentPaneButtons(_commentListPanel)
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (button is null)
            return false;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var item = button.ContextMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(candidate => candidateLabel is null ||
                string.Equals(candidate.Header as string, candidateLabel, StringComparison.Ordinal));
        if (item is null)
            return candidateLabel is null;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }

    internal Border? AnimPaneHostForTest => _animPaneHost;

    internal IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> PaneAccessibilitySnapshotForTests =>
        _paneAccessibility.BuildSnapshot();

    internal string PaneAccessibilitySnapshotSerializationForTests =>
        _paneAccessibility.SerializeSnapshot();

    internal ContextMenu BuildChartContextMenuForTests(ChartSubtargetHit hit) =>
        BuildDomainContextMenu(_domainContextMenuSession.BuildChart(hit));

    internal ContextMenu? BuildTableContextMenuForTests(uint shapeId)
    {
        var plan = _domainContextMenuSession.BuildTable(shapeId);
        return plan is null ? null : BuildDomainContextMenu(plan);
    }

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

    internal SmartArtNodeEditResult? ApplySmartArtTextPanePictureForTests(
        byte[] imageBytes,
        string contentType = "image/png",
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPanePicture(imageBytes, contentType);
    }

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

}
