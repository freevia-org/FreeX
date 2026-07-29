using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class PresentationPaneAccessibilityTests
{
    [StaFact]
    public void Wpf_live_panes_expose_the_shared_ordered_contract_and_refresh_state()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);

        var initial = window.PaneAccessibilitySnapshotForTests;
        initial.Select(entry => entry.PaneId).Should().Equal(
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            PresentationPaneAccessibilityPlanner.NotesPaneId,
            PresentationPaneAccessibilityPlanner.CommentsPaneId,
            PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
            PresentationPaneAccessibilityPlanner.AltTextPaneId,
            PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
            PresentationPaneAccessibilityPlanner.ProofingPaneId,
            PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
            PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            PresentationPaneAccessibilityPlanner.AnimationPaneId);

        AutomationProperties.GetAutomationId(window.NotesPaneForAccessibilityTests)
            .Should().Be("FreePNotesPane");
        AutomationProperties.GetName(window.NotesPaneForAccessibilityTests)
            .Should().Be("Notes");
        AutomationProperties.GetHelpText(window.NotesPaneForAccessibilityTests)
            .Should().Be("Read or edit notes for the current slide.");
        AutomationProperties.GetItemStatus(window.NotesPaneForAccessibilityTests)
            .Should().Contain("Visible; Order 2");

        window.ShowReviewCommentsPane();
        window.PaneAccessibilitySnapshotForTests
            .Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.CommentsPaneId)
            .State.Should().Be("Visible");
        window.HideReviewCommentsPane();
        window.PaneAccessibilitySnapshotForTests
            .Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.CommentsPaneId)
            .State.Should().Be("Hidden");

        window.ShowSelectionPane();
        window.ToggleAnimationPane();
        var open = window.PaneAccessibilitySnapshotForTests;
        open.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SelectionPaneId)
            .State.Should().Be("Visible");
        open.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId)
            .State.Should().Be("Visible");
        AutomationProperties.GetAutomationId(window.SelectionPaneForAccessibilityTests)
            .Should().Be("FreePSelectionPane");
        AutomationProperties.GetAutomationId(window.AnimationPaneForAccessibilityTests!)
            .Should().Be("FreePAnimationPane");

        window.ToggleAnimationPane();
        window.PaneAccessibilitySnapshotForTests
            .Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId)
            .State.Should().Be("Hidden");
    }

    [StaFact]
    public void Wpf_live_selection_and_animation_items_receive_stable_item_metadata()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);

        var item = new Border();
        PresentationPaneAccessibilityAdapter.ApplyItem(
            item,
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            0,
            "Title",
            "Selected");

        AutomationProperties.GetAutomationId(item).Should().Be("FreePSelectionPaneItem1");
        AutomationProperties.GetName(item).Should().Be("Title");
        AutomationProperties.GetHelpText(item).Should().Be("Selection Pane item 1.");
        AutomationProperties.GetItemStatus(item).Should().Be("Selected; Order 1");
    }
}
