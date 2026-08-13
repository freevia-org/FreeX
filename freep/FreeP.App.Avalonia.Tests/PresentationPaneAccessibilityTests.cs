using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresentationPaneAccessibilityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static PresentationPaneAccessibilityTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task Avalonia_live_panes_expose_the_shared_ordered_contract_and_refresh_state()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
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
            window.ShowAnimationPane();
            var open = window.PaneAccessibilitySnapshotForTests;
            open.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.SelectionPaneId)
                .State.Should().Be("Visible");
            open.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.AnimationPaneId)
                .State.Should().Be("Visible");
            AutomationProperties.GetAutomationId(window.SelectionPaneForAccessibilityTests)
                .Should().Be("FreePSelectionPane");
            AutomationProperties.GetAutomationId(window.CommentsPaneForAccessibilityTests)
                .Should().Be("FreePCommentsPane");

            window.HideAnimationPane();
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
            window.ShowAnimationPane();
            window.AnimationPaneItemsForAccessibilityTests.Should().ContainSingle();
            var animationItem = window.AnimationPaneItemsForAccessibilityTests.Single();
            AutomationProperties.GetAutomationId(animationItem).Should().Be("FreePAnimationPaneItemAnimation1-1");
            AutomationProperties.GetName(animationItem).Should().Be(shape.Name);
            AutomationProperties.GetItemStatus(animationItem).Should().Be("Selected; Order 1");

            window.ShowReviewCommentsPane();
            window.AddComment("Accessible comment");
            window.SetSelectedReviewCommentIndexForTests(0);
            window.CommentsPaneItemsForAccessibilityTests.Should().ContainSingle();
            var commentItem = window.CommentsPaneItemsForAccessibilityTests.Single();
            AutomationProperties.GetAutomationId(commentItem).Should().Be("FreePCommentsPaneItemSlide1Comment1");
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
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_live_item_control_receives_stable_metadata()
    {
        await Session.Dispatch(() =>
        {
            var item = new Border();
            PresentationPaneAccessibilityAdapter.ApplyItem(
                item,
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.SelectionPaneId,
                    0,
                    "Title",
                    isSelected: true));

            AutomationProperties.GetAutomationId(item).Should().Be("FreePSelectionPaneItem1");
            AutomationProperties.GetName(item).Should().Be("Title");
            AutomationProperties.GetHelpText(item).Should().Be("Selection Pane item 1.");
            AutomationProperties.GetItemStatus(item).Should().Be("Selected; Order 1");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Representative_live_panes_follow_shared_keyboard_focus_order()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.ShowReviewCommentsPane();
            window.ShowSelectionPane();
            window.ShowAnimationPane();

            var panes = new Control[]
            {
                window.SlidePaneForAccessibilityTests,
                window.NotesPaneForAccessibilityTests,
                window.CommentsPaneForAccessibilityTests,
                window.SelectionPaneForAccessibilityTests,
                window.AnimationPaneForAccessibilityTests,
            };
            var descriptors = new[]
            {
                PresentationPaneAccessibilityPlanner.Get(PresentationPaneAccessibilityPlanner.SlidePaneId),
                PresentationPaneAccessibilityPlanner.Get(PresentationPaneAccessibilityPlanner.NotesPaneId),
                PresentationPaneAccessibilityPlanner.Get(PresentationPaneAccessibilityPlanner.CommentsPaneId),
                PresentationPaneAccessibilityPlanner.Get(PresentationPaneAccessibilityPlanner.SelectionPaneId),
                PresentationPaneAccessibilityPlanner.Get(PresentationPaneAccessibilityPlanner.AnimationPaneId),
            };

            panes.Select(AutomationProperties.GetName).Should().Equal("Slides", "Notes", "Comments", "Selection Pane", "Animation Pane");
            panes.Select(pane => pane.Focusable).Should().OnlyContain(value => value);
            panes.Select(pane => pane.IsTabStop).Should().OnlyContain(value => value);
            panes.Select(pane => pane.TabIndex).Should().Equal(descriptors.Select(descriptor => descriptor.Order + 1));

            window.HideReviewCommentsPane();
            window.CommentsPaneForAccessibilityTests.Focusable.Should().BeFalse();
            window.CommentsPaneForAccessibilityTests.IsTabStop.Should().BeFalse();
        }, CancellationToken.None);
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
