using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PresentationPaneAccessibilityTests
{
    [StaFact]
    public void Wpf_live_panes_expose_the_shared_ordered_contract_and_refresh_state()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges);

        window.PaneAccessibilitySnapshotSerializationForTests.Should().Be(ExpectedInitialSnapshot());

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

        var shape = window.Editor.CurrentSlide!.Shapes.First();
        window.Editor.Select(shape.Id);
        var selection = window.PaneAccessibilitySnapshotForTests
            .Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SelectionPaneId);
        selection.ItemCount.Should().Be(1);
        selection.SelectedIndex.Should().Be(0);
        window.SelectionPaneItemsForAccessibilityTests.Should().ContainSingle();
        var selectionItem = window.SelectionPaneItemsForAccessibilityTests.Single();
        AutomationProperties.GetAutomationId(selectionItem).Should().Be("FreePSelectionPaneItemShape1");
        AutomationProperties.GetName(selectionItem).Should().Be(shape.Name);
        AutomationProperties.GetItemStatus(selectionItem).Should().Be("Selected; Order 1");

        window.Editor.AddAnimation(shape.Id, new ShapeAnimation
        {
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        var animation = window.PaneAccessibilitySnapshotForTests
            .Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId);
        animation.ItemCount.Should().Be(1);
        animation.SelectedIndex.Should().Be(0);
        window.AnimationPaneItemsForAccessibilityTests.Should().ContainSingle();
        var animationItem = window.AnimationPaneItemsForAccessibilityTests.Single();
        AutomationProperties.GetAutomationId(animationItem).Should().Be(
            PresentationPaneAccessibilityPlanner.ProjectItem(
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.AnimationPaneId,
                    index: 0,
                    shape.Name,
                    isSelected: true,
                    PresentationPaneAccessibilityPlanner.BuildAnimationKey(shape.Id, animationIndex: 0)))
            .AutomationId);
        AutomationProperties.GetName(animationItem).Should().Be(shape.Name);
        AutomationProperties.GetItemStatus(animationItem).Should().Be("Selected; Order 1");

        window.ShowReviewCommentsPane();
        window.AddComment("Accessible comment");
        window.SetSelectedReviewCommentIndexForTests(0);
        window.CommentsPaneItemsForAccessibilityTests.Should().ContainSingle();
        var commentItem = window.CommentsPaneItemsForAccessibilityTests.Single();
        var comment = window.LastCommentPanePlan!.Comments.Single();
        AutomationProperties.GetAutomationId(commentItem).Should().Be(
            PresentationPaneAccessibilityPlanner.ProjectItem(
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.CommentsPaneId,
                    index: 0,
                    comment.TextPreview,
                    isSelected: true,
                    comment.AccessibilityKey))
            .AutomationId);
        AutomationProperties.GetName(commentItem).Should().Be("Accessible comment");
        AutomationProperties.GetItemStatus(commentItem).Should().Be("Selected; Order 1");

        window.Editor.InsertSlide();
        var afterCurrentSlideChange = window.PaneAccessibilitySnapshotForTests;
        afterCurrentSlideChange.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SlidePaneId)
            .SelectedIndex.Should().Be(1);
        afterCurrentSlideChange.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SelectionPaneId)
            .ItemCount.Should().Be(1);
        afterCurrentSlideChange.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SelectionPaneId)
            .SelectedIndex.Should().Be(0);
        afterCurrentSlideChange.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId)
            .ItemCount.Should().Be(0);
        afterCurrentSlideChange.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId)
            .SelectedIndex.Should().Be(-1);
        window.Editor.AddSectionAtSlide(0, "Intro").Should().BeTrue();
        var slideItemIds = window.SlidePaneItemsForAccessibilityTests
            .Select(AutomationProperties.GetAutomationId)
            .ToArray();
        slideItemIds.Should().OnlyHaveUniqueItems();
        slideItemIds.Should().Contain("FreePSlidePaneItemSection1");
        slideItemIds.Should().Contain("FreePSlidePaneItemSlide1");
        slideItemIds.Should().Contain("FreePSlidePaneItemSlide2");

        slideItemIds.Should().Equal(
            "FreePSlidePaneItemSection1",
            "FreePSlidePaneItemSlide1",
            "FreePSlidePaneItemSlide2");
        var slideItems = window.SlidePaneItemsForAccessibilityTests;
        AutomationProperties.GetItemStatus(slideItems[0]).Should().Be("Not selected; Order 1");
        AutomationProperties.GetItemStatus(slideItems[1]).Should().Be("Not selected; Order 2");
        AutomationProperties.GetItemStatus(slideItems[2]).Should().Be("Active and selected; Order 3");

        window.Editor.SelectSlide(0);
        var reorderedSlideItems = window.SlidePaneItemsForAccessibilityTests;
        reorderedSlideItems.Select(AutomationProperties.GetAutomationId).Should().Equal(slideItemIds);
        AutomationProperties.GetItemStatus(reorderedSlideItems[0]).Should().Be("Not selected; Order 1");
        AutomationProperties.GetItemStatus(reorderedSlideItems[1]).Should().Be("Active and selected; Order 2");
        AutomationProperties.GetItemStatus(reorderedSlideItems[2]).Should().Be("Not selected; Order 3");
    }

    private static string ExpectedInitialSnapshot() => string.Join(
        Environment.NewLine,
        new[]
        {
            "00|slide-pane|FreePSlidePane|Slides|Navigate slides and sections.|Visible|1|0",
            "01|notes-pane|FreePNotesPane|Notes|Read or edit notes for the current slide.|Visible|1|-1",
            "02|comments-pane|FreePCommentsPane|Comments|Review comments for the current slide.|Hidden|0|-1",
            "03|accessibility-pane|FreePAccessibilityPane|Accessibility|Review accessibility issues and details.|Hidden|0|-1",
            "04|alt-text-pane|FreePAltTextPane|Alt Text|Edit alternative text for the selected object.|Hidden|3|-1",
            "05|reading-order-pane|FreePReadingOrderPane|Reading Order|Review and reorder objects for assistive reading.|Hidden|1|-1",
            "06|proofing-pane|FreePProofingPane|Spelling|Review spelling and proofing suggestions.|Hidden|0|-1",
            "07|media-caption-pane|FreePMediaCaptionPane|Media Captions|Edit captions and transcripts for media.|Hidden|0|-1",
            "08|smartart-text-pane|FreePSmartArtTextPane|SmartArt Text Pane|Edit the SmartArt outline and structure.|Hidden|0|-1",
            "09|selection-pane|FreePSelectionPane|Selection Pane|Select, rename, hide, and reorder objects.|Hidden|1|-1",
            "10|animation-pane|FreePAnimationPane|Animation Pane|Review and edit slide animations.|Hidden|0|-1",
        });
}
