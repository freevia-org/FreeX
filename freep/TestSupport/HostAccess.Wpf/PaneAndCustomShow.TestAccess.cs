using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class CustomShowDialog
{
    public void SelectCustomShowSlideForTests(int index) => _formSession.SelectSlide(index);
    public void MoveSelectedCustomShowSlideUpForTests() => _controller.MoveSelectedSlide(-1);
    public void MoveSelectedCustomShowSlideDownForTests() => _controller.MoveSelectedSlide(1);
    public void RemoveSelectedCustomShowSlideForTests() => _controller.RemoveSelectedSlide();
    public void AddCustomShowSlideOccurrenceForTests(string slideId) =>
        _controller.AddSlideOccurrence(slideId);
    public SlideShowCustomShowDragReorderPlan DragReorderCustomShowSlideForTests(
        int sourceSlideIndex,
        int targetDropIndex) =>
        _controller.Reorder(sourceSlideIndex, targetDropIndex);

    internal void PrepareMissingNameForTests()
    {
        _nameBox.Text = string.Empty;
        _controller.Create();
    }
}

public sealed partial class ChartExSeriesLayoutDialog
{
    internal int SelectedSeriesIndexForTests => _session.SelectedSeriesIndex;
    internal string? SelectedLayoutIdForTests => _session.LayoutIdAt(SelectedLayoutIndex);

    internal void ApplyForTests()
    {
        if (!_session.TryApply(SelectedLayoutIndex, out var error))
            throw new ArgumentException(error);
    }
}

internal sealed partial class SelectionPane
{
    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests =>
        _items.Children.OfType<FrameworkElement>().ToArray();
}

public sealed partial class SlidePane
{
    internal ContextMenu BuildSlideContextMenuForTests(int slideIndex) =>
        BuildSlideContextMenu(slideIndex);

    internal ContextMenu BuildSectionContextMenuForTests(SlidePaneEntry entry) =>
        BuildSectionContextMenu(entry);

    internal IReadOnlyList<string?> SlidePaneThumbnailAutomationNamesForTests => _list.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is int)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<string?> SlidePaneSectionHeaderAutomationNamesForTests => _list.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is SectionHeaderTag)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests => _list.Items
        .OfType<FrameworkElement>()
        .Where(item => AutomationProperties.GetAutomationId(item)
            .StartsWith("FreePSlidePaneItem", StringComparison.Ordinal))
        .ToArray();

    internal bool ToggleSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _workarea.Presentation.Sections.Count)
            return false;
        _workarea.ToggleSlidePaneSection(SlidePanePlanner.GetSectionIdentity(
            _workarea.Presentation.Sections[sectionIndex], sectionIndex));
        return true;
    }

    internal bool TryApplySlideSectionActionForTests(
        SlideSectionActionKind kind,
        int slideIndex = -1,
        int sectionIndex = -1,
        string? promptedName = null)
    {
        var command = kind switch
        {
            SlideSectionActionKind.AddSection => FreePContextMenuCommand.AddSection,
            SlideSectionActionKind.RenameSection => FreePContextMenuCommand.RenameSection,
            SlideSectionActionKind.RemoveSection => FreePContextMenuCommand.RemoveSection,
            SlideSectionActionKind.RemoveAllSections => FreePContextMenuCommand.RemoveAllSections,
            _ => default,
        };
        var execution = _workarea.BuildSlidePaneContextCommandRoute(command, slideIndex, sectionIndex)
            .SectionExecution;
        return execution is not null && _workarea.ExecuteSlidePaneSectionAction(execution, promptedName);
    }

    internal ListBox NativeListForTests => _list;
    internal Button NewSlideButtonForTests => _newSlideButton;
}

public sealed partial class AnimationPane
{
    internal AnimationPaneTimelinePlan CurrentTimelinePlanForTest => CurrentTimelinePlan;

    internal AnimationPaneEffectOptionMutationPlan ApplyAnimationPaneEffectOptionEditForTest(
        int animationIndex,
        string optionId) =>
        ApplyEffectOptionMutation(animationIndex, optionId);

    internal AnimationPaneEasingMutationPlan ApplyAnimationPaneEasingEditForTest(
        int animationIndex,
        string? accelerationText,
        string? decelerationText) =>
        ApplyEasingMutation(animationIndex, accelerationText, decelerationText);

    internal AnimationPanePlaybackSessionPlan? CurrentPlaybackSessionPlanForTest => _session.Playback;
    internal AnimationPanePlaybackWorkflowEvidencePlan? CurrentPlaybackWorkflowEvidencePlanForTest =>
        _session.PlaybackWorkflowEvidence;
    internal IReadOnlyList<AnimationPanePlaybackControlDescriptor> CurrentPlaybackControlsForTest =>
        BuildTimelinePlan().PlaybackControls;
    internal AnimationPaneWorkflowViewPlan CurrentWorkflowViewPlanForTest => BuildWorkflowViewPlan();

    internal AnimationPaneWorkflowEvidencePlan CurrentWorkflowEvidencePlanForTest
    {
        get
        {
            BuildTimelinePlan();
            return _session.WorkflowEvidence!;
        }
    }

    internal AnimationPaneControlSchemaPlan ControlSchemaForTests => _session.ControlSchema;
    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests =>
        _listPanel.Children.OfType<FrameworkElement>().ToArray();

    internal AnimationPanePlaybackSessionPlan ExecutePlaybackControlForTest(
        AnimationPanePlaybackControlKind controlKind)
    {
        var control = BuildTimelinePlan()
            .PlaybackControls
            .First(candidate => candidate.Kind == controlKind);
        return ExecutePlaybackControl(control, invokePreview: false);
    }

    internal AnimationPaneReorderMutationPlan MoveAnimationForTest(int animationIndex, int offset) =>
        ApplyReorderMutation(animationIndex, offset);

    internal AnimationPaneRemoveMutationPlan RemoveAnimationForTest(int animationIndex) =>
        ApplyRemoveMutation(animationIndex);
}
