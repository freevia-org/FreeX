using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPaneAccessibilityPlannerTests
{
    [Fact]
    public void WpfAndAvaloniaUseTheSameOrderedPaneSnapshotContract()
    {
        var states = new[]
        {
            new PresentationPaneAccessibilityState(PresentationPaneAccessibilityPlanner.AnimationPaneId, true, 2, 1),
            new PresentationPaneAccessibilityState(PresentationPaneAccessibilityPlanner.CommentsPaneId, true, 3),
            new PresentationPaneAccessibilityState(PresentationPaneAccessibilityPlanner.NotesPaneId, true),
        };

        var snapshot = PresentationPaneAccessibilityPlanner.BuildSnapshot(states);

        snapshot.Select(entry => entry.PaneId).Should().Equal(
            "slide-pane",
            "notes-pane",
            "comments-pane",
            "accessibility-pane",
            "alt-text-pane",
            "reading-order-pane",
            "proofing-pane",
            "media-caption-pane",
            "smartart-text-pane",
            "selection-pane",
            "animation-pane");
        snapshot.Single(entry => entry.PaneId == "animation-pane").Should().Match<PresentationPaneAccessibilitySnapshotEntry>(
            entry => entry.State == "Visible" && entry.ItemCount == 2 && entry.SelectedIndex == 1);
        snapshot.Single(entry => entry.PaneId == "selection-pane").State.Should().Be("Hidden");
        snapshot.Select(entry => entry.AutomationId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ItemDescriptorsRemainStableWhenRenderedByEitherHost()
    {
        var first = PresentationPaneAccessibilityPlanner.Item(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            0,
            "Title");
        var second = PresentationPaneAccessibilityPlanner.Item(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            0,
            "Title");

        first.Should().Be(second);
        first.AutomationId.Should().Be("FreePSelectionPaneItem1");
        first.HelpText.Should().Be("Selection Pane item 1.");
    }

    [Fact]
    public void Pane_projection_owns_shared_metadata_status_and_keyboard_decisions()
    {
        var projection = PresentationPaneAccessibilityPlanner.ProjectPane(
            PresentationPaneAccessibilityPlanner.NotesPaneId,
            isVisible: true,
            itemCount: 2,
            selectedIndex: 1);

        projection.State.Should().Be(new PresentationPaneAccessibilityState(
            PresentationPaneAccessibilityPlanner.NotesPaneId,
            true,
            2,
            1));
        projection.AutomationId.Should().Be("FreePNotesPane");
        projection.Name.Should().Be("Notes");
        projection.HelpText.Should().Be("Read or edit notes for the current slide.");
        projection.ItemStatus.Should().Be("Visible; Order 2");
        projection.IsKeyboardNavigationEnabled.Should().BeTrue();
        projection.KeyboardOrder.Should().Be(2);

        PresentationPaneAccessibilityPlanner.ProjectPane(
                PresentationPaneAccessibilityPlanner.NotesPaneId,
                isVisible: false)
            .Should().Match<PresentationPaneAccessibilityPaneProjection>(pane =>
                pane.ItemStatus == "Hidden; Order 2" &&
                !pane.IsKeyboardNavigationEnabled &&
                pane.KeyboardOrder == 2);
    }

    [Fact]
    public void Item_projection_owns_shared_status_formatting()
    {
        var selected = PresentationPaneAccessibilityPlanner.ProjectItem(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            0,
            "Title",
            "Selected");
        var unqualified = PresentationPaneAccessibilityPlanner.ProjectItem(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            1,
            "Subtitle",
            stableKey: "subtitle");

        selected.Should().Be(new PresentationPaneAccessibilityItemProjection(
            "FreePSelectionPaneItem1",
            "Title",
            "Selection Pane item 1.",
            "Selected; Order 1"));
        unqualified.AutomationId.Should().Be("FreePSelectionPaneItemsubtitle");
        unqualified.ItemStatus.Should().Be("Order 2");
    }

    [Fact]
    public void Item_plans_own_selection_vocabulary_and_stable_key_families()
    {
        var slide = PresentationPaneAccessibilityPlanner.PlanSlideItem(
            index: 4,
            slideIndex: 2,
            name: "Slide 3",
            isSelected: true,
            isActive: true);
        var section = PresentationPaneAccessibilityPlanner.PlanSectionItem(
            index: 1,
            sectionIndex: 6,
            name: "Results");
        var shape = PresentationPaneAccessibilityPlanner.PlanItem(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            index: 0,
            name: "Chart 1",
            isSelected: false,
            stableKey: PresentationPaneAccessibilityPlanner.BuildShapeKey(42));

        slide.State.Should().Be(PresentationPaneAccessibilityPlanner.ActiveAndSelectedState);
        slide.StableKey.Should().Be("Slide3");
        PresentationPaneAccessibilityPlanner.ProjectItem(slide).AutomationId
            .Should().Be("FreePSlidePaneItemSlide3");
        section.State.Should().Be(PresentationPaneAccessibilityPlanner.NotSelectedState);
        section.StableKey.Should().Be("Section7");
        shape.StableKey.Should().Be("Shape42");
        PresentationPaneAccessibilityPlanner.BuildAnimationKey(42, 1).Should().Be("Animation42-2");
    }

    [Fact]
    public void Session_owns_live_state_and_keeps_last_update_for_each_pane()
    {
        var session = new PresentationPaneAccessibilitySession();

        session.UpdatePane(PresentationPaneAccessibilityPlanner.CommentsPaneId, true, 3, 1);
        session.UpdatePane(PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1, 0);
        session.UpdatePane(PresentationPaneAccessibilityPlanner.CommentsPaneId, false, -2, 9);

        var snapshot = session.BuildSnapshot();
        snapshot.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.NotesPaneId)
            .Should().Match<PresentationPaneAccessibilitySnapshotEntry>(entry =>
                entry.State == "Visible" && entry.ItemCount == 1 && entry.SelectedIndex == 0);
        snapshot.Single(entry => entry.PaneId == PresentationPaneAccessibilityPlanner.CommentsPaneId)
            .Should().Match<PresentationPaneAccessibilitySnapshotEntry>(entry =>
                entry.State == "Hidden" && entry.ItemCount == 0 && entry.SelectedIndex == -1);
        session.SerializeSnapshot().Should().Contain("02|comments-pane|FreePCommentsPane|Comments|")
            .And.Contain("|Hidden|0|-1");
    }

    [Fact]
    public void Snapshot_serialization_is_ordered_and_normalizes_invalid_selection()
    {
        var snapshot = PresentationPaneAccessibilityPlanner.SerializeSnapshot(
        [
            new(PresentationPaneAccessibilityPlanner.AnimationPaneId, true, 2, 99),
            new(PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1, 0),
        ]);

        snapshot.Should().Contain("00|slide-pane|FreePSlidePane|Slides|");
        snapshot.Should().Contain("01|notes-pane|FreePNotesPane|Notes|Read or edit notes for the current slide.|Visible|1|0");
        snapshot.Should().Contain("10|animation-pane|FreePAnimationPane|Animation Pane|Review and edit slide animations.|Visible|2|-1");
        snapshot.IndexOf("01|notes-pane", StringComparison.Ordinal)
            .Should().BeLessThan(snapshot.IndexOf("10|animation-pane", StringComparison.Ordinal));
    }
}
