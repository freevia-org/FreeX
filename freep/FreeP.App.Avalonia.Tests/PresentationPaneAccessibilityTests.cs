using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;

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
                PresentationPaneAccessibilityPlanner.SelectionPaneId,
                0,
                "Title",
                "Selected");

            AutomationProperties.GetAutomationId(item).Should().Be("FreePSelectionPaneItem1");
            AutomationProperties.GetName(item).Should().Be("Title");
            AutomationProperties.GetHelpText(item).Should().Be("Selection Pane item 1.");
            AutomationProperties.GetItemStatus(item).Should().Be("Selected; Order 1");
        }, CancellationToken.None);
    }
}
