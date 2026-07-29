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
